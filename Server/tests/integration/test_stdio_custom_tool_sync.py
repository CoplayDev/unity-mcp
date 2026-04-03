"""
Tests for stdio-mode custom tool discovery (GitHub issue #837).

Verifies that:
1. sync_tool_visibility_from_unity registers custom tools when extended metadata is present
2. Custom tools are skipped gracefully when metadata is missing (old Unity package)
3. Reconnection flag triggers a background re-sync
"""
import asyncio
from unittest.mock import AsyncMock, MagicMock, patch

import pytest


# ---------------------------------------------------------------------------
# Helpers
# ---------------------------------------------------------------------------

def _make_unity_response(tools, include_extended=True):
    """Build a fake get_tool_states response from Unity."""
    tool_list = []
    for t in tools:
        entry = {
            "name": t["name"],
            "group": t.get("group", "core"),
            "enabled": t.get("enabled", True),
        }
        if include_extended:
            entry.update({
                "description": t.get("description", f"Tool: {t['name']}"),
                "auto_register": t.get("auto_register", True),
                "is_built_in": t.get("is_built_in", True),
                "structured_output": t.get("structured_output", False),
                "requires_polling": t.get("requires_polling", False),
                "poll_action": t.get("poll_action", "status"),
                "max_poll_seconds": t.get("max_poll_seconds", 0),
                "parameters": t.get("parameters", []),
            })
        tool_list.append(entry)
    return {
        "data": {
            "tools": tool_list,
            "groups": [],
        }
    }


BUILTIN_TOOL = {
    "name": "manage_gameobject",
    "group": "core",
    "is_built_in": True,
    "description": "Manage GameObjects in the scene.",
}

CUSTOM_TOOL = {
    "name": "test_ping",
    "group": "core",
    "is_built_in": False,
    "description": "Simple test tool that returns a pong.",
    "parameters": [
        {"name": "message", "description": "Message to echo", "type": "string", "required": False, "default_value": "pong"},
    ],
}


# ---------------------------------------------------------------------------
# sync_tool_visibility_from_unity — custom tool registration
# ---------------------------------------------------------------------------

@pytest.mark.asyncio
async def test_sync_registers_custom_tools():
    """Custom (non-built-in) tools should be registered via CustomToolService."""
    response = _make_unity_response([BUILTIN_TOOL, CUSTOM_TOOL])

    mock_service = MagicMock()
    mock_service.register_global_tools = MagicMock()

    with patch(
        "transport.legacy.unity_connection.async_send_command_with_retry",
        new_callable=AsyncMock,
        return_value=response,
    ), patch(
        "transport.plugin_hub.PluginHub._sync_server_tool_visibility",
    ), patch(
        "transport.plugin_hub.PluginHub._notify_mcp_tool_list_changed",
        new_callable=AsyncMock,
    ), patch(
        "services.custom_tool_service.CustomToolService.get_instance",
        return_value=mock_service,
    ):
        from services.tools import sync_tool_visibility_from_unity
        result = await sync_tool_visibility_from_unity(notify=False)

    assert result["synced"] is True
    assert result["custom_tool_count"] == 1

    # Verify register_global_tools was called with the custom tool
    mock_service.register_global_tools.assert_called_once()
    registered = mock_service.register_global_tools.call_args[0][0]
    assert len(registered) == 1
    assert registered[0].name == "test_ping"
    assert registered[0].description == "Simple test tool that returns a pong."
    assert len(registered[0].parameters) == 1
    assert registered[0].parameters[0].name == "message"


@pytest.mark.asyncio
async def test_sync_skips_builtin_tools():
    """Built-in tools should NOT be passed to register_global_tools."""
    response = _make_unity_response([BUILTIN_TOOL])

    with patch(
        "transport.legacy.unity_connection.async_send_command_with_retry",
        new_callable=AsyncMock,
        return_value=response,
    ), patch(
        "transport.plugin_hub.PluginHub._sync_server_tool_visibility",
    ), patch(
        "transport.plugin_hub.PluginHub._notify_mcp_tool_list_changed",
        new_callable=AsyncMock,
    ), patch(
        "services.custom_tool_service.CustomToolService.get_instance",
    ) as mock_get_instance:
        from services.tools import sync_tool_visibility_from_unity
        result = await sync_tool_visibility_from_unity(notify=False)

    assert result["synced"] is True
    assert result["custom_tool_count"] == 0
    # No custom tools → register_global_tools should NOT be called
    mock_get_instance.assert_not_called()


