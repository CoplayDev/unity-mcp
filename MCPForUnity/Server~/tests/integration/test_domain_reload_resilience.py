"""
Integration test for domain reload resilience.

Tests that the MCP server can handle rapid-fire requests during Unity domain reloads
by waiting for the plugin to reconnect instead of failing immediately.
"""
import asyncio
import pytest
from unittest.mock import AsyncMock, patch
from datetime import datetime

from .test_helpers import DummyContext


@pytest.mark.asyncio
async def test_plugin_hub_waits_for_reconnection_during_reload():
    """Test that PluginHub._resolve_session_id waits for plugin reconnection."""
    # Import after conftest stubs are set up
    from transport.plugin_hub import PluginHub
    from transport.plugin_registry import PluginRegistry, PluginSession

    # Create a mock registry
    mock_registry = AsyncMock(spec=PluginRegistry)

    # Simulate plugin reconnection sequence:
    # First 2 calls: no sessions (plugin disconnected)
    # Third call: session appears (plugin reconnected)
    call_count = [0]

    async def mock_list_sessions():
        call_count[0] += 1
        if call_count[0] <= 2:
            # Plugin not yet reconnected
            return {}
        else:
            # Plugin reconnected
            now = datetime.now()
            session = PluginSession(
                session_id="test-session-123",
                project_name="TestProject",
                project_hash="abc123",
                unity_version="2022.3.0f1",
                registered_at=now,
                connected_at=now
            )
            return {"test-session-123": session}

    mock_registry.list_sessions = mock_list_sessions

    # Configure PluginHub with our mock while preserving the original state
    original_registry = PluginHub._registry
    original_lock = PluginHub._lock
    PluginHub._registry = mock_registry
    PluginHub._lock = asyncio.Lock()

    try:
        # Call _resolve_session_id when no session is available
        # It should wait and retry until the session appears
        session_id = await PluginHub._resolve_session_id(unity_instance=None)

        # Should have retried and eventually found the session
        assert session_id == "test-session-123"
        assert call_count[0] >= 3  # Should have tried at least 3 times

    finally:
        # Clean up: restore original PluginHub state
        PluginHub._registry = original_registry
        PluginHub._lock = original_lock


@pytest.mark.asyncio
async def test_plugin_hub_fails_after_timeout():
    """Test that PluginHub._resolve_session_id eventually times out if plugin never reconnects."""
    from transport.plugin_hub import PluginHub
    from transport.plugin_registry import PluginRegistry

    # Create a mock registry that never returns sessions
    mock_registry = AsyncMock(spec=PluginRegistry)

    async def mock_list_sessions():
        return {}  # Never returns sessions

    mock_registry.list_sessions = mock_list_sessions

    # Configure PluginHub with our mock while preserving the original state
    original_registry = PluginHub._registry
    original_lock = PluginHub._lock
    PluginHub._registry = mock_registry
    PluginHub._lock = asyncio.Lock()

    # Temporarily override config for a short timeout
    with patch('transport.plugin_hub.config') as mock_config:
        mock_config.reload_max_retries = 3  # Only 3 retries
        mock_config.reload_retry_ms = 10    # 10ms between retries

        try:
            # Should raise RuntimeError after timeout
            with pytest.raises(RuntimeError, match="No Unity plugins are currently connected"):
                await PluginHub._resolve_session_id(unity_instance=None)
        finally:
            # Clean up: restore original PluginHub state
            PluginHub._registry = original_registry
            PluginHub._lock = original_lock


@pytest.mark.asyncio
async def test_read_console_during_simulated_reload(monkeypatch):
    """
    Simulate the stress test: create script (triggers reload) + rapid read_console calls.

    This test simulates what happens when:
    1. A script is created (triggering domain reload)
    2. Multiple read_console calls are made immediately
    3. The plugin disconnects and reconnects during those calls
    """
    # Setup tools
    from services.tools.read_console import read_console

    call_count = [0]

    async def fake_send_command(*args, **kwargs):
        """Simulate successful command execution."""
        call_count[0] += 1
        return {
            "success": True,
            "message": f"Retrieved {call_count[0]} log entries.",
            "data": ["<b><color=#2EA3FF>MCP-FOR-UNITY</color></b>: Auto-discovered 10 tools"]
        }

    # Patch the async_send_command_with_retry directly
    import services.tools.read_console
    monkeypatch.setattr(
        services.tools.read_console,
        "async_send_command_with_retry",
        fake_send_command
    )

    # Run multiple read_console calls rapidly (simulating the stress test)
    results = []
    for i in range(5):
        result = await read_console(
            ctx=DummyContext(),
            action="get",
            types=["all"],
            count=50,
            format="plain",
            include_stacktrace=False
        )
        results.append(result)

    # All calls should succeed
    assert len(results) == 5
    for i, result in enumerate(results):
        assert result["success"] is True, f"Call {i+1} failed with result: {result}"
        assert "data" in result

    # At least 5 calls should have been made
    assert call_count[0] == 5


@pytest.mark.asyncio
async def test_plugin_hub_respects_unity_instance_preference():
    """Test that _resolve_session_id prefers a specific Unity instance if requested."""
    from transport.plugin_hub import PluginHub
    from transport.plugin_registry import PluginRegistry, PluginSession

    # Create a mock registry with two sessions
    mock_registry = AsyncMock(spec=PluginRegistry)

    now = datetime.now()
    session1 = PluginSession(
        session_id="session-1",
        project_name="Project1",
        project_hash="hash1",
        unity_version="2022.3.0f1",
        registered_at=now,
        connected_at=now
    )
    session2 = PluginSession(
        session_id="session-2",
        project_name="Project2",
        project_hash="hash2",
        unity_version="2022.3.0f1",
        registered_at=now,
        connected_at=now
    )

    async def mock_list_sessions():
        return {
            "session-1": session1,
            "session-2": session2
        }

    async def mock_get_session_by_hash(project_hash):
        if project_hash == "hash2":
            return "session-2"
        return None

    mock_registry.list_sessions = mock_list_sessions
    mock_registry.get_session_id_by_hash = mock_get_session_by_hash

    # Configure PluginHub with our mock while preserving the original state
    original_registry = PluginHub._registry
    original_lock = PluginHub._lock
    PluginHub._registry = mock_registry
    PluginHub._lock = asyncio.Lock()

    try:
        # Request specific Unity instance
        session_id = await PluginHub._resolve_session_id(unity_instance="hash2")

        # Should return the requested instance
        assert session_id == "session-2"

        # Request default (no specific instance)
        with pytest.raises(RuntimeError) as exc:
            await PluginHub._resolve_session_id(unity_instance=None)
        assert "Multiple Unity instances are connected" in str(exc.value)

    finally:
        # Clean up: restore original PluginHub state
        PluginHub._registry = original_registry
        PluginHub._lock = original_lock
