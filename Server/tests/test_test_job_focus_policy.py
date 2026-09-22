"""Test-job focus budgets and identity routing without desktop activation."""

import asyncio
from collections import OrderedDict
from itertools import count
from types import SimpleNamespace
from unittest.mock import AsyncMock

import pytest

import services.tools.run_tests as mod


def snapshot(update=100, completed=0, **progress):
    return {"job_id": "job", "status": "running", "last_update_unix_ms": update,
            "progress": {"completed": completed, "editor_is_focused": False, **progress}}


@pytest.fixture
def policy(monkeypatch):
    monkeypatch.setattr(mod, "_nudge_states", OrderedDict())
    monkeypatch.setattr(mod, "_terminal_nudge_jobs", OrderedDict())
    monkeypatch.setattr(mod, "_background_tasks", set())
    monkeypatch.setattr(mod, "_active_nudge_task", None)
    monkeypatch.setattr(mod, "_poll_observation_order", count())
    monkeypatch.setattr(mod.config, "transport_mode", "stdio")
    monkeypatch.setattr(mod.config, "http_remote_hosted", False)
    monkeypatch.delenv("UNITY_MCP_DISABLE_FOCUS_NUDGE", raising=False)
    monkeypatch.setattr(mod.focus_nudge, "_BASE_NUDGE_INTERVAL_S", 0)
    monkeypatch.setattr(mod.focus_nudge, "_MAX_NUDGE_INTERVAL_S", 0)
    monkeypatch.setattr(mod, "_get_unity_project_path", AsyncMock(return_value=r"C:\Project"))
    nudge = AsyncMock(return_value=True)
    monkeypatch.setattr(mod, "nudge_unity_focus", nudge)
    return nudge


async def poll(data=None, instance="Game@hash", user=None, job="job", wait=True, observation_order=None):
    data = snapshot() if data is None else data
    await mod._update_job_nudge(instance, user, job, data, wait=wait, observation_order=observation_order)
    return data


async def drain():
    if mod._background_tasks:
        await asyncio.gather(*mod._background_tasks, return_exceptions=True)


@pytest.mark.asyncio
async def test_separate_polls_share_three_attempt_limit(policy):
    for _ in range(6):
        data = await poll(wait=False)
        await drain()
    assert policy.await_count == 3
    assert data["progress"]["focus_nudge_attempts"] == 3
    assert data["progress"]["stuck_suspected"] is True
    assert data["progress"]["focus_nudge_status"] == "attempt_limit_reached"


@pytest.mark.asyncio
async def test_failed_attempts_are_also_bounded(policy):
    policy.return_value = False
    for _ in range(5):
        await poll()
    assert policy.await_count == 3


@pytest.mark.asyncio
async def test_stale_alternating_pollers_cannot_renew_spent_budget(policy):
    for update in (200, 100, 200, 100, 200, 100):
        data = await poll(snapshot(update))
    assert policy.await_count == 3
    assert data["progress"]["focus_nudge_status"] == "attempt_limit_reached"
    data = await poll(snapshot(300))
    assert policy.await_count == 4
    assert data["progress"]["focus_nudge_attempts"] == 1


@pytest.mark.parametrize("progress", [
    {"completed": 1}, {"current_test_started_unix_ms": 200}, {"last_finished_unix_ms": 200},
])
@pytest.mark.asyncio
async def test_actual_test_progress_renews_only_its_job_budget(policy, progress):
    for _ in range(3):
        await poll(job="a")
        await poll(job="b")
    data = snapshot()
    data["progress"].update(progress)
    await poll(data, job="a")
    capped = await poll(job="b")
    assert policy.await_count == 7
    assert capped["progress"]["focus_nudge_status"] == "attempt_limit_reached"


@pytest.mark.asyncio
async def test_instance_and_user_budgets_are_isolated(policy):
    for _ in range(3):
        await poll(instance="Game@one", user="a")
    await poll(instance="Game@two", user="a")
    await poll(instance="Game@one", user="b")
    await poll(instance="one", user="a")  # Same canonical hash, same budget.
    assert policy.await_count == 5
    assert len(mod._nudge_states) == 3


@pytest.mark.asyncio
async def test_background_execution_skips_even_long_test_without_progress(policy):
    data = await poll(snapshot(run_in_background=True))
    policy.assert_not_awaited()
    assert data["progress"]["focus_nudge_status"] == "background_execution_enabled"
    assert data["progress"]["focus_nudge_attempts"] == 0


