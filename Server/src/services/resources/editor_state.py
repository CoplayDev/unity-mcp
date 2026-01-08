import os
import time
from typing import Any

from fastmcp import Context
from pydantic import BaseModel

from models import MCPResponse
from services.registry import mcp_for_unity_resource
from services.tools import get_unity_instance_from_context
from services.state.external_changes_scanner import external_changes_scanner
import transport.unity_transport as unity_transport
from transport.legacy.unity_connection import async_send_command_with_retry


class EditorStateUnity(BaseModel):
    instance_id: str | None = None
    unity_version: str | None = None
    project_id: str | None = None
    platform: str | None = None
    is_batch_mode: bool | None = None


class EditorStatePlayMode(BaseModel):
    is_playing: bool | None = None
    is_paused: bool | None = None
    is_changing: bool | None = None


class EditorStateActiveScene(BaseModel):
    path: str | None = None
    guid: str | None = None
    name: str | None = None


class EditorStateEditor(BaseModel):
    is_focused: bool | None = None
    play_mode: EditorStatePlayMode | None = None
    active_scene: EditorStateActiveScene | None = None


class EditorStateActivity(BaseModel):
    phase: str | None = None
    since_unix_ms: int | None = None
    reasons: list[str] | None = None


class EditorStateCompilation(BaseModel):
    is_compiling: bool | None = None
    is_domain_reload_pending: bool | None = None
    last_compile_started_unix_ms: int | None = None
    last_compile_finished_unix_ms: int | None = None
    last_domain_reload_before_unix_ms: int | None = None
    last_domain_reload_after_unix_ms: int | None = None


class EditorStateRefresh(BaseModel):
    is_refresh_in_progress: bool | None = None
    last_refresh_requested_unix_ms: int | None = None
    last_refresh_finished_unix_ms: int | None = None


class EditorStateAssets(BaseModel):
    is_updating: bool | None = None
    external_changes_dirty: bool | None = None
    external_changes_last_seen_unix_ms: int | None = None
    external_changes_dirty_since_unix_ms: int | None = None
    external_changes_last_cleared_unix_ms: int | None = None
    refresh: EditorStateRefresh | None = None


class EditorStateLastRun(BaseModel):
    finished_unix_ms: int | None = None
    result: str | None = None
    counts: Any | None = None


class EditorStateTests(BaseModel):
    is_running: bool | None = None
    mode: str | None = None
    current_job_id: str | None = None
    started_unix_ms: int | None = None
    started_by: str | None = None
    last_run: EditorStateLastRun | None = None


class EditorStateTransport(BaseModel):
    unity_bridge_connected: bool | None = None
    last_message_unix_ms: int | None = None


class EditorStateAdvice(BaseModel):
    ready_for_tools: bool | None = None
    blocking_reasons: list[str] | None = None
    recommended_retry_after_ms: int | None = None
    recommended_next_action: str | None = None


class EditorStateStaleness(BaseModel):
    age_ms: int | None = None
    is_stale: bool | None = None


class EditorStateData(BaseModel):
    schema_version: str
    observed_at_unix_ms: int
    sequence: int
    unity: EditorStateUnity | None = None
    editor: EditorStateEditor | None = None
    activity: EditorStateActivity | None = None
    compilation: EditorStateCompilation | None = None
    assets: EditorStateAssets | None = None
    tests: EditorStateTests | None = None
    transport: EditorStateTransport | None = None
    advice: EditorStateAdvice | None = None
    staleness: EditorStateStaleness | None = None


def _now_unix_ms() -> int:
    return int(time.time() * 1000)


def _in_pytest() -> bool:
    # Avoid instance-discovery side effects during the Python integration test suite.
    return bool(os.environ.get("PYTEST_CURRENT_TEST"))


async def infer_single_instance_id(ctx: Context) -> str | None:
    """
    Best-effort: if exactly one Unity instance is connected, return its Name@hash id.
    This makes editor_state outputs self-describing even when no explicit active instance is set.
    """
    await ctx.info("If exactly one Unity instance is connected, return its Name@hash id.")

    try:
        transport = unity_transport._current_transport()
    except Exception:
        transport = None

    if transport == "http":
        # HTTP/WebSocket transport: derive from PluginHub sessions.
        try:
            from transport.plugin_hub import PluginHub

            sessions_data = await PluginHub.get_sessions()
            sessions = sessions_data.sessions if hasattr(
                sessions_data, "sessions") else {}
            if isinstance(sessions, dict) and len(sessions) == 1:
                session = next(iter(sessions.values()))
                project = getattr(session, "project", None)
                project_hash = getattr(session, "hash", None)
                if project and project_hash:
                    return f"{project}@{project_hash}"
        except Exception:
            return None
        return None

    # Stdio/TCP transport: derive from connection pool discovery.
    try:
        from transport.legacy.unity_connection import get_unity_connection_pool

        pool = get_unity_connection_pool()
        instances = pool.discover_all_instances(force_refresh=False)
        if isinstance(instances, list) and len(instances) == 1:
            inst = instances[0]
            inst_id = getattr(inst, "id", None)
            return str(inst_id) if inst_id else None
    except Exception:
        return None
    return None


