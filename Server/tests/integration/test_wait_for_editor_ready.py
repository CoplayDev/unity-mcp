import asyncio
import os
import pytest

from services.tools.refresh_unity import is_reloading_rejection
from .test_helpers import DummyContext


@pytest.mark.asyncio
async def test_returns_immediately_in_pytest(monkeypatch):
    """_in_pytest() detects PYTEST_CURRENT_TEST and returns (True, 0.0) immediately."""
    # PYTEST_CURRENT_TEST is set by pytest automatically, so this should short-circuit.
    from services.tools.refresh_unity import wait_for_editor_ready

    ctx = DummyContext()
    ready, elapsed = await wait_for_editor_ready(ctx, timeout_s=5.0)
    assert ready is True
    assert elapsed == 0.0


@pytest.mark.asyncio
async def test_polls_until_ready(monkeypatch):
    """When not in pytest, the helper polls get_editor_state until ready_for_tools."""
    monkeypatch.delenv("PYTEST_CURRENT_TEST", raising=False)

    from services.tools import refresh_unity as mod

    call_count = 0

    async def fake_get_editor_state(ctx):
        nonlocal call_count
        call_count += 1
        if call_count < 3:
            return {"data": {"advice": {"ready_for_tools": False, "blocking_reasons": ["compiling"]}}}
        return {"data": {"advice": {"ready_for_tools": True, "blocking_reasons": []}}}

    monkeypatch.setattr(mod.editor_state, "get_editor_state", fake_get_editor_state)

    ctx = DummyContext()
    ready, elapsed = await mod.wait_for_editor_ready(ctx, timeout_s=10.0)
    assert ready is True
    assert call_count >= 3
    assert elapsed > 0


@pytest.mark.asyncio
async def test_timeout_returns_false(monkeypatch):
    """When editor never becomes ready, returns (False, ~timeout)."""
    monkeypatch.delenv("PYTEST_CURRENT_TEST", raising=False)

    from services.tools import refresh_unity as mod

    async def fake_get_editor_state(ctx):
        return {"data": {"advice": {"ready_for_tools": False, "blocking_reasons": ["compiling"]}}}

    monkeypatch.setattr(mod.editor_state, "get_editor_state", fake_get_editor_state)

    ctx = DummyContext()
    ready, elapsed = await mod.wait_for_editor_ready(ctx, timeout_s=0.6)
    assert ready is False
    assert elapsed >= 0.5


@pytest.mark.asyncio
async def test_stale_only_treated_as_ready(monkeypatch):
    """If the only blocking reason is stale_status, consider ready."""
    monkeypatch.delenv("PYTEST_CURRENT_TEST", raising=False)

    from services.tools import refresh_unity as mod

    async def fake_get_editor_state(ctx):
        return {"data": {"advice": {"ready_for_tools": False, "blocking_reasons": ["stale_status"]}}}

    monkeypatch.setattr(mod.editor_state, "get_editor_state", fake_get_editor_state)

    ctx = DummyContext()
    ready, elapsed = await mod.wait_for_editor_ready(ctx, timeout_s=5.0)
    assert ready is True


@pytest.mark.asyncio
async def test_exception_during_poll_keeps_trying(monkeypatch):
    """If get_editor_state throws, the helper keeps polling until ready."""
    monkeypatch.delenv("PYTEST_CURRENT_TEST", raising=False)

    from services.tools import refresh_unity as mod

    call_count = 0

    async def fake_get_editor_state(ctx):
        nonlocal call_count
        call_count += 1
        if call_count < 3:
            raise ConnectionError("Unity disconnected")
        return {"data": {"advice": {"ready_for_tools": True, "blocking_reasons": []}}}

    monkeypatch.setattr(mod.editor_state, "get_editor_state", fake_get_editor_state)

    ctx = DummyContext()
    ready, elapsed = await mod.wait_for_editor_ready(ctx, timeout_s=10.0)
    assert ready is True
    assert call_count >= 3


def test_is_reloading_rejection_true():
    """Detects a reloading rejection response."""
    resp = {"success": False, "error": "Unity is reloading", "data": {"reason": "reloading"}, "hint": "retry"}
    assert is_reloading_rejection(resp) is True