@pytest.mark.asyncio
async def test_old_editor_without_background_field_retains_bounded_nudges(policy):
    await poll(snapshot())
    policy.assert_awaited_once()
    assert policy.call_args.kwargs["force"] is True
    assert policy.call_args.kwargs["focus_duration_s"] == mod.focus_nudge._DEFAULT_FOCUS_DURATION_S


@pytest.mark.asyncio
async def test_opt_out_does_not_spend_job_attempts(policy, monkeypatch):
    monkeypatch.setenv("UNITY_MCP_DISABLE_FOCUS_NUDGE", "1")
    data = await poll()
    policy.assert_not_awaited()
    assert data["progress"]["focus_nudge_attempts"] == 0


@pytest.mark.asyncio
async def test_missing_identity_or_path_never_activates(policy):
    await poll(instance=None)
    assert not mod._nudge_states
    mod._get_unity_project_path.return_value = None
    data = await poll()
    policy.assert_not_awaited()
    assert data["progress"]["focus_nudge_attempts"] == 0
    assert data["progress"]["focus_nudge_status"] == "project_path_unavailable"


@pytest.mark.asyncio
async def test_one_active_task_does_not_spend_other_pollers_budgets(policy):
    started, release = asyncio.Event(), asyncio.Event()

    async def wait_for_release(**kwargs):
        started.set()
        await release.wait()
        return True

    policy.side_effect = wait_for_release
    await poll(wait=False)
    await asyncio.wait_for(started.wait(), 1)
    try:
        same = await poll(wait=False)
        other = await poll(instance="Other@other", wait=False)
        assert same["progress"]["focus_nudge_attempts"] == 1
        assert other["progress"]["focus_nudge_attempts"] == 0
        assert policy.await_count == 1
    finally:
        release.set()
        await drain()
    await poll(instance="Other@other")
    assert policy.await_count == 2


@pytest.mark.asyncio
async def test_terminal_status_cancels_active_nudge_and_removes_state(policy):
    started, release = asyncio.Event(), asyncio.Event()

    async def wait_forever(**kwargs):
        started.set()
        await release.wait()

    policy.side_effect = wait_forever
    await poll(wait=False)
    await asyncio.wait_for(started.wait(), 1)
    task = mod._active_nudge_task
    await poll({"status": "succeeded"})
    await drain()
    assert task.cancelled()
    assert not mod._nudge_states


@pytest.mark.asyncio
async def test_terminal_poll_does_not_cancel_another_waiting_poll(policy):
    started, release = asyncio.Event(), asyncio.Event()

    async def wait_forever(**kwargs):
        started.set()
        await release.wait()

    policy.side_effect = wait_forever
    waiting_poll = asyncio.create_task(poll(wait=True))
    await asyncio.wait_for(started.wait(), 1)
    await poll({"status": "succeeded"})
    await waiting_poll
    assert not waiting_poll.cancelled()
    assert not mod._nudge_states


@pytest.mark.asyncio
async def test_caller_cancellation_still_cancels_nudge(policy):
    started, release = asyncio.Event(), asyncio.Event()

    async def wait_forever(**kwargs):
        started.set()
        await release.wait()

    policy.side_effect = wait_forever
    waiting_poll = asyncio.create_task(poll(wait=True))
    await asyncio.wait_for(started.wait(), 1)
    child = mod._active_nudge_task
    waiting_poll.cancel()
    with pytest.raises(asyncio.CancelledError):
        await waiting_poll
    assert child.cancelled()


@pytest.mark.asyncio
async def test_stale_running_reply_after_terminal_does_not_recreate_budget(policy):
    await poll({"status": "succeeded"})
    stale = await poll()
    policy.assert_not_awaited()
    assert not mod._nudge_states
    assert stale["progress"]["focus_nudge_status"] == "terminal_already_observed"


@pytest.mark.parametrize("fresh_flags", [{"run_in_background": True}, {"editor_is_focused": True}])
@pytest.mark.asyncio
async def test_stale_unfocused_reply_cannot_override_safe_fresh_observation(policy, fresh_flags):
    await poll(snapshot(200, **fresh_flags), observation_order=2)
    await poll(snapshot(100), observation_order=0)
    await poll(snapshot(200), observation_order=1)
    policy.assert_not_awaited()


