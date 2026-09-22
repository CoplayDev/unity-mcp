"""Async Unity Test Runner jobs: start + poll."""
from __future__ import annotations

import asyncio
from collections import OrderedDict
from dataclasses import dataclass
from itertools import count
import logging
import ntpath
import posixpath
import time
from typing import Annotated, Any, Literal

from fastmcp import Context
from mcp.types import ToolAnnotations
from pydantic import BaseModel

from models import MCPResponse
from core.config import config
from services.registry import mcp_for_unity_tool
from services.tools import get_unity_instance_from_context
from services.tools.preflight import preflight
import transport.unity_transport as unity_transport
from transport.legacy.unity_connection import async_send_command_with_retry
from transport.plugin_hub import PluginHub
from utils import focus_nudge
from utils.focus_nudge import nudge_unity_focus, should_nudge

logger = logging.getLogger(__name__)

# Strong references to background fire-and-forget tasks to prevent premature GC.
_background_tasks: set[asyncio.Task] = set()
_active_nudge_task: asyncio.Task | None = None
_MAX_JOB_NUDGES = 3
_NUDGE_STATE_TTL_S = 3600.0
_MAX_NUDGE_STATES = 256
_poll_observation_order = count()


@dataclass
class _JobNudgeState:
    last_seen: float
    last_update: int = 0
    completed: int = 0
    test_started: int = 0
    test_finished: int = 0
    attempts: int = 0
    last_attempt: float | None = None
    task: asyncio.Task | None = None
    run_in_background: bool = False
    editor_is_focused: bool = True
    latest_observation: int = -1


_nudge_states: OrderedDict[tuple[str, str, str, str], _JobNudgeState] = OrderedDict()
_terminal_nudge_jobs: OrderedDict[tuple[str, str, str, str], float] = OrderedDict()


async def _get_unity_project_path(unity_instance: str | None, user_id: str | None = None) -> str | None:
    """Get the project root path for a Unity instance (for focus nudging).

    Args:
        unity_instance: Unity instance hash or "Name@hash" format or None

    Returns:
        Exact absolute project root path, or None if the identity is unresolved.
    """
    if not unity_instance:
        return None

    try:
        # Parse Name@hash format if present (middleware stores instances as "Name@hash")
        target_hash = unity_instance
        if "@" in target_hash:
            _, _, target_hash = target_hash.rpartition("@")
        if not target_hash:
            return None

        if unity_transport._is_http_transport():
            registry = PluginHub._registry
            if not registry or (config.http_remote_hosted and not user_id):
                return None
            session_id = await registry.get_session_id_by_hash(target_hash, user_id=user_id)
            session = await registry.get_session(session_id) if session_id else None
            path = session.project_path if session else None
        else:
            from transport.legacy.stdio_port_registry import stdio_port_registry
            instances = stdio_port_registry.get_instances()
            matches = [instance for instance in instances if (
                instance.id == unity_instance or instance.hash == target_hash
            )]
            path = matches[0].path if len(matches) == 1 else None
            # Stdio status files contain Application.dataPath, ending in Assets.
            if path:
                path_module = ntpath if ntpath.splitdrive(path)[0] or "\\" in path else posixpath
                path = path_module.normpath(path)
                if path_module.basename(path).lower() == "assets":
                    path = path_module.dirname(path)
        if not path:
            return None
        path_module = ntpath if ntpath.splitdrive(path)[0] or "\\" in path else posixpath
        if path_module is ntpath and not ntpath.splitdrive(path)[0]:
            return None
        return path_module.normpath(path) if path_module.isabs(path) else None

    except Exception as e:
        # Re-raise cancellation errors so task cancellation propagates
        if isinstance(e, asyncio.CancelledError):
            raise
        logger.debug(f"Could not get Unity project path: {e}")
        return None


