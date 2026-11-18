from __future__ import annotations

import asyncio
import sys
import types
import weakref

import anyio
import pytest

from transport import plugin_hub
from transport.plugin_hub import PluginHub


class _FakeSession:
    def __init__(
        self,
        failure: Exception | None = None,
        delay: float = 0,
    ) -> None:
        self.failure = failure
        self.delay = delay
        self.notifications = 0

    async def send_tool_list_changed(self) -> None:
        if self.delay:
            await asyncio.sleep(self.delay)
        if self.failure:
            raise self.failure
        self.notifications += 1


@pytest.fixture(autouse=True)
def _reset_session_tracking() -> None:
    plugin_hub._active_mcp_sessions.clear()
    PluginHub._published_tool_fingerprint = None
    PluginHub._pending_tool_list_notifications.clear()
    yield
    plugin_hub._active_mcp_sessions.clear()
    PluginHub._published_tool_fingerprint = None
    PluginHub._pending_tool_list_notifications.clear()


def test_session_tracking_removes_exited_session() -> None:
    session = _FakeSession()

    plugin_hub._track_mcp_session(session)
    assert session in plugin_hub._active_mcp_sessions

    plugin_hub._untrack_mcp_session(session)
    assert session not in plugin_hub._active_mcp_sessions


def test_twenty_connect_disconnect_cycles_return_to_baseline() -> None:
    sessions = [_FakeSession() for _ in range(20)]

    for session in sessions:
        plugin_hub._track_mcp_session(session)
        plugin_hub._untrack_mcp_session(session)

    assert list(plugin_hub._active_mcp_sessions) == []


@pytest.mark.asyncio
async def test_notification_prunes_closed_sessions() -> None:
    active = _FakeSession()
    closed = _FakeSession(failure=anyio.ClosedResourceError())
    plugin_hub._track_mcp_session(active)
    plugin_hub._track_mcp_session(closed)

    await PluginHub._notify_mcp_tool_list_changed()

    assert active.notifications == 1
    assert active in plugin_hub._active_mcp_sessions
    assert closed not in plugin_hub._active_mcp_sessions


@pytest.mark.asyncio
async def test_notification_keeps_session_after_unconfirmed_failure() -> None:
    transient = _FakeSession(failure=RuntimeError("temporary"))
    plugin_hub._track_mcp_session(transient)

    await PluginHub._notify_mcp_tool_list_changed()

    assert transient in plugin_hub._active_mcp_sessions
    assert transient in PluginHub._pending_tool_list_notifications


@pytest.mark.asyncio
async def test_notification_timeout_is_bounded_without_pruning_live_session(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    slow = _FakeSession(delay=0.1)
    plugin_hub._track_mcp_session(slow)
    monkeypatch.setattr(PluginHub, "_TOOL_LIST_NOTIFY_TIMEOUT_SECONDS", 0.01)

    await PluginHub._notify_mcp_tool_list_changed()

    assert slow.notifications == 0
    assert slow in plugin_hub._active_mcp_sessions
    assert slow in PluginHub._pending_tool_list_notifications


@pytest.mark.asyncio
async def test_patched_session_context_is_tracked_symmetrically(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    class _MiddlewareServerSession:
        async def __aenter__(self):
            return self

        async def __aexit__(self, exc_type, exc_value, traceback):
            return None

    low_level = types.ModuleType("fastmcp.server.low_level")
    low_level.MiddlewareServerSession = _MiddlewareServerSession
    monkeypatch.setitem(sys.modules, "fastmcp.server.low_level", low_level)
    monkeypatch.setattr(plugin_hub, "_session_tracking_installed", False)

    plugin_hub._install_session_tracking()
    session = _MiddlewareServerSession()
    await session.__aenter__()
    preserved_sessions = plugin_hub._active_mcp_sessions

    assert session in preserved_sessions

    monkeypatch.setattr(plugin_hub, "_active_mcp_sessions", weakref.WeakSet())
    monkeypatch.setattr(plugin_hub, "_session_tracking_installed", False)
    plugin_hub._install_session_tracking()

    assert plugin_hub._active_mcp_sessions is preserved_sessions
    await session.__aexit__(None, None, None)
    assert session not in plugin_hub._active_mcp_sessions


@pytest.mark.asyncio
async def test_unchanged_tools_do_not_rebroadcast() -> None:
    sessions = [_FakeSession(), _FakeSession()]
    for session in sessions:
        plugin_hub._track_mcp_session(session)

    tools = [{"name": "read_console", "description": "Console"}]
    assert await PluginHub._record_and_notify_tool_list_change(tools) is True
    assert [session.notifications for session in sessions] == [1, 1]

    assert await PluginHub._record_and_notify_tool_list_change(tools) is False

    assert [session.notifications for session in sessions] == [1, 1]


def test_tool_fingerprint_deduplicates_reordered_payload() -> None:
    first = [
        {"name": "manage_scene", "description": "Scene"},
        {"name": "read_console", "description": "Console"},
    ]
    reordered = list(reversed(first))
    changed_schema = [
        {"name": "manage_scene", "description": "Scene changed"},
        {"name": "read_console", "description": "Console"},
    ]

    assert PluginHub._tool_fingerprint(first) == PluginHub._tool_fingerprint(reordered)
    assert PluginHub._tool_fingerprint(first) != PluginHub._tool_fingerprint(changed_schema)


@pytest.mark.asyncio
async def test_fingerprint_is_not_committed_when_server_inspection_fails(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    class _FailingMcp:
        async def list_tools(self) -> list:
            raise RuntimeError("inspection failed")

    monkeypatch.setattr(PluginHub, "_mcp", _FailingMcp())

    assert await PluginHub._record_published_tool_list([{"name": "one"}]) is False
    assert PluginHub._published_tool_fingerprint is None


@pytest.mark.asyncio
async def test_actual_server_tool_registration_changes_publication_fingerprint(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    class _FakeMcp:
        def __init__(self) -> None:
            self.tools: list[dict] = []

        async def list_tools(self) -> list[dict]:
            return self.tools

    mcp = _FakeMcp()
    monkeypatch.setattr(PluginHub, "_mcp", mcp)
    desired = [{"name": "custom"}]

    assert await PluginHub._record_published_tool_list(desired) is True
    mcp.tools.append({"name": "custom"})
    assert await PluginHub._record_published_tool_list(desired) is True


@pytest.mark.asyncio
async def test_failed_notification_retries_only_pending_session() -> None:
    delivered = _FakeSession()
    transient = _FakeSession(failure=RuntimeError("temporary"))
    plugin_hub._track_mcp_session(delivered)
    plugin_hub._track_mcp_session(transient)
    tools = [{"name": "read_console", "description": "Console"}]

    assert await PluginHub._record_and_notify_tool_list_change(tools) is True

    assert delivered.notifications == 1
    assert transient.notifications == 0
    assert delivered not in PluginHub._pending_tool_list_notifications
    assert transient in PluginHub._pending_tool_list_notifications

    transient.failure = None
    assert await PluginHub._record_and_notify_tool_list_change(tools) is False

    assert delivered.notifications == 1
    assert transient.notifications == 1
    assert list(PluginHub._pending_tool_list_notifications) == []