@pytest.mark.parametrize("safe_flags", [{"editor_is_focused": True}, {"run_in_background": True}])
@pytest.mark.asyncio
async def test_new_sequential_unfocused_observation_can_nudge_without_test_progress(policy, safe_flags):
    await poll(snapshot(200, **safe_flags))
    policy.assert_not_awaited()
    data = await poll(snapshot(200))
    policy.assert_awaited_once()
    assert data["progress"]["focus_nudge_attempts"] == 1


@pytest.mark.asyncio
async def test_actual_fetch_order_preserves_newer_flags_when_older_request_replies_late(policy, monkeypatch):
    started, release = asyncio.Event(), asyncio.Event()
    fetch_count = 0

    async def send(*args, **kwargs):
        nonlocal fetch_count
        index = fetch_count
        fetch_count += 1
        if index == 0:
            started.set()
            await release.wait()
        return {"success": True, "data": snapshot(200, editor_is_focused=index == 1)}

    monkeypatch.setattr(mod, "get_unity_instance_from_context", AsyncMock(return_value="Game@hash"))
    monkeypatch.setattr(mod.unity_transport, "send_with_unity_instance", send)
    context = SimpleNamespace()
    older_request = asyncio.create_task(mod.get_test_job(context, "job"))
    try:
        await asyncio.wait_for(started.wait(), 1)
        await mod.get_test_job(context, "job")
    finally:
        release.set()
        await older_request
    await drain()
    policy.assert_not_awaited()
    # A later sequential unfocused observation is valid even with the same
    # test-progress timestamp, and must not inherit the earlier focused flag.
    await mod.get_test_job(context, "job")
    await drain()
    policy.assert_awaited_once()


@pytest.mark.asyncio
async def test_staggered_concurrent_resolvers_cannot_bypass_budget_or_cooldown(policy, monkeypatch):
    monkeypatch.setattr(mod.focus_nudge, "_BASE_NUDGE_INTERVAL_S", 60)
    monkeypatch.setattr(mod.focus_nudge, "_MAX_NUDGE_INTERVAL_S", 60)
    ready = [asyncio.Event() for _ in range(5)]
    release = [asyncio.Event() for _ in range(5)]
    arrivals = 0

    async def delayed_resolve(*args):
        nonlocal arrivals
        index = arrivals
        arrivals += 1
        ready[index].set()
        await release[index].wait()
        return r"C:\Project"

    mod._get_unity_project_path.side_effect = delayed_resolve
    calls = [asyncio.create_task(poll()) for _ in range(5)]
    try:
        await asyncio.wait_for(asyncio.gather(*(event.wait() for event in ready)), 1)
        for index, call in enumerate(calls):
            release[index].set()
            await call
    finally:
        for event in release:
            event.set()
        await asyncio.gather(*calls, return_exceptions=True)
    assert policy.await_count == 1
    assert next(iter(mod._nudge_states.values())).attempts == 1


@pytest.mark.asyncio
async def test_terminal_tombstones_are_bounded_and_expire(policy, monkeypatch):
    monkeypatch.setattr(mod, "_MAX_NUDGE_STATES", 2)
    for job in ("a", "b", "c"):
        await poll({"status": "succeeded"}, job=job)
    assert len(mod._terminal_nudge_jobs) == 2
    old_key = next(iter(mod._terminal_nudge_jobs))
    mod._terminal_nudge_jobs[old_key] -= mod._NUDGE_STATE_TTL_S + 1
    await poll(snapshot(run_in_background=True), job="other")
    assert old_key not in mod._terminal_nudge_jobs


@pytest.mark.asyncio
async def test_state_capacity_does_not_evict_and_renew_capped_jobs(policy, monkeypatch):
    monkeypatch.setattr(mod, "_MAX_NUDGE_STATES", 2)
    for _ in range(3):
        await poll(job="a")
    await poll(snapshot(run_in_background=True), job="b")
    rejected = await poll(job="c")
    assert rejected["progress"]["focus_nudge_status"] == "tracking_limit"
    assert len(mod._nudge_states) == 2
    await poll(job="a")
    assert policy.await_count == 3
    await poll({"status": "succeeded"}, job="b")
    await poll(job="c")
    assert policy.await_count == 4


@pytest.mark.asyncio
async def test_inactive_states_expire_without_unbounded_growth(policy):
    await poll(snapshot(run_in_background=True), job="old")
    old_key = next(iter(mod._nudge_states))
    mod._nudge_states[old_key].last_seen -= mod._NUDGE_STATE_TTL_S + 1
    await poll(snapshot(run_in_background=True), job="new")
    assert old_key not in mod._nudge_states
    assert len(mod._nudge_states) == 1