def test_is_reloading_rejection_false_on_success():
    assert is_reloading_rejection({"success": True, "data": {"reason": "reloading"}, "hint": "retry"}) is False


def test_is_reloading_rejection_false_on_other_error():
    assert is_reloading_rejection({"success": False, "error": "timeout", "data": {}, "hint": "retry"}) is False


def test_is_reloading_rejection_false_on_non_dict():
    assert is_reloading_rejection("some string") is False
    assert is_reloading_rejection(None) is False


# --- is_connection_lost_after_send tests ---

from services.tools.refresh_unity import is_connection_lost_after_send


def test_connection_lost_on_connection_closed():
    resp = {"success": False, "error": "Connection closed before reading expected bytes"}
    assert is_connection_lost_after_send(resp) is True


def test_connection_lost_on_disconnected():
    resp = {"success": False, "error": "Unity disconnected"}
    assert is_connection_lost_after_send(resp) is True


def test_connection_lost_on_aborted():
    resp = {"success": False, "error": "Connection aborted"}
    assert is_connection_lost_after_send(resp) is True


def test_connection_lost_false_on_success():
    resp = {"success": True, "error": "Connection closed before reading expected bytes"}
    assert is_connection_lost_after_send(resp) is False


def test_connection_lost_false_on_other_error():
    resp = {"success": False, "error": "timeout"}
    assert is_connection_lost_after_send(resp) is False


def test_connection_lost_false_on_non_dict():
    assert is_connection_lost_after_send("some string") is False
    assert is_connection_lost_after_send(None) is False


# --- send_mutation tests ---

from services.tools.refresh_unity import send_mutation


@pytest.mark.asyncio
async def test_send_mutation_returns_success_directly(monkeypatch):
    """Normal success response is returned as-is."""
    from services.tools import refresh_unity as mod

    async def fake_send(*args, **kwargs):
        return {"success": True, "data": {"ok": True}}

    monkeypatch.setattr(mod.unity_transport, "send_with_unity_instance", fake_send)

    ctx = DummyContext()
    resp = await send_mutation(ctx, None, "manage_script", {"action": "create"})
    assert resp == {"success": True, "data": {"ok": True}}


@pytest.mark.asyncio
async def test_send_mutation_retries_on_reloading_rejection(monkeypatch):
    """Reloading rejection triggers one retry after wait."""
    from services.tools import refresh_unity as mod

    call_count = 0

    async def fake_send(*args, **kwargs):
        nonlocal call_count
        call_count += 1
        if call_count == 1:
            return {"success": False, "data": {"reason": "reloading"}, "hint": "retry"}
        return {"success": True, "data": {"retried": True}}

    monkeypatch.setattr(mod.unity_transport, "send_with_unity_instance", fake_send)

    ctx = DummyContext()
    resp = await send_mutation(ctx, None, "manage_script", {"action": "create"})
    assert resp.get("success") is True
    assert call_count == 2


@pytest.mark.asyncio
async def test_send_mutation_calls_verify_on_connection_lost(monkeypatch):
    """Connection lost triggers verify callback."""
    from services.tools import refresh_unity as mod

    async def fake_send(*args, **kwargs):
        return {"success": False, "error": "Connection closed before reading expected bytes"}

    monkeypatch.setattr(mod.unity_transport, "send_with_unity_instance", fake_send)

    verify_called = False

    async def fake_verify():
        nonlocal verify_called
        verify_called = True
        return {"success": True, "message": "Verified!"}

    ctx = DummyContext()
    resp = await send_mutation(ctx, None, "manage_script", {}, verify_after_disconnect=fake_verify)
    assert verify_called
    assert resp == {"success": True, "message": "Verified!"}


@pytest.mark.asyncio
async def test_send_mutation_keeps_error_when_verify_returns_none(monkeypatch):
    """When verify callback returns None, original error is preserved."""
    from services.tools import refresh_unity as mod

    async def fake_send(*args, **kwargs):
        return {"success": False, "error": "Connection closed before reading expected bytes"}

    monkeypatch.setattr(mod.unity_transport, "send_with_unity_instance", fake_send)

    async def fake_verify():
        return None

    ctx = DummyContext()
    resp = await send_mutation(ctx, None, "manage_script", {}, verify_after_disconnect=fake_verify)
    assert resp.get("success") is False
