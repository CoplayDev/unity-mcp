import time

import pytest

from .test_helpers import DummyContext


@pytest.fixture(autouse=True)
def isolated_operation_lease_dir(tmp_path, monkeypatch):
    monkeypatch.setenv("UNITY_MCP_OPERATION_LEASE_DIR", str(tmp_path))
    monkeypatch.delenv("UNITY_MCP_OPERATION_LEASE_TTL_S", raising=False)


def test_operation_lease_blocks_second_owner():
    from services.tools.editor_operation_lease import (
        operation_busy_response,
        try_acquire_editor_operation_lease,
    )

    lease, busy = try_acquire_editor_operation_lease(
        "UnityMCPTests@abc123",
        "refresh_unity",
        owner="agent-a",
        ttl_s=30,
    )
    assert lease is not None
    assert busy is None

    try:
        second_lease, second_busy = try_acquire_editor_operation_lease(
            "UnityMCPTests@abc123",
            "run_tests",
            owner="agent-b",
            ttl_s=30,
        )
        assert second_lease is None
        assert second_busy is not None
        assert second_busy.owner == "agent-a"
        assert second_busy.operation == "refresh_unity"

        response = operation_busy_response(second_busy)
        assert response.success is False
        assert response.error == "operation_busy"
        assert response.hint == "retry"
        assert response.data["reason"] == "operation_busy"
        assert response.data["operation"] == "refresh_unity"
    finally:
        lease.release()


def test_operation_lease_reclaims_expired_file():
    from services.tools.editor_operation_lease import try_acquire_editor_operation_lease

    lease, busy = try_acquire_editor_operation_lease(
        "UnityMCPTests@abc123",
        "refresh_unity",
        owner="agent-a",
        ttl_s=0.001,
    )
    assert lease is not None
    assert busy is None

    time.sleep(0.01)

    replacement, replacement_busy = try_acquire_editor_operation_lease(
        "UnityMCPTests@abc123",
        "run_tests",
        owner="agent-b",
        ttl_s=30,
    )
    assert replacement is not None
    assert replacement_busy is None
    assert replacement.owner == "agent-b"
    replacement.release()


def test_operation_owner_is_per_tool_context():
    from services.tools.editor_operation_lease import operation_owner_from_context

    first = DummyContext()
    second = DummyContext()

    assert operation_owner_from_context(first) == operation_owner_from_context(first)
    assert operation_owner_from_context(first) != operation_owner_from_context(second)


def test_operation_lease_allows_reentrant_acquire_for_same_owner():
    from services.tools.editor_operation_lease import try_acquire_editor_operation_lease

    lease, busy = try_acquire_editor_operation_lease(
        "UnityMCPTests@abc123",
        "run_tests",
        owner="agent-a",
        ttl_s=30,
    )
    assert lease is not None
    assert busy is None

    try:
        nested, nested_busy = try_acquire_editor_operation_lease(
            "UnityMCPTests@abc123",
            "refresh_unity",
            owner="agent-a",
            ttl_s=30,
        )
        assert nested is not None
        assert nested_busy is None
        assert nested.owner == "agent-a"

        nested.release()

        other, other_busy = try_acquire_editor_operation_lease(
            "UnityMCPTests@abc123",
            "refresh_unity",
            owner="agent-b",
            ttl_s=30,
        )
        assert other is None
        assert other_busy is not None
        assert other_busy.owner == "agent-a"
    finally:
        lease.release()


@pytest.mark.asyncio
async def test_run_tests_returns_retry_when_another_agent_holds_refresh_lease(monkeypatch):
    from services.tools.editor_operation_lease import try_acquire_editor_operation_lease
    from services.tools.run_tests import run_tests
    import services.tools.run_tests as mod

    ctx = DummyContext()
    await ctx.set_state("unity_instance", "UnityMCPTests@abc123")

    lease, busy = try_acquire_editor_operation_lease(
        "UnityMCPTests@abc123",
        "refresh_unity",
        owner="agent-a",
        ttl_s=30,
    )
    assert lease is not None
    assert busy is None

    async def fail_if_dispatched(*args, **kwargs):
        raise AssertionError("run_tests should not dispatch while operation lease is held")

    monkeypatch.setattr(mod.unity_transport, "send_with_unity_instance", fail_if_dispatched)

    try:
        response = await run_tests(ctx, mode="EditMode")
        assert response.success is False
        assert response.error == "operation_busy"
        assert response.hint == "retry"
        assert response.data["operation"] == "refresh_unity"
    finally:
        lease.release()


@pytest.mark.asyncio
async def test_refresh_unity_returns_retry_when_another_agent_holds_run_tests_lease(monkeypatch):
    from services.tools.editor_operation_lease import try_acquire_editor_operation_lease
    from services.tools.refresh_unity import refresh_unity
    import services.tools.refresh_unity as mod

    ctx = DummyContext()
    await ctx.set_state("unity_instance", "UnityMCPTests@abc123")

    lease, busy = try_acquire_editor_operation_lease(
        "UnityMCPTests@abc123",
        "run_tests",
        owner="agent-a",
        ttl_s=30,
    )
    assert lease is not None
    assert busy is None

    async def fail_if_dispatched(*args, **kwargs):
        raise AssertionError("refresh_unity should not dispatch while operation lease is held")

    monkeypatch.setattr(mod.unity_transport, "send_with_unity_instance", fail_if_dispatched)

    try:
        response = await refresh_unity(ctx, wait_for_ready=False)
        assert response.success is False
        assert response.error == "operation_busy"
        assert response.hint == "retry"
        assert response.data["operation"] == "run_tests"
    finally:
        lease.release()


@pytest.mark.asyncio
async def test_run_tests_releases_start_lease_after_dispatch(monkeypatch):
    from services.tools.editor_operation_lease import try_acquire_editor_operation_lease
    from services.tools.run_tests import run_tests
    import services.tools.run_tests as mod

    ctx = DummyContext()
    await ctx.set_state("unity_instance", "UnityMCPTests@abc123")

    async def fake_send_with_unity_instance(send_fn, unity_instance, command_type, params, **kwargs):
        return {"success": True, "data": {"job_id": "job-1", "status": "running", "mode": "EditMode"}}

    monkeypatch.setattr(mod.unity_transport, "send_with_unity_instance", fake_send_with_unity_instance)

    response = await run_tests(ctx, mode="EditMode")
    assert response.success is True

    lease, busy = try_acquire_editor_operation_lease(
        "UnityMCPTests@abc123",
        "refresh_unity",
        owner="agent-b",
        ttl_s=30,
    )
    assert lease is not None
    assert busy is None
    lease.release()