def _enrich_advice_and_staleness(state_v2: dict[str, Any]) -> dict[str, Any]:
    now_ms = _now_unix_ms()
    observed = state_v2.get("observed_at_unix_ms")
    try:
        observed_ms = int(observed)
    except Exception:
        observed_ms = now_ms

    age_ms = max(0, now_ms - observed_ms)
    # Conservative default: treat >2s as stale (covers common unfocused-editor throttling).
    is_stale = age_ms > 2000

    compilation = state_v2.get("compilation") or {}
    tests = state_v2.get("tests") or {}
    assets = state_v2.get("assets") or {}
    refresh = (assets.get("refresh") or {}) if isinstance(assets, dict) else {}

    blocking: list[str] = []
    if compilation.get("is_compiling") is True:
        blocking.append("compiling")
    if compilation.get("is_domain_reload_pending") is True:
        blocking.append("domain_reload")
    if tests.get("is_running") is True:
        blocking.append("running_tests")
    if refresh.get("is_refresh_in_progress") is True:
        blocking.append("asset_refresh")
    if is_stale:
        blocking.append("stale_status")

    ready_for_tools = len(blocking) == 0

    state_v2["advice"] = {
        "ready_for_tools": ready_for_tools,
        "blocking_reasons": blocking,
        "recommended_retry_after_ms": 0 if ready_for_tools else 500,
        "recommended_next_action": "none" if ready_for_tools else "retry_later",
    }
    state_v2["staleness"] = {"age_ms": age_ms, "is_stale": is_stale}
    return state_v2


@mcp_for_unity_resource(
    uri="mcpforunity://editor/state",
    name="editor_state",
    description="Canonical editor readiness snapshot. Includes advice and server-computed staleness.",
)
async def get_editor_state(ctx: Context) -> MCPResponse:
    unity_instance = get_unity_instance_from_context(ctx)

    response = await unity_transport.send_with_unity_instance(
        async_send_command_with_retry,
        unity_instance,
        "get_editor_state",
        {},
    )

    # If Unity returns a structured retry hint or error, surface it directly.
    if isinstance(response, dict) and not response.get("success", True):
        return MCPResponse(**response)

    state_v2 = response.get("data") if isinstance(
        response, dict) and isinstance(response.get("data"), dict) else {}
    state_v2.setdefault("schema_version", "unity-mcp/editor_state@2")
    state_v2.setdefault("observed_at_unix_ms", _now_unix_ms())
    state_v2.setdefault("sequence", 0)

    # Ensure the returned snapshot is clearly associated with the targeted instance.
    unity_section = state_v2.get("unity")
    if not isinstance(unity_section, dict):
        unity_section = {}
        state_v2["unity"] = unity_section
    current_instance_id = unity_section.get("instance_id")
    if current_instance_id in (None, ""):
        if unity_instance:
            unity_section["instance_id"] = unity_instance
        else:
            inferred = await infer_single_instance_id(ctx)
            if inferred:
                unity_section["instance_id"] = inferred

    # External change detection (server-side): compute per instance based on project root path.
    try:
        instance_id = unity_section.get("instance_id")
        if isinstance(instance_id, str) and instance_id.strip():
            from services.resources.project_info import get_project_info

            proj_resp = await get_project_info(ctx)
            proj = proj_resp.model_dump() if hasattr(
                proj_resp, "model_dump") else proj_resp
            proj_data = proj.get("data") if isinstance(proj, dict) else None
            project_root = proj_data.get("projectRoot") if isinstance(
                proj_data, dict) else None
            if isinstance(project_root, str) and project_root.strip():
                external_changes_scanner.set_project_root(
                    instance_id, project_root)

            ext = external_changes_scanner.update_and_get(instance_id)

            assets = state_v2.get("assets")
            if not isinstance(assets, dict):
                assets = {}
                state_v2["assets"] = assets
            assets["external_changes_dirty"] = bool(
                ext.get("external_changes_dirty", False))
            assets["external_changes_last_seen_unix_ms"] = ext.get(
                "external_changes_last_seen_unix_ms")
            assets["external_changes_dirty_since_unix_ms"] = ext.get(
                "dirty_since_unix_ms")
            assets["external_changes_last_cleared_unix_ms"] = ext.get(
                "last_cleared_unix_ms")
    except Exception:
        pass

    state_v2 = _enrich_advice_and_staleness(state_v2)

    try:
        if hasattr(EditorStateData, "model_validate"):
            validated = EditorStateData.model_validate(state_v2)
        else:
            validated = EditorStateData.parse_obj(
                state_v2)  # type: ignore[attr-defined]
        data = validated.model_dump() if hasattr(
            validated, "model_dump") else validated.dict()
    except Exception as e:
        return MCPResponse(
            success=False,
            error="invalid_editor_state",
            message=f"Editor state payload failed validation: {e}",
            data={"raw": state_v2},
        )

    return MCPResponse(success=True, message="Retrieved editor state.", data=data)