async def _update_job_nudge(
    unity_instance: str | None, user_id: str | None, job_id: str,
    data: dict[str, Any], *, wait: bool, observation_order: int | None = None,
) -> None:
    """Share a bounded, monotonic no-progress budget across every poll of a job."""
    global _active_nudge_task
    if not unity_instance or (config.http_remote_hosted and not user_id):
        return
    if observation_order is None:
        observation_order = next(_poll_observation_order)
    instance_hash = unity_instance.rpartition("@")[2]
    key = (config.transport_mode, user_id or "", instance_hash, job_id)
    now = time.monotonic()
    for terminal_key, observed_at in list(_terminal_nudge_jobs.items()):
        if now - observed_at > _NUDGE_STATE_TTL_S:
            del _terminal_nudge_jobs[terminal_key]
    for old_key, old_state in list(_nudge_states.items()):
        if now - old_state.last_seen > _NUDGE_STATE_TTL_S and not (
            old_state.task and not old_state.task.done()
        ):
            del _nudge_states[old_key]
    if data.get("status") in ("succeeded", "failed", "cancelled"):
        _terminal_nudge_jobs[key] = now
        _terminal_nudge_jobs.move_to_end(key)
        while len(_terminal_nudge_jobs) > _MAX_NUDGE_STATES:
            _terminal_nudge_jobs.popitem(last=False)
        state = _nudge_states.pop(key, None)
        if state and state.task and not state.task.done():
            state.task.cancel()
        return
    if data.get("status") != "running":
        return
    progress = data.setdefault("progress", {}) or {}
    data["progress"] = progress
    if key in _terminal_nudge_jobs:
        progress["focus_nudge_status"] = "terminal_already_observed"
        return
    state = _nudge_states.get(key)
    if state is None:
        # Do not evict an observed running job: that would renew its spent budget.
        if len(_nudge_states) >= _MAX_NUDGE_STATES:
            progress["focus_nudge_status"] = "tracking_limit"
            return
        state = _JobNudgeState(last_seen=now)
        _nudge_states[key] = state
    state.last_seen = now
    _nudge_states.move_to_end(key)
    advanced = False
    for attr, value in (
        ("last_update", data.get("last_update_unix_ms")),
        ("completed", progress.get("completed")),
        ("test_started", progress.get("current_test_started_unix_ms")),
        ("test_finished", progress.get("last_finished_unix_ms")),
    ):
        if isinstance(value, int) and not isinstance(value, bool) and value > getattr(state, attr):
            setattr(state, attr, value)
            advanced = True
    if advanced:
        state.attempts = 0
        state.last_attempt = None
    # Focus can change without any test progress. Order UI observations by when
    # their polls started, not by test timestamps or by reply arrival order.
    if observation_order > state.latest_observation:
        state.latest_observation = observation_order
        state.run_in_background = progress.get("run_in_background") is True
        state.editor_is_focused = progress.get("editor_is_focused", True)
    progress["focus_nudge_attempts"] = state.attempts
    progress["focus_nudge_limit"] = _MAX_JOB_NUDGES
    if state.attempts >= _MAX_JOB_NUDGES:
        progress["stuck_suspected"] = True
        progress["focus_nudge_status"] = "attempt_limit_reached"
        return
    if state.run_in_background:
        progress["focus_nudge_status"] = "background_execution_enabled"
        return
    if not should_nudge(
        status="running", editor_is_focused=state.editor_is_focused,
        last_update_unix_ms=state.last_update or None,
        current_time_ms=int(time.time() * 1000),
    ):
        return
    if _active_nudge_task is not None and not _active_nudge_task.done():
        return
    interval = min(focus_nudge._BASE_NUDGE_INTERVAL_S * (2 ** state.attempts), focus_nudge._MAX_NUDGE_INTERVAL_S)
    if state.last_attempt is not None and now - state.last_attempt < interval:
        return
    observed_progress = (state.last_update, state.completed, state.test_started, state.test_finished)
    observed_reservation = (state.attempts, state.last_attempt)
    project_path = await _get_unity_project_path(unity_instance, user_id)
    if not project_path:
        progress["focus_nudge_status"] = "project_path_unavailable"
        return
    # Resolution can await HTTP registry locks; recheck after another poll may
    # have completed the job, reported progress, or reserved the desktop.
    if _nudge_states.get(key) is not state or observed_progress != (
        state.last_update, state.completed, state.test_started, state.test_finished
    ) or observed_reservation != (state.attempts, state.last_attempt) or (
        state.run_in_background or state.editor_is_focused
    ) or (
        _active_nudge_task is not None and not _active_nudge_task.done()
    ):
        return
    state.attempts += 1
    state.last_attempt = time.monotonic()
    progress["focus_nudge_attempts"] = state.attempts
    progress["focus_nudge_status"] = "scheduled"

    async def perform_nudge() -> None:
        await nudge_unity_focus(
            unity_project_path=project_path, force=True,
            focus_duration_s=focus_nudge._DEFAULT_FOCUS_DURATION_S,
        )

    task = asyncio.create_task(perform_nudge())
    state.task = task
    _active_nudge_task = task
    _background_tasks.add(task)

    def finish(done: asyncio.Task) -> None:
        global _active_nudge_task
        _background_tasks.discard(done)
        if _active_nudge_task is done:
            _active_nudge_task = None
        if state.task is done:
            state.task = None
        if not done.cancelled() and done.exception() is not None:
            logger.warning("Test job focus nudge failed: %s", done.exception())

    task.add_done_callback(finish)
    if wait:
        # Another poll may cancel the child after observing terminal status.
        # Child cancellation must not cancel this request; caller cancellation
        # still propagates through gather and cancels the nudge for focus restore.
        await asyncio.gather(task, return_exceptions=True)


