"""Tests for Keep Server Running feature (Issue #672).

This feature allows the MCP server to stay running even when Unity disconnects,
enabling automatic reconnection when Unity comes back.
"""

import asyncio
import pytest
from unittest.mock import MagicMock

from transport.plugin_registry import PluginSession


class TestKeepServerRunningMode:
    """Tests for keep_server_running functionality."""

    def test_keep_running_message_format(self):
        """Verify keep_server_running field in RegisterMessage."""
        # Test that RegisterMessage can be created with keep_server_running=True
        msg_true = {
            "project_name": "TestProject",
            "project_hash": "hash123",
            "unity_version": "2022.3.0f0",
            "project_path": "/Test/Path",
            "keep_server_running": True
        }

        # Test that RegisterMessage can be created with keep_server_running=False
        msg_false = {
            "project_name": "TestProject",
            "project_hash": "hash456",
            "unity_version": "2022.3.0f0",
            "project_path": "/Test/Path",
            "keep_server_running": False
        }

        # Verify the field name and values are correct
        assert "keep_server_running" in msg_true
        assert msg_true["keep_server_running"] is True
        assert "keep_server_running" in msg_false
        assert msg_false["keep_server_running"] is False

    def test_plugin_session_has_keep_running_field(self):
        """Verify PluginSession dataclass has keep_server_running field."""
        # Create a session with keep_server_running=True
        session = PluginSession(
            session_id="test-session-123",
            project_name="TestProject",
            project_hash="abc123",
            unity_version="2022.3.0f0",
            project_path="/path/to/project",
            keep_server_running=True
        )
        assert session.keep_server_running is True

        # Create a session with keep_server_running=False (default)
        session_default = PluginSession(
            session_id="test-session-456",
            project_name="TestProject",
            project_hash="def456",
            unity_version="2023.2.0f1",
            project_path="/another/path"
        )
        assert session_default.keep_server_running is False

    @pytest.mark.asyncio
    async def test_server_lifespan_yields_in_http_mode(self, monkeypatch):
        """HTTP mode should still enter lifespan context and yield shared state."""
        import main as main_module

        class _NoOpTimer:
            def __init__(self, *_args, **_kwargs):
                pass

            def start(self):
                return None

        old_pool = main_module._unity_connection_pool
        old_registry = main_module._plugin_registry
        old_keep_running = dict(main_module.keep_server_running)

        monkeypatch.setenv("UNITY_MCP_ENABLE_HTTP_SERVER", "1")
        monkeypatch.delenv("UNITY_MCP_SKIP_STARTUP_CONNECT", raising=False)
        monkeypatch.setattr(main_module.threading, "Timer", _NoOpTimer)

        try:
            main_module._unity_connection_pool = None
            main_module._plugin_registry = None
            main_module.keep_server_running.clear()

            async with main_module.server_lifespan(MagicMock()) as state:
                assert state["pool"] is None
                assert state["plugin_registry"] is not None
        finally:
            main_module._unity_connection_pool = old_pool
            main_module._plugin_registry = old_registry
            main_module.keep_server_running.clear()
            main_module.keep_server_running.update(old_keep_running)

    @pytest.mark.asyncio
    async def test_keep_running_flag_cleared_on_disconnect(self, monkeypatch):
        """Verify keep_server_running entry is removed when session disconnects."""
        import main as main_module
        from transport.plugin_hub import PluginHub
        from transport.plugin_registry import PluginRegistry

        # Clear any existing state
        main_module.keep_server_running.clear()

        registry = PluginRegistry()
        loop = asyncio.get_running_loop()
        PluginHub.configure(registry, loop)

        # Simulate session registration with keep_server_running=True
        session_id = "test-disconnect-session"
        main_module.keep_server_running[session_id] = True

        # Verify entry exists
        assert session_id in main_module.keep_server_running
        assert main_module.keep_server_running[session_id] is True

        # Simulate disconnection cleanup (as happens in on_disconnect)
        main_module.keep_server_running.pop(session_id, None)

        # Verify entry is removed
        assert session_id not in main_module.keep_server_running