@pytest.mark.asyncio
async def test_per_job_cooldown_is_not_reset_by_external_poll_boundaries(policy, monkeypatch):
    monkeypatch.setattr(mod.focus_nudge, "_BASE_NUDGE_INTERVAL_S", 60)
    monkeypatch.setattr(mod.focus_nudge, "_MAX_NUDGE_INTERVAL_S", 60)
    await poll()
    await poll()
    assert policy.await_count == 1
    await poll(job="other")
    assert policy.await_count == 2


@pytest.mark.asyncio
async def test_resolution_race_with_terminal_status_does_not_schedule(policy):
    entered, release = asyncio.Event(), asyncio.Event()

    async def resolve(*args):
        entered.set()
        await release.wait()
        return r"C:\Project"

    mod._get_unity_project_path.side_effect = resolve
    task = asyncio.create_task(poll())
    await asyncio.wait_for(entered.wait(), 1)
    await poll({"status": "succeeded"})
    release.set()
    await task
    policy.assert_not_awaited()


@pytest.mark.asyncio
async def test_external_and_wait_timeout_paths_share_policy_and_response_fields(policy, monkeypatch):
    context = SimpleNamespace()
    monkeypatch.setattr(mod, "get_unity_instance_from_context", AsyncMock(return_value="Game@hash"))
    send = AsyncMock(side_effect=lambda *args, **kwargs: {"success": True, "data": snapshot()})
    monkeypatch.setattr(mod.unity_transport, "send_with_unity_instance", send)
    for _ in range(3):
        await mod.get_test_job(context, "job")
        await drain()
    capped = await mod.get_test_job(context, "job")
    assert capped.data.progress.stuck_suspected is True
    assert capped.data.progress.focus_nudge_attempts == 3
    send.side_effect = [
        {"success": True, "data": snapshot()},
        {"success": True, "data": {"job_id": "job", "status": "succeeded"}},
    ]
    monkeypatch.setattr(mod.asyncio, "sleep", AsyncMock())
    completed = await mod.get_test_job(context, "job", wait_timeout=30)
    assert completed.data.status == "succeeded"
    assert policy.await_count == 3
    assert not mod._nudge_states


@pytest.mark.parametrize("path,expected", [
    (r"C:\Worktrees\Game\Assets", r"C:\Worktrees\Game"),
    ("/worktrees/Game/Assets/", "/worktrees/Game"),
    (r"C:\Game\MyAssets", r"C:\Game\MyAssets"),
    ("Game/Assets", None),
    (r"\Projects\Game\Assets", None),
])
@pytest.mark.asyncio
async def test_stdio_path_uses_exact_registry_entry_and_strips_only_assets(monkeypatch, path, expected):
    from transport.legacy.stdio_port_registry import stdio_port_registry
    monkeypatch.setattr(mod.config, "transport_mode", "stdio")
    monkeypatch.setattr(stdio_port_registry, "get_instances", lambda: [
        SimpleNamespace(id="Game@wrong", hash="wrong", path=r"C:\Wrong\Assets"),
        SimpleNamespace(id="Game@right", hash="right", path=path),
    ])
    assert await mod._get_unity_project_path("Game@right") == expected
    assert await mod._get_unity_project_path("right") == expected
    assert await mod._get_unity_project_path("Game") is None
    assert await mod._get_unity_project_path("rig") is None
    assert await mod._get_unity_project_path(None) is None


@pytest.mark.asyncio
async def test_http_path_uses_user_scope_and_never_name_fallback(monkeypatch):
    monkeypatch.setattr(mod.config, "transport_mode", "http")
    monkeypatch.setattr(mod.config, "http_remote_hosted", True)
    registry = SimpleNamespace(
        get_session_id_by_hash=AsyncMock(return_value="session"),
        get_session=AsyncMock(return_value=SimpleNamespace(project_path=None, project_name="Game")),
    )
    monkeypatch.setattr(mod.PluginHub, "_registry", registry)
    assert await mod._get_unity_project_path("Game@hash", "user") is None
    registry.get_session_id_by_hash.assert_awaited_once_with("hash", user_id="user")
    registry.get_session.return_value.project_path = "/work/Game"
    assert await mod._get_unity_project_path("Game@hash", "user") == "/work/Game"
    assert await mod._get_unity_project_path("Game@hash") is None