class RunTestsSummary(BaseModel):
    total: int
    passed: int
    failed: int
    skipped: int
    durationSeconds: float
    resultState: str


class RunTestsTestResult(BaseModel):
    name: str
    fullName: str
    state: str
    durationSeconds: float
    message: str | None = None
    stackTrace: str | None = None
    output: str | None = None


class RunTestsResult(BaseModel):
    mode: str
    summary: RunTestsSummary
    results: list[RunTestsTestResult] | None = None


class RunTestsStartData(BaseModel):
    job_id: str
    status: str
    mode: str | None = None
    include_details: bool | None = None
    include_failed_tests: bool | None = None


class RunTestsStartResponse(MCPResponse):
    data: RunTestsStartData | None = None


class TestJobFailure(BaseModel):
    full_name: str | None = None
    message: str | None = None


class TestJobProgress(BaseModel):
    completed: int | None = None
    total: int | None = None
    current_test_full_name: str | None = None
    current_test_started_unix_ms: int | None = None
    last_finished_test_full_name: str | None = None
    last_finished_unix_ms: int | None = None
    stuck_suspected: bool | None = None
    editor_is_focused: bool | None = None
    run_in_background: bool | None = None
    focus_nudge_attempts: int | None = None
    focus_nudge_limit: int | None = None
    focus_nudge_status: str | None = None
    blocked_reason: str | None = None
    failures_so_far: list[TestJobFailure] | None = None
    failures_capped: bool | None = None


class GetTestJobData(BaseModel):
    job_id: str
    status: str
    mode: str | None = None
    started_unix_ms: int | None = None
    finished_unix_ms: int | None = None
    last_update_unix_ms: int | None = None
    progress: TestJobProgress | None = None
    error: str | None = None
    result: RunTestsResult | None = None


class GetTestJobResponse(MCPResponse):
    data: GetTestJobData | None = None


@mcp_for_unity_tool(
    group="testing",
    description="Starts a Unity test run asynchronously and returns a job_id immediately. Poll with get_test_job for progress.",
    annotations=ToolAnnotations(
        title="Run Tests",
        destructiveHint=True,
    ),
)
async def run_tests(
    ctx: Context,
    mode: Annotated[Literal["EditMode", "PlayMode"],
                    "Unity test mode to run"] = "EditMode",
    test_names: Annotated[list[str] | str,
                          "Full names of specific tests to run"] | None = None,
    group_names: Annotated[list[str] | str,
                           "Same as test_names, except it allows for Regex"] | None = None,
    category_names: Annotated[list[str] | str,
                              "NUnit category names to filter by"] | None = None,
    assembly_names: Annotated[list[str] | str,
                              "Assembly names to filter tests by"] | None = None,
    include_failed_tests: Annotated[bool,
                                    "Include details for failed/skipped tests only (default: false)"] = False,
    include_details: Annotated[bool,
                               "Include details for all tests (default: false)"] = False,
    init_timeout: Annotated[int | None,
                            "Initialization timeout in milliseconds. PlayMode tests may need longer "
                            "due to domain reload (default: 15000). Recommended: 120000 for PlayMode."] = None,
    clear_stuck: Annotated[bool,
                           "Clear an orphaned running job instead of starting a run. Use when a job "
                           "was lost to a domain reload and is blocking every subsequent run."] = False,
) -> RunTestsStartResponse | MCPResponse:
    unity_instance = await get_unity_instance_from_context(ctx)

    # Runs before both the init_timeout check and preflight on purpose: neither is relevant to
    # clearing, and requires_no_tests would reject the very call that exists to clear the
    # orphaned job blocking it.
    if clear_stuck:
        response = await unity_transport.send_with_unity_instance(
            async_send_command_with_retry,
            unity_instance,
            "run_tests",
            {"clear_stuck": True},
        )
        if isinstance(response, dict):
            return MCPResponse(**response)
        return MCPResponse(success=False, error=str(response))

    if init_timeout is not None and init_timeout <= 0:
        return MCPResponse(success=False, error="init_timeout must be a positive integer (milliseconds) or None")

    gate = await preflight(ctx, requires_no_tests=True, wait_for_no_compile=True, refresh_if_dirty=True)
    if isinstance(gate, MCPResponse):
        return gate

    def _coerce_string_list(value) -> list[str] | None:
        if value is None:
            return None
        if isinstance(value, str):
            return [value] if value.strip() else None
        if isinstance(value, list):
            result = [str(v).strip() for v in value if v and str(v).strip()]
            return result if result else None
        return None

    params: dict[str, Any] = {"mode": mode}
    if (t := _coerce_string_list(test_names)):
        params["testNames"] = t
    if (g := _coerce_string_list(group_names)):
        params["groupNames"] = g
    if (c := _coerce_string_list(category_names)):
        params["categoryNames"] = c
    if (a := _coerce_string_list(assembly_names)):
        params["assemblyNames"] = a
    if include_failed_tests:
        params["includeFailedTests"] = True
    if include_details:
        params["includeDetails"] = True
    if init_timeout is not None and init_timeout > 0:
        params["initTimeout"] = init_timeout

    response = await unity_transport.send_with_unity_instance(
        async_send_command_with_retry,
        unity_instance,
        "run_tests",
        params,
    )

    if isinstance(response, dict):
        if not response.get("success", True):
            return MCPResponse(**response)
        return RunTestsStartResponse(**response)
    return MCPResponse(success=False, error=str(response))