@pytest.mark.asyncio
async def test_sync_skips_when_no_extended_metadata():
    """When Unity returns old-format data (no is_built_in), skip custom tool registration."""
    response = _make_unity_response([BUILTIN_TOOL, CUSTOM_TOOL], include_extended=False)

    with patch(
        "transport.legacy.unity_connection.async_send_command_with_retry",
        new_callable=AsyncMock,
        return_value=response,
    ), patch(
        "transport.plugin_hub.PluginHub._sync_server_tool_visibility",
    ), patch(
        "transport.plugin_hub.PluginHub._notify_mcp_tool_list_changed",
        new_callable=AsyncMock,
    ), patch(
        "services.custom_tool_service.CustomToolService.get_instance",
    ) as mock_get_instance:
        from services.tools import sync_tool_visibility_from_unity
        result = await sync_tool_visibility_from_unity(notify=False)

    assert result["synced"] is True
    assert result["custom_tool_count"] == 0
    mock_get_instance.assert_not_called()


@pytest.mark.asyncio
async def test_sync_handles_custom_tool_service_not_initialized():
    """If CustomToolService isn't initialized yet, skip gracefully (no crash)."""
    response = _make_unity_response([CUSTOM_TOOL])

    with patch(
        "transport.legacy.unity_connection.async_send_command_with_retry",
        new_callable=AsyncMock,
        return_value=response,
    ), patch(
        "transport.plugin_hub.PluginHub._sync_server_tool_visibility",
    ), patch(
        "transport.plugin_hub.PluginHub._notify_mcp_tool_list_changed",
        new_callable=AsyncMock,
    ), patch(
        "services.custom_tool_service.CustomToolService.get_instance",
        side_effect=RuntimeError("not initialized"),
    ):
        from services.tools import sync_tool_visibility_from_unity
        result = await sync_tool_visibility_from_unity(notify=False)

    # Should succeed overall even though custom tool registration failed
    assert result["synced"] is True
    assert result["custom_tool_count"] == 0


# ---------------------------------------------------------------------------
# Reconnection re-sync trigger
# ---------------------------------------------------------------------------

@pytest.mark.asyncio
async def test_reconnection_flag_triggers_resync():
    """After reconnection, async_send_command_with_retry should schedule a re-sync."""
    mock_conn = MagicMock()
    mock_conn._needs_tool_resync = True
    mock_conn.instance_id = None

    mock_pool = MagicMock()
    mock_pool.get_connection.return_value = mock_conn

    with patch(
        "transport.legacy.unity_connection.send_command_with_retry",
        return_value={"success": True, "message": "ok"},
    ), patch(
        "transport.legacy.unity_connection.get_unity_connection_pool",
        return_value=mock_pool,
    ), patch(
        "transport.legacy.unity_connection._resync_tools_after_reconnect",
        new_callable=AsyncMock,
    ) as mock_resync:
        from transport.legacy.unity_connection import async_send_command_with_retry
        result = await async_send_command_with_retry("manage_gameobject", {"action": "list"})

        # ensure_future schedules on the event loop; give it a tick to run
        await asyncio.sleep(0)

    assert result["success"] is True
    # Flag should be cleared
    assert mock_conn._needs_tool_resync is False
    # Re-sync should have been scheduled
    mock_resync.assert_awaited_once_with(None)


@pytest.mark.asyncio
async def test_no_resync_for_get_tool_states():
    """get_tool_states itself should NOT trigger re-sync (avoids recursion)."""
    mock_conn = MagicMock()
    mock_conn._needs_tool_resync = True

    mock_pool = MagicMock()
    mock_pool.get_connection.return_value = mock_conn

    with patch(
        "transport.legacy.unity_connection.send_command_with_retry",
        return_value={"data": {"tools": []}},
    ), patch(
        "transport.legacy.unity_connection.get_unity_connection_pool",
        return_value=mock_pool,
    ), patch(
        "transport.legacy.unity_connection._resync_tools_after_reconnect",
        new_callable=AsyncMock,
    ) as mock_resync:
        from transport.legacy.unity_connection import async_send_command_with_retry
        await async_send_command_with_retry("get_tool_states", {})

    # Flag should NOT be cleared — get_tool_states is excluded
    assert mock_conn._needs_tool_resync is True
    mock_resync.assert_not_awaited()


@pytest.mark.asyncio
async def test_no_resync_when_not_reconnected():
    """When _needs_tool_resync is False, no re-sync should be scheduled."""
    mock_conn = MagicMock()
    mock_conn._needs_tool_resync = False

    mock_pool = MagicMock()
    mock_pool.get_connection.return_value = mock_conn

    with patch(
        "transport.legacy.unity_connection.send_command_with_retry",
        return_value={"success": True},
    ), patch(
        "transport.legacy.unity_connection.get_unity_connection_pool",
        return_value=mock_pool,
    ), patch(
        "transport.legacy.unity_connection._resync_tools_after_reconnect",
        new_callable=AsyncMock,
    ) as mock_resync:
        from transport.legacy.unity_connection import async_send_command_with_retry
        await async_send_command_with_retry("manage_gameobject", {"action": "list"})

    mock_resync.assert_not_awaited()
