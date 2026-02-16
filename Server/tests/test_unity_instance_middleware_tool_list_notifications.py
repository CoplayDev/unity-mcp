import asyncio
from types import SimpleNamespace
from unittest.mock import AsyncMock

import pytest

from core.config import config
from transport.unity_instance_middleware import UnityInstanceMiddleware


def _build_context(session_id: str, session_obj: object, method: str = "tools/list"):
    fastmcp_context = SimpleNamespace(
        request_context=object(),
        session_id=session_id,
        session=session_obj,
    )
    return SimpleNamespace(
        fastmcp_context=fastmcp_context,
        method=method,
    )


@pytest.mark.asyncio
async def test_on_message_registers_new_session_and_notifies(monkeypatch):
    monkeypatch.setattr(config, "transport_mode", "stdio")
    middleware = UnityInstanceMiddleware()
    session = SimpleNamespace(send_tool_list_changed=AsyncMock())

    context = _build_context("session-1", session)
    await middleware.on_message(context, AsyncMock(return_value=None))
    await middleware.on_message(context, AsyncMock(return_value=None))

    # First message for a new session sends one immediate refresh notification.
    assert session.send_tool_list_changed.await_count == 1


@pytest.mark.asyncio
async def test_on_notification_initialized_triggers_tools_list_changed(monkeypatch):
    monkeypatch.setattr(config, "transport_mode", "stdio")
    middleware = UnityInstanceMiddleware()
    session = SimpleNamespace(send_tool_list_changed=AsyncMock())

    context = _build_context("session-init", session, method="notifications/initialized")
    await middleware.on_notification(context, AsyncMock(return_value=None))

    assert session.send_tool_list_changed.await_count == 1


@pytest.mark.asyncio
async def test_notify_tool_list_changed_removes_stale_sessions(monkeypatch):
    monkeypatch.setattr(config, "transport_mode", "stdio")
    middleware = UnityInstanceMiddleware()

    healthy_session = SimpleNamespace(send_tool_list_changed=AsyncMock(return_value=None))

    async def _raise_send():
        raise RuntimeError("session closed")

    stale_session = SimpleNamespace(send_tool_list_changed=AsyncMock(side_effect=_raise_send))
    middleware._tracked_sessions["healthy"] = healthy_session
    middleware._tracked_sessions["stale"] = stale_session

    await middleware._notify_tool_list_changed_to_sessions("test_reason")

    assert "healthy" in middleware._tracked_sessions
    assert "stale" not in middleware._tracked_sessions
    assert healthy_session.send_tool_list_changed.await_count == 1


@pytest.mark.asyncio
async def test_start_stdio_tools_watcher_skips_when_transport_is_not_stdio(monkeypatch):
    monkeypatch.setattr(config, "transport_mode", "http")
    middleware = UnityInstanceMiddleware()

    await middleware.start_stdio_tools_watcher()
    assert middleware._stdio_tools_watch_task is None


@pytest.mark.asyncio
async def test_stdio_tools_watcher_notifies_on_signature_change(monkeypatch):
    monkeypatch.setattr(config, "transport_mode", "stdio")
    monkeypatch.setenv("UNITY_MCP_STDIO_TOOLS_WATCH_INTERVAL_SECONDS", "0.2")
    middleware = UnityInstanceMiddleware()
    middleware._notify_tool_list_changed_to_sessions = AsyncMock(return_value=None)

    signatures = [
        (("hash1", ("manage_scene",)),),
        (("hash1", ("manage_scene", "manage_asset")),),
        (("hash1", ("manage_scene", "manage_asset")),),
    ]
    signature_index = {"value": 0}

    def _fake_signature():
        index = signature_index["value"]
        signature_index["value"] += 1
        if index >= len(signatures):
            return signatures[-1]
        return signatures[index]

    monkeypatch.setattr(middleware, "_build_stdio_tools_state_signature", _fake_signature)

    await middleware.start_stdio_tools_watcher()
    try:
        await asyncio.sleep(0.35)
    finally:
        await middleware.stop_stdio_tools_watcher()

    middleware._notify_tool_list_changed_to_sessions.assert_awaited_with("stdio_state_changed")