@mcp_for_unity_tool(
    group="testing",
    description="Polls an async Unity test job by job_id.",
    annotations=ToolAnnotations(
        title="Get Test Job",
        readOnlyHint=True,
        destructiveHint=False,
        idempotentHint=True,
        openWorldHint=False,
    ),
)
async def get_test_job(
    ctx: Context,
    job_id: Annotated[str, "Job id returned by run_tests"],
    include_failed_tests: Annotated[bool,
                                    "Include details for failed/skipped tests only (default: false)"] = False,
    include_details: Annotated[bool,
                               "Include details for all tests (default: false)"] = False,
    wait_timeout: Annotated[int | None,
                            "If set, wait up to this many seconds for tests to complete before returning. "
                            "Reduces polling frequency and avoids client-side loop detection. "
                            "Recommended: 30-60 seconds. Returns immediately if tests complete sooner."] = None,
) -> GetTestJobResponse | MCPResponse:
    unity_instance = await get_unity_instance_from_context(ctx)
    user_id = await ctx.get_state("user_id") if config.http_remote_hosted else None

    params: dict[str, Any] = {"job_id": job_id}
    if include_failed_tests:
        params["includeFailedTests"] = True
    if include_details:
        params["includeDetails"] = True

    async def _fetch_status() -> tuple[Any, int]:
        observation_order = next(_poll_observation_order)
        response = await unity_transport.send_with_unity_instance(
            async_send_command_with_retry,
            unity_instance,
            "get_test_job",
            params,
        )
        return response, observation_order

    # If wait_timeout is specified, poll server-side until complete or timeout
    if wait_timeout and wait_timeout > 0:
        deadline = asyncio.get_event_loop().time() + wait_timeout
        poll_interval = 2.0  # Poll Unity every 2 seconds

        while True:
            response, observation_order = await _fetch_status()

            if not isinstance(response, dict):
                return MCPResponse(success=False, error=str(response))

            if not response.get("success", True):
                return MCPResponse(**response)

            # Check if tests are done
            data = response.get("data", {})
            status = data.get("status", "")
            await _update_job_nudge(
                unity_instance, user_id, job_id, data, wait=True, observation_order=observation_order,
            )
            if status in ("succeeded", "failed", "cancelled"):
                return GetTestJobResponse(**response)

            # Check timeout
            remaining = deadline - asyncio.get_event_loop().time()
            if remaining <= 0:
                # Timeout reached, return current status
                return GetTestJobResponse(**response)

            # Wait before next poll (but don't exceed remaining time)
            await asyncio.sleep(min(poll_interval, remaining))
    
    # No wait_timeout - return immediately (original behavior)
    response, observation_order = await _fetch_status()
    if not isinstance(response, dict):
        return MCPResponse(success=False, error=str(response))
    if not response.get("success", True):
        return MCPResponse(**response)

    data = response.get("data", {})
    await _update_job_nudge(
        unity_instance, user_id, job_id, data, wait=False, observation_order=observation_order,
    )
    return GetTestJobResponse(**response)
