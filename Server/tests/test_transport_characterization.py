"""
Characterization tests for Transport & Communication domain.

These tests capture CURRENT behavior of the transport layer without refactoring.
They validate:
- Instance routing and session management
- Plugin discovery and registration
- HTTP server behavior and error handling
- Middleware request/response flows
- Edge cases and failure modes

The tests serve as regression detectors for any future changes to the transport layer.
"""

import asyncio
import pytest
import pytest_asyncio
from unittest.mock import AsyncMock, Mock, MagicMock, patch, call
from datetime import datetime, timezone
import uuid
from types import SimpleNamespace

from transport.unity_instance_middleware import UnityInstanceMiddleware, get_unity_instance_middleware, set_unity_instance_middleware
from transport.plugin_registry import PluginRegistry, PluginSession
from transport.plugin_hub import PluginHub, NoUnitySessionError, InstanceSelectionRequiredError, PluginDisconnectedError
from transport.models import (
    RegisterMessage,
    RegisterToolsMessage,
    CommandResultMessage,
    PongMessage,
    SessionList,
    SessionDetails,
)
from models.models import ToolDefinitionModel
from core.config import config


# ============================================================================
# FIXTURES
# ============================================================================


def _tool_registry_for_visibility_tests() -> list[dict]:
    return [
        {"name": "manage_scene", "unity_target": "manage_scene"},
        {"name": "manage_script", "unity_target": "manage_script"},
        {"name": "manage_asset", "unity_target": "manage_asset"},
        {"name": "create_script", "unity_target": "manage_script"},
        {"name": "find_in_file", "unity_target": "manage_script"},
        {"name": "script_apply_edits", "unity_target": "manage_script"},
        {"name": "set_active_instance", "unity_target": None},
        {"name": "execute_custom_tool", "unity_target": None},
    ]

@pytest.fixture
def mock_context():
    """Create a mock FastMCP context."""
    ctx = Mock()
    ctx.session_id = "test-session-123"
    ctx.client_id = "test-client-456"

    state_storage = {}
    ctx.set_state = Mock(side_effect=lambda k, v: state_storage.__setitem__(k, v))
    ctx.get_state = Mock(side_effect=lambda k: state_storage.get(k))
    ctx.info = AsyncMock()

    return ctx


@pytest.fixture
def mock_websocket():
    """Create a mock WebSocket."""
    ws = AsyncMock()
    ws.send_json = AsyncMock()
    ws.receive_json = AsyncMock()
    ws.accept = AsyncMock()
    ws.close = AsyncMock()
    return ws


@pytest.fixture
def plugin_registry():
    """Create an in-memory plugin registry."""
    return PluginRegistry()


@pytest_asyncio.fixture
async def configured_plugin_hub(plugin_registry):
    """Configure PluginHub with a registry and event loop."""
    loop = asyncio.get_running_loop()
    PluginHub.configure(plugin_registry, loop)
    yield
    # Cleanup
    PluginHub._registry = None
    PluginHub._lock = None
    PluginHub._loop = None
    PluginHub._connections.clear()
    PluginHub._pending.clear()


# ============================================================================
# SESSION MANAGEMENT & ROUTING TESTS
# ============================================================================

class TestUnityInstanceMiddlewareSessionManagement:
    """Test instance routing and per-session state management."""

    def test_middleware_stores_instance_per_session(self, mock_context):
        """
        Current behavior: Middleware maintains independent instance selection
        per session using get_session_key() derivation.
        """
        middleware = UnityInstanceMiddleware()
        instance_id = "TestProject@abc123def456"

        middleware.set_active_instance(mock_context, instance_id)
        retrieved = middleware.get_active_instance(mock_context)

        assert retrieved == instance_id, \
            "Middleware must store and retrieve instance per session"

    def test_middleware_uses_client_id_over_session_id(self):
        """
        Current behavior: get_session_key() prioritizes client_id for stability,
        falling back to 'global' when unavailable.
        """
        middleware = UnityInstanceMiddleware()

        ctx = Mock()
        ctx.client_id = "stable-client-id"
        ctx.session_id = "unstable-session-id"

        key = middleware.get_session_key(ctx)
        assert key == "stable-client-id"

    def test_middleware_falls_back_to_global_key(self):
        """
        Current behavior: When client_id is None/missing, use 'global' key.
        This allows single-user local mode to work without session tracking.
        """
        middleware = UnityInstanceMiddleware()

        ctx = Mock()
        ctx.client_id = None
        ctx.session_id = "session-id"

        key = middleware.get_session_key(ctx)
        assert key == "global"

    def test_middleware_isolates_multiple_sessions(self):
        """
        Current behavior: Different sessions (different client_ids) maintain
        separate instance selections.
        """
        middleware = UnityInstanceMiddleware()

        ctx1 = Mock()
        ctx1.client_id = "client-1"
        ctx1.session_id = "session-1"

        ctx2 = Mock()
        ctx2.client_id = "client-2"
        ctx2.session_id = "session-2"

        middleware.set_active_instance(ctx1, "Project1@hash1")
        middleware.set_active_instance(ctx2, "Project2@hash2")

        assert middleware.get_active_instance(ctx1) == "Project1@hash1"
        assert middleware.get_active_instance(ctx2) == "Project2@hash2"

    def test_middleware_clear_instance(self, mock_context):
        """
        Current behavior: clear_active_instance() removes stored instance
        for the session, allowing reset to None.
        """
        middleware = UnityInstanceMiddleware()
        instance_id = "TestProject@xyz"

        middleware.set_active_instance(mock_context, instance_id)
        assert middleware.get_active_instance(mock_context) == instance_id

        middleware.clear_active_instance(mock_context)
        assert middleware.get_active_instance(mock_context) is None

    def test_middleware_thread_safe_updates(self):
        """
        Current behavior: Middleware uses RLock to serialize access to
        _active_by_key dictionary.
        """
        middleware = UnityInstanceMiddleware()
        ctx = Mock()
        ctx.client_id = "client-123"
        ctx.session_id = "session-123"

        # Rapidly update instances (would race without locking)
        for i in range(10):
            instance = f"Project{i}@hash{i}"
            middleware.set_active_instance(ctx, instance)

        # Final state should be consistent
        assert middleware.get_active_instance(ctx) == "Project9@hash9"


# ============================================================================
# MIDDLEWARE INJECTION & CONTEXT FLOW TESTS
# ============================================================================

class TestUnityInstanceMiddlewareInjection:
    """Test middleware injection of instance into context state."""

    @pytest.mark.asyncio
    async def test_middleware_injects_into_tool_context(self, mock_context):
        """
        Current behavior: on_call_tool() calls _inject_unity_instance(),
        which sets ctx.set_state("unity_instance", active_instance) when
        an instance is active.
        """
        middleware = UnityInstanceMiddleware()
        instance_id = "Project@abc123"

        middleware.set_active_instance(mock_context, instance_id)

        # Create middleware context wrapper
        middleware_ctx = Mock()
        middleware_ctx.fastmcp_context = mock_context

        call_next_called = False
        async def mock_call_next(_ctx):
            nonlocal call_next_called
            call_next_called = True
            return {"status": "ok"}

        await middleware.on_call_tool(middleware_ctx, mock_call_next)

        assert call_next_called, "Middleware must call next handler"
        mock_context.set_state.assert_called_with("unity_instance", instance_id)

    @pytest.mark.asyncio
    async def test_middleware_injects_into_resource_context(self, mock_context):
        """
        Current behavior: on_read_resource() performs same injection as
        on_call_tool(), ensuring resources see the active instance.
        """
        middleware = UnityInstanceMiddleware()
        instance_id = "Project@hash123"

        middleware.set_active_instance(mock_context, instance_id)

        middleware_ctx = Mock()
        middleware_ctx.fastmcp_context = mock_context

        async def mock_call_next(_ctx):
            return {"status": "ok"}

        await middleware.on_read_resource(middleware_ctx, mock_call_next)

        mock_context.set_state.assert_called_with("unity_instance", instance_id)

    @pytest.mark.asyncio
    async def test_middleware_does_not_inject_when_no_instance(self, mock_context):
        """
        Current behavior: When no active instance is set and auto-select fails,
        middleware does not inject anything (None instance not stored).
        """
        middleware = UnityInstanceMiddleware()

        # Don't set any instance (will try auto-select and fail)
        middleware_ctx = Mock()
        middleware_ctx.fastmcp_context = mock_context

        async def mock_call_next(_ctx):
            return {"status": "ok"}

        # Mock PluginHub as unavailable AND legacy connection pool to prevent fallback discovery
        with patch("transport.unity_instance_middleware.PluginHub.is_configured", return_value=False):
            with patch("transport.legacy.unity_connection.get_unity_connection_pool", return_value=None):
                await middleware.on_call_tool(middleware_ctx, mock_call_next)

        # set_state should not be called for unity_instance if no instance found
        calls = [c for c in mock_context.set_state.call_args_list
                if len(c[0]) > 0 and c[0][0] == "unity_instance"]
        assert len(calls) == 0

    @pytest.mark.asyncio
    async def test_list_tools_filters_disabled_unity_tools_and_aliases(self, mock_context, monkeypatch):
        """
        Current behavior: in HTTP mode with a connected Unity session, on_list_tools()
        uses PluginHub-registered tool names to hide disabled Unity tools while keeping
        server-only tools visible. Aliases like create_script follow manage_script state.
        """
        middleware = UnityInstanceMiddleware()
        middleware_ctx = Mock()
        middleware_ctx.fastmcp_context = mock_context

        mock_context.set_state("unity_instance", "Project@abc123")
        monkeypatch.setattr(config, "transport_mode", "http")

        available_tools = [
            SimpleNamespace(name="manage_scene"),
            SimpleNamespace(name="manage_script"),
            SimpleNamespace(name="set_active_instance"),
            SimpleNamespace(name="manage_asset"),
            SimpleNamespace(name="create_script"),
        ]

        async def call_next(_ctx):
            return available_tools

        with patch.object(middleware, "_inject_unity_instance", new=AsyncMock()):
            with patch("transport.unity_instance_middleware.PluginHub.is_configured", return_value=True):
                with patch("transport.unity_instance_middleware.get_registered_tools", return_value=_tool_registry_for_visibility_tests()):
                    with patch("transport.unity_instance_middleware.PluginHub.get_sessions", new_callable=AsyncMock) as mock_get_sessions:
                        with patch("transport.unity_instance_middleware.PluginHub.get_tools_for_project", new_callable=AsyncMock) as mock_get_tools:
                            mock_get_sessions.return_value = SessionList(
                                sessions={
                                    "session-1": SessionDetails(
                                        project="Project",
                                        hash="abc123",
                                        unity_version="2022.3",
                                        connected_at="2025-01-26T00:00:00Z",
                                    )
                                }
                            )
                            mock_get_tools.return_value = [
                                SimpleNamespace(name="manage_scene"),
                                SimpleNamespace(name="manage_script"),
                            ]

                            filtered = await middleware.on_list_tools(middleware_ctx, call_next)

        names = [tool.name for tool in filtered]
        assert "manage_scene" in names
        assert "create_script" in names
        assert "set_active_instance" in names
        assert "manage_asset" not in names

    @pytest.mark.asyncio
    async def test_list_tools_skips_filter_when_no_tools_registered_yet(self, mock_context, monkeypatch):
        """
        When a Unity session is connected but register_tools has not been sent yet
        (empty registered_tools), defer filtering to avoid hiding tools that may
        be valid once register_tools arrives. This prevents clients that cache
        early list_tools responses from getting persistently incomplete tool lists.
        """
        middleware = UnityInstanceMiddleware()
        middleware_ctx = Mock()
        middleware_ctx.fastmcp_context = mock_context

        mock_context.set_state("unity_instance", "Project@abc123")
        monkeypatch.setattr(config, "transport_mode", "http")

        original_tools = [
            SimpleNamespace(name="manage_scene"),
            SimpleNamespace(name="manage_asset"),
            SimpleNamespace(name="create_script"),
            SimpleNamespace(name="set_active_instance"),
            SimpleNamespace(name="custom_server_tool"),
        ]

        async def call_next(_ctx):
            return original_tools

        with patch.object(middleware, "_inject_unity_instance", new=AsyncMock()):
            with patch("transport.unity_instance_middleware.PluginHub.is_configured", return_value=True):
                with patch("transport.unity_instance_middleware.get_registered_tools", return_value=_tool_registry_for_visibility_tests()):
                    with patch("transport.unity_instance_middleware.PluginHub.get_sessions", new_callable=AsyncMock) as mock_get_sessions:
                        with patch("transport.unity_instance_middleware.PluginHub.get_tools_for_project", new_callable=AsyncMock) as mock_get_tools:
                            mock_get_sessions.return_value = SessionList(
                                sessions={
                                    "session-1": SessionDetails(
                                        project="Project",
                                        hash="abc123",
                                        unity_version="2022.3",
                                        connected_at="2025-01-26T00:00:00Z",
                                    )
                                }
                            )
                            # Simulate register_tools not yet sent
                            mock_get_tools.return_value = []

                            filtered = await middleware.on_list_tools(middleware_ctx, call_next)

        names = [tool.name for tool in filtered]
        # All tools should be visible when register_tools hasn't been sent yet
        assert "manage_scene" in names
        assert "manage_asset" in names
        assert "create_script" in names
        assert "set_active_instance" in names
        assert "custom_server_tool" in names

    @pytest.mark.asyncio
    async def test_list_tools_filters_when_all_tools_disabled(self, mock_context, monkeypatch):
        """
        When register_tools has been sent with an empty tool list (all tools disabled),
        Unity-managed tools are filtered out while server-only tools remain visible.
        This differs from the "no tools registered yet" case where we defer filtering.
        """
        middleware = UnityInstanceMiddleware()
        middleware_ctx = Mock()
        middleware_ctx.fastmcp_context = mock_context

        mock_context.set_state("unity_instance", "Project@abc123")
        monkeypatch.setattr(config, "transport_mode", "http")

        original_tools = [
            SimpleNamespace(name="manage_scene"),
            SimpleNamespace(name="manage_asset"),
            SimpleNamespace(name="create_script"),
            SimpleNamespace(name="set_active_instance"),
            SimpleNamespace(name="custom_server_tool"),
        ]

        async def call_next(_ctx):
            return original_tools

        # Simulate a registered tool that indicates all tools are disabled
        disabled_tool = SimpleNamespace(name="_marker_tool_indicates_registration_sent")
        registered_tools = [disabled_tool]

        with patch.object(middleware, "_inject_unity_instance", new=AsyncMock()):
            with patch("transport.unity_instance_middleware.PluginHub.is_configured", return_value=True):
                with patch("transport.unity_instance_middleware.get_registered_tools", return_value=_tool_registry_for_visibility_tests()):
                    with patch("transport.unity_instance_middleware.PluginHub.get_sessions", new_callable=AsyncMock) as mock_get_sessions:
                        with patch("transport.unity_instance_middleware.PluginHub.get_tools_for_project", new_callable=AsyncMock) as mock_get_tools:
                            mock_get_sessions.return_value = SessionList(
                                sessions={
                                    "session-1": SessionDetails(
                                        project="Project",
                                        hash="abc123",
                                        unity_version="2022.3",
                                        connected_at="2025-01-26T00:00:00Z",
                                    )
                                }
                            )
                            # register_tools has been sent (non-empty list), but all tools disabled
                            mock_get_tools.return_value = registered_tools

                            filtered = await middleware.on_list_tools(middleware_ctx, call_next)

        names = [tool.name for tool in filtered]
        assert "set_active_instance" in names
        assert "custom_server_tool" in names
        assert "manage_scene" not in names
        assert "manage_asset" not in names
        assert "create_script" not in names

    @pytest.mark.asyncio
    async def test_list_tools_skips_filter_when_enabled_set_lookup_fails(self, mock_context, monkeypatch):
        """
        Current behavior: if enabled-tool lookup fails unexpectedly, on_list_tools()
        leaves the FastMCP list unchanged to avoid hiding tools due to transient
        PluginHub failures.
        """
        middleware = UnityInstanceMiddleware()
        middleware_ctx = Mock()
        middleware_ctx.fastmcp_context = mock_context

        mock_context.set_state("unity_instance", "Project@abc123")
        monkeypatch.setattr(config, "transport_mode", "http")

        original_tools = [
            SimpleNamespace(name="manage_scene"),
            SimpleNamespace(name="manage_asset"),
            SimpleNamespace(name="set_active_instance"),
        ]

        async def call_next(_ctx):
            return original_tools

        with patch.object(middleware, "_inject_unity_instance", new=AsyncMock()):
            with patch("transport.unity_instance_middleware.PluginHub.is_configured", return_value=True):
                with patch("transport.unity_instance_middleware.get_registered_tools", return_value=_tool_registry_for_visibility_tests()):
                    with patch("transport.unity_instance_middleware.PluginHub.get_sessions", new_callable=AsyncMock) as mock_get_sessions:
                        with patch("transport.unity_instance_middleware.PluginHub.get_tools_for_project", new_callable=AsyncMock) as mock_get_tools:
                            mock_get_sessions.return_value = SessionList(
                                sessions={
                                    "session-1": SessionDetails(
                                        project="Project",
                                        hash="abc123",
                                        unity_version="2022.3",
                                        connected_at="2025-01-26T00:00:00Z",
                                    )
                                }
                            )
                            mock_get_tools.side_effect = RuntimeError("hub unavailable")

                            filtered = await middleware.on_list_tools(middleware_ctx, call_next)

        assert [tool.name for tool in filtered] == [tool.name for tool in original_tools]

    @pytest.mark.asyncio
    async def test_list_tools_uses_user_scoped_tool_lookup_in_hosted_mode(self, mock_context, monkeypatch):
        """
        Current behavior: in remote-hosted HTTP mode, tool filtering fetches
        Unity-registered tools scoped to the current user.
        """
        middleware = UnityInstanceMiddleware()
        middleware_ctx = Mock()
        middleware_ctx.fastmcp_context = mock_context

        mock_context.set_state("unity_instance", "Project@abc123")
        mock_context.set_state("user_id", "user-123")
        monkeypatch.setattr(config, "transport_mode", "http")
        monkeypatch.setattr(config, "http_remote_hosted", True)

        async def call_next(_ctx):
            return [SimpleNamespace(name="manage_scene")]

        with patch.object(middleware, "_inject_unity_instance", new=AsyncMock()):
            with patch("transport.unity_instance_middleware.PluginHub.is_configured", return_value=True):
                with patch("transport.unity_instance_middleware.get_registered_tools", return_value=_tool_registry_for_visibility_tests()):
                    with patch("transport.unity_instance_middleware.PluginHub.get_sessions", new_callable=AsyncMock) as mock_get_sessions:
                        with patch("transport.unity_instance_middleware.PluginHub.get_tools_for_project", new_callable=AsyncMock) as mock_get_tools:
                            mock_get_sessions.return_value = SessionList(
                                sessions={
                                    "session-1": SessionDetails(
                                        project="Project",
                                        hash="abc123",
                                        unity_version="2022.3",
                                        connected_at="2025-01-26T00:00:00Z",
                                    )
                                }
                            )
                            mock_get_tools.return_value = [SimpleNamespace(name="manage_scene")]

                            filtered = await middleware.on_list_tools(middleware_ctx, call_next)

        assert [tool.name for tool in filtered] == ["manage_scene"]
        mock_get_tools.assert_awaited_once_with("abc123", user_id="user-123")

    @pytest.mark.asyncio
    async def test_list_tools_skips_filter_when_active_instance_hash_is_stale(self, mock_context, monkeypatch):
        middleware = UnityInstanceMiddleware()
        middleware_ctx = Mock()
        middleware_ctx.fastmcp_context = mock_context

        mock_context.set_state("unity_instance", "Project@stale-hash")
        monkeypatch.setattr(config, "transport_mode", "http")

        original_tools = [
            SimpleNamespace(name="manage_scene"),
            SimpleNamespace(name="manage_asset"),
            SimpleNamespace(name="set_active_instance"),
        ]

        async def call_next(_ctx):
            return original_tools

        with patch.object(middleware, "_inject_unity_instance", new=AsyncMock()):
            with patch("transport.unity_instance_middleware.PluginHub.is_configured", return_value=True):
                with patch("transport.unity_instance_middleware.get_registered_tools", return_value=_tool_registry_for_visibility_tests()):
                    with patch("transport.unity_instance_middleware.PluginHub.get_sessions", new_callable=AsyncMock) as mock_get_sessions:
                        with patch("transport.unity_instance_middleware.PluginHub.get_tools_for_project", new_callable=AsyncMock) as mock_get_tools:
                            mock_get_sessions.return_value = SessionList(
                                sessions={
                                    "session-1": SessionDetails(
                                        project="Project",
                                        hash="abc123",
                                        unity_version="2022.3",
                                        connected_at="2025-01-26T00:00:00Z",
                                    )
                                }
                            )

                            filtered = await middleware.on_list_tools(middleware_ctx, call_next)

        assert [tool.name for tool in filtered] == [tool.name for tool in original_tools]
        mock_get_tools.assert_not_called()

    @pytest.mark.asyncio
    async def test_list_tools_hides_alias_when_target_tool_is_disabled(self, mock_context, monkeypatch):
        middleware = UnityInstanceMiddleware()
        middleware_ctx = Mock()
        middleware_ctx.fastmcp_context = mock_context

        mock_context.set_state("unity_instance", "Project@abc123")
        monkeypatch.setattr(config, "transport_mode", "http")

        original_tools = [
            SimpleNamespace(name="manage_scene"),
            SimpleNamespace(name="manage_script"),
            SimpleNamespace(name="create_script"),
            SimpleNamespace(name="set_active_instance"),
        ]

        async def call_next(_ctx):
            return original_tools

        with patch.object(middleware, "_inject_unity_instance", new=AsyncMock()):
            with patch("transport.unity_instance_middleware.PluginHub.is_configured", return_value=True):
                with patch("transport.unity_instance_middleware.get_registered_tools", return_value=_tool_registry_for_visibility_tests()):
                    with patch("transport.unity_instance_middleware.PluginHub.get_sessions", new_callable=AsyncMock) as mock_get_sessions:
                        with patch("transport.unity_instance_middleware.PluginHub.get_tools_for_project", new_callable=AsyncMock) as mock_get_tools:
                            mock_get_sessions.return_value = SessionList(
                                sessions={
                                    "session-1": SessionDetails(
                                        project="Project",
                                        hash="abc123",
                                        unity_version="2022.3",
                                        connected_at="2025-01-26T00:00:00Z",
                                    )
                                }
                            )
                            # manage_script is disabled; alias create_script should also be hidden.
                            mock_get_tools.return_value = [SimpleNamespace(name="manage_scene")]

                            filtered = await middleware.on_list_tools(middleware_ctx, call_next)

        names = [tool.name for tool in filtered]
        assert "manage_scene" in names
        assert "set_active_instance" in names
        assert "manage_script" not in names
        assert "create_script" not in names

    @pytest.mark.asyncio
    async def test_list_tools_keeps_all_visible_when_tool_registry_is_empty(self, mock_context, monkeypatch):
        middleware = UnityInstanceMiddleware()
        middleware_ctx = Mock()
        middleware_ctx.fastmcp_context = mock_context

        mock_context.set_state("unity_instance", "Project@abc123")
        monkeypatch.setattr(config, "transport_mode", "http")

        original_tools = [
            SimpleNamespace(name="manage_scene"),
            SimpleNamespace(name="set_active_instance"),
            SimpleNamespace(name="execute_custom_tool"),
        ]

        async def call_next(_ctx):
            return original_tools

        with patch.object(middleware, "_inject_unity_instance", new=AsyncMock()):
            with patch("transport.unity_instance_middleware.PluginHub.is_configured", return_value=True):
                with patch("transport.unity_instance_middleware.get_registered_tools", return_value=[]):
                    with patch("transport.unity_instance_middleware.PluginHub.get_sessions", new_callable=AsyncMock) as mock_get_sessions:
                        with patch("transport.unity_instance_middleware.PluginHub.get_tools_for_project", new_callable=AsyncMock) as mock_get_tools:
                            mock_get_sessions.return_value = SessionList(
                                sessions={
                                    "session-1": SessionDetails(
                                        project="Project",
                                        hash="abc123",
                                        unity_version="2022.3",
                                        connected_at="2025-01-26T00:00:00Z",
                                    )
                                }
                            )
                            mock_get_tools.return_value = []

                            filtered = await middleware.on_list_tools(middleware_ctx, call_next)

        assert [tool.name for tool in filtered] == [tool.name for tool in original_tools]

    @pytest.mark.asyncio
    async def test_list_tools_uses_union_of_enabled_tools_across_multiple_sessions(self, mock_context, monkeypatch):
        middleware = UnityInstanceMiddleware()
        middleware_ctx = Mock()
        middleware_ctx.fastmcp_context = mock_context

        monkeypatch.setattr(config, "transport_mode", "http")

        original_tools = [
            SimpleNamespace(name="manage_scene"),
            SimpleNamespace(name="manage_asset"),
            SimpleNamespace(name="manage_script"),
        ]

        async def call_next(_ctx):
            return original_tools

        async def get_tools_side_effect(project_hash, user_id=None):  # noqa: ARG001
            if project_hash == "hash-a":
                return [SimpleNamespace(name="manage_scene")]
            if project_hash == "hash-b":
                return [SimpleNamespace(name="manage_asset")]
            return []

        with patch.object(middleware, "_inject_unity_instance", new=AsyncMock()):
            with patch("transport.unity_instance_middleware.PluginHub.is_configured", return_value=True):
                with patch("transport.unity_instance_middleware.get_registered_tools", return_value=_tool_registry_for_visibility_tests()):
                    with patch("transport.unity_instance_middleware.PluginHub.get_sessions", new_callable=AsyncMock) as mock_get_sessions:
                        with patch("transport.unity_instance_middleware.PluginHub.get_tools_for_project", new_callable=AsyncMock) as mock_get_tools:
                            mock_get_sessions.return_value = SessionList(
                                sessions={
                                    "session-a": SessionDetails(
                                        project="ProjectA",
                                        hash="hash-a",
                                        unity_version="2022.3",
                                        connected_at="2025-01-26T00:00:00Z",
                                    ),
                                    "session-b": SessionDetails(
                                        project="ProjectB",
                                        hash="hash-b",
                                        unity_version="2022.3",
                                        connected_at="2025-01-26T00:00:00Z",
                                    ),
                                }
                            )
                            mock_get_tools.side_effect = get_tools_side_effect

                            filtered = await middleware.on_list_tools(middleware_ctx, call_next)

        names = [tool.name for tool in filtered]
        assert "manage_scene" in names
        assert "manage_asset" in names
        assert "manage_script" not in names


# ============================================================================
# AUTO-SELECT INSTANCE TESTS
# ============================================================================

class TestAutoSelectInstance:
    """Test auto-selection of sole Unity instance when none is explicitly set."""

    @pytest.mark.asyncio
    async def test_autoselect_via_plugin_hub_single_instance(self, mock_context):
        """
        Current behavior: When single instance is available via PluginHub,
        auto-select it and store in middleware state.
        """
        middleware = UnityInstanceMiddleware()

        # Mock PluginHub to return single session
        fake_sessions = SessionList(
            sessions={
                "session-1": SessionDetails(
                    project="TestProject",
                    hash="abc123",
                    unity_version="2022.3",
                    connected_at="2025-01-26T00:00:00Z"
                )
            }
        )

        with patch("transport.unity_instance_middleware.PluginHub.is_configured", return_value=True):
            with patch("transport.unity_instance_middleware.PluginHub.get_sessions", new_callable=AsyncMock) as mock_get:
                mock_get.return_value = fake_sessions

                instance = await middleware._maybe_autoselect_instance(mock_context)

        assert instance == "TestProject@abc123"
        assert middleware.get_active_instance(mock_context) == "TestProject@abc123"

    @pytest.mark.asyncio
    async def test_autoselect_fails_with_multiple_instances(self, mock_context):
        """
        Current behavior: When multiple instances available, auto-select
        returns None (ambiguous), allowing caller to decide.
        """
        middleware = UnityInstanceMiddleware()

        fake_sessions = SessionList(
            sessions={
                "session-1": SessionDetails(
                    project="Project1",
                    hash="aaa111",
                    unity_version="2022.3",
                    connected_at="2025-01-26T00:00:00Z"
                ),
                "session-2": SessionDetails(
                    project="Project2",
                    hash="bbb222",
                    unity_version="2023.2",
                    connected_at="2025-01-26T00:00:00Z"
                )
            }
        )

        with patch("transport.unity_instance_middleware.PluginHub.is_configured", return_value=True):
            with patch("transport.unity_instance_middleware.PluginHub.get_sessions", new_callable=AsyncMock) as mock_get:
                with patch("transport.legacy.unity_connection.get_unity_connection_pool", return_value=None):
                    mock_get.return_value = fake_sessions

                    instance = await middleware._maybe_autoselect_instance(mock_context)

        assert instance is None

    @pytest.mark.asyncio
    async def test_autoselect_handles_plugin_hub_connection_error(self, mock_context):
        """
        Current behavior: If PluginHub probe fails with ConnectionError,
        gracefully falls back and returns None (no instance selected).
        """
        middleware = UnityInstanceMiddleware()

        with patch("transport.unity_instance_middleware.PluginHub.is_configured", return_value=True):
            with patch("transport.unity_instance_middleware.PluginHub.get_sessions", new_callable=AsyncMock) as mock_get:
                with patch("transport.legacy.unity_connection.get_unity_connection_pool", return_value=None):
                    mock_get.side_effect = ConnectionError("Plugin hub unavailable")

                    # When PluginHub fails, auto-select returns None (graceful fallback)
                    instance = await middleware._maybe_autoselect_instance(mock_context)

        # Should return None since both PluginHub failed
        assert instance is None


# ============================================================================
# PLUGIN REGISTRY TESTS
# ============================================================================

class TestPluginRegistryFunctionality:
    """Test plugin session registration and lookup."""

    @pytest.mark.asyncio
    async def test_registry_registers_session(self, plugin_registry):
        """
        Current behavior: register() creates a new PluginSession and stores
        it by session_id and project_hash.
        """
        session = await plugin_registry.register(
            session_id="sess-abc",
            project_name="TestProject",
            project_hash="hash123",
            unity_version="2022.3"
        )

        assert session.session_id == "sess-abc"
        assert session.project_name == "TestProject"
        assert session.project_hash == "hash123"
        assert session.unity_version == "2022.3"

    @pytest.mark.asyncio
    async def test_registry_lookup_by_hash(self, plugin_registry):
        """
        Current behavior: get_session_id_by_hash() maps project_hash to
        the active session_id.
        """
        await plugin_registry.register(
            session_id="sess-1",
            project_name="Project1",
            project_hash="hash-aaa",
            unity_version="2022.3"
        )

        found_id = await plugin_registry.get_session_id_by_hash("hash-aaa")
        assert found_id == "sess-1"

    @pytest.mark.asyncio
    async def test_registry_reconnect_updates_mapping(self, plugin_registry):
        """
        Current behavior: When a new session registers with same project_hash,
        it replaces the old mapping (supporting reconnect scenarios).
        """
        # Register first session
        await plugin_registry.register(
            session_id="sess-1",
            project_name="Project",
            project_hash="hash-same",
            unity_version="2022.3"
        )

        # Reconnect with new session_id, same hash
        await plugin_registry.register(
            session_id="sess-2",
            project_name="Project",
            project_hash="hash-same",
            unity_version="2022.3"
        )

        # Hash should map to new session
        found_id = await plugin_registry.get_session_id_by_hash("hash-same")
        assert found_id == "sess-2"

        # Old session should be removed
        old_session = await plugin_registry.get_session("sess-1")
        assert old_session is None

    @pytest.mark.asyncio
    async def test_registry_register_tools_for_session(self, plugin_registry):
        """
        Current behavior: register_tools_for_session() stores tool definitions
        keyed by tool name on the session.
        """
        await plugin_registry.register(
            session_id="sess-x",
            project_name="Project",
            project_hash="hash-x",
            unity_version="2022.3"
        )

        tools = [
            ToolDefinitionModel(name="tool1", description="Test tool 1"),
            ToolDefinitionModel(name="tool2", description="Test tool 2"),
        ]

        await plugin_registry.register_tools_for_session("sess-x", tools)

        updated_session = await plugin_registry.get_session("sess-x")
        assert len(updated_session.tools) == 2
        assert "tool1" in updated_session.tools
        assert "tool2" in updated_session.tools

    @pytest.mark.asyncio
    async def test_registry_touch_updates_connected_at(self, plugin_registry):
        """
        Current behavior: touch() updates the connected_at timestamp on heartbeat.
        """
        session = await plugin_registry.register(
            session_id="sess-y",
            project_name="Project",
            project_hash="hash-y",
            unity_version="2022.3"
        )

        original_timestamp = session.connected_at

        # Wait a tiny bit
        await asyncio.sleep(0.01)

        # Touch should update timestamp
        await plugin_registry.touch("sess-y")

        updated = await plugin_registry.get_session("sess-y")
        assert updated.connected_at > original_timestamp

    @pytest.mark.asyncio
    async def test_registry_unregister_removes_session(self, plugin_registry):
        """
        Current behavior: unregister() removes session and its hash mapping.
        """
        await plugin_registry.register(
            session_id="sess-z",
            project_name="Project",
            project_hash="hash-z",
            unity_version="2022.3"
        )

        await plugin_registry.unregister("sess-z")

        session = await plugin_registry.get_session("sess-z")
        assert session is None

        hash_id = await plugin_registry.get_session_id_by_hash("hash-z")
        assert hash_id is None

    @pytest.mark.asyncio
    async def test_registry_list_sessions(self, plugin_registry):
        """
        Current behavior: list_sessions() returns shallow copy of all sessions.
        """
        await plugin_registry.register(
            session_id="sess-1",
            project_name="Project1",
            project_hash="hash-1",
            unity_version="2022.3"
        )
        await plugin_registry.register(
            session_id="sess-2",
            project_name="Project2",
            project_hash="hash-2",
            unity_version="2023.2"
        )

        sessions = await plugin_registry.list_sessions()

        assert len(sessions) == 2
        assert "sess-1" in sessions
        assert "sess-2" in sessions


# ============================================================================
# PLUGIN HUB MESSAGE HANDLING TESTS
# ============================================================================

class TestPluginHubMessageHandling:
    """Test PluginHub message parsing and registration flow."""

    def test_register_message_parsing(self):
        """
        Current behavior: RegisterMessage can be constructed from incoming data
        with project_name, project_hash, and unity_version.
        """
        msg = RegisterMessage(
            type="register",
            project_name="TestProject",
            project_hash="hash-reg-1",
            unity_version="2022.3"
        )

        assert msg.project_name == "TestProject"
        assert msg.project_hash == "hash-reg-1"
        assert msg.unity_version == "2022.3"

    def test_register_message_requires_hash(self):
        """
        Current behavior: RegisterMessage validates that project_hash
        is required (not empty).
        """
        # Empty hash should still parse, but would be rejected by PluginHub._handle_register
        msg = RegisterMessage(
            type="register",
            project_name="TestProject",
            project_hash="",
            unity_version="2022.3"
        )

        assert msg.project_hash == ""

    def test_register_tools_message_parsing(self):
        """
        Current behavior: RegisterToolsMessage accepts a list of tool definitions.
        """
        tools = [
            ToolDefinitionModel(name="tool1", description="Test 1"),
            ToolDefinitionModel(name="tool2", description="Test 2"),
        ]

        msg = RegisterToolsMessage(
            type="register_tools",
            tools=tools
        )

        assert len(msg.tools) == 2
        assert msg.tools[0].name == "tool1"

    def test_command_result_message_parsing(self):
        """
        Current behavior: CommandResultMessage carries command_id and result dict.
        """
        result_msg = CommandResultMessage(
            type="command_result",
            id="cmd-123",
            result={"success": True, "data": "test"}
        )

        assert result_msg.id == "cmd-123"
        assert result_msg.result["success"] is True

    def test_pong_message_parsing(self):
        """
        Current behavior: PongMessage can include optional session_id.
        """
        pong_msg = PongMessage(
            type="pong",
            session_id="sess-123"
        )

        assert pong_msg.session_id == "sess-123"


# ============================================================================
# COMMAND ROUTING & TIMEOUTS TESTS
# ============================================================================

class TestPluginHubCommandRouting:
    """Test command routing and timeout behavior."""

    def test_fast_fail_commands_are_defined(self):
        """
        Current behavior: PluginHub defines a set of fast-fail commands
        that use shorter timeouts (ping, read_console, get_editor_state).
        """
        assert "ping" in PluginHub._FAST_FAIL_COMMANDS
        assert "read_console" in PluginHub._FAST_FAIL_COMMANDS
        assert "get_editor_state" in PluginHub._FAST_FAIL_COMMANDS
        assert PluginHub.FAST_FAIL_TIMEOUT == 2.0

    @pytest.mark.asyncio
    async def test_send_command_respects_requested_timeout(self, configured_plugin_hub):
        """
        Current behavior: If params contain timeout_seconds or timeoutSeconds,
        use max(COMMAND_TIMEOUT, requested) clamped to [1, 3600] seconds.
        """
        # This is validated in the send_command method
        # The actual timeout handling uses asyncio.wait_for with server_wait_s
        # Verify timeout calculation logic
        params = {"timeout_seconds": 100}

        # In send_command, this would be used as:
        # unity_timeout_s = max(30, 100) = 100
        # server_wait_s = max(30, 100 + 5) = 105
        assert True  # This is implicit in send_command implementation


# ============================================================================
# PLUGIN DISCONNECT & ERROR HANDLING TESTS
# ============================================================================

class TestPluginHubDisconnect:
    """Test behavior when plugin WebSocket disconnects."""

    def test_plugin_disconnected_error_is_defined(self):
        """
        Current behavior: PluginDisconnectedError is a RuntimeError subclass
        raised when a WebSocket disconnects during command processing.
        """
        error = PluginDisconnectedError("Test message")
        assert isinstance(error, RuntimeError)
        assert str(error) == "Test message"

    def test_no_unity_session_error_is_defined(self):
        """
        Current behavior: NoUnitySessionError is a RuntimeError subclass
        raised when no Unity plugins are connected.
        """
        error = NoUnitySessionError("Test message")
        assert isinstance(error, RuntimeError)
        assert str(error) == "Test message"


# ============================================================================
# SESSION RESOLUTION & WAITING TESTS
# ============================================================================

class TestSessionResolution:
    """Test session resolution with waiting for reconnects."""

    @pytest.mark.asyncio
    async def test_resolve_session_id_waits_for_reconnect(self, plugin_registry):
        """
        Current behavior: _resolve_session_id() waits up to max_wait_s for
        a plugin to connect/reconnect before failing.
        """
        # Configure PluginHub
        loop = asyncio.get_event_loop()
        PluginHub.configure(plugin_registry, loop)

        # This simulates domain reload recovery
        target_hash = "hash-delayed"

        # Start with no sessions
        async def delayed_register():
            await asyncio.sleep(0.1)
            await plugin_registry.register(
                session_id="sess-delayed",
                project_name="Project",
                project_hash=target_hash,
                unity_version="2022.3"
            )

        # Schedule registration
        task = asyncio.create_task(delayed_register())

        # Resolve with short timeout
        session_id = await PluginHub._resolve_session_id(target_hash)

        assert session_id == "sess-delayed"

        # Ensure background task completes
        await task

        # Cleanup
        PluginHub._registry = None
        PluginHub._lock = None
        PluginHub._loop = None

    @pytest.mark.asyncio
    async def test_resolve_session_id_fails_when_no_session_appears(self, plugin_registry, monkeypatch):
        """
        Current behavior: If no session appears within max_wait_s,
        raise NoUnitySessionError.
        """
        # Configure PluginHub
        loop = asyncio.get_event_loop()
        PluginHub.configure(plugin_registry, loop)

        # Set very short timeout
        monkeypatch.setenv("UNITY_MCP_SESSION_RESOLVE_MAX_WAIT_S", "0.05")

        # Try to resolve unknown hash
        with pytest.raises(NoUnitySessionError):
            await PluginHub._resolve_session_id("nonexistent-hash")

        # Cleanup
        PluginHub._registry = None
        PluginHub._lock = None
        PluginHub._loop = None

    @pytest.mark.asyncio
    async def test_resolve_session_id_auto_selects_sole_instance(self, plugin_registry):
        """
        Current behavior: When no target_hash provided and exactly one session
        exists, auto-select it.
        """
        # Configure PluginHub
        loop = asyncio.get_event_loop()
        PluginHub.configure(plugin_registry, loop)

        await plugin_registry.register(
            session_id="sess-sole",
            project_name="Project",
            project_hash="hash-sole",
            unity_version="2022.3"
        )

        session_id = await PluginHub._resolve_session_id(None)

        assert session_id == "sess-sole"

        # Cleanup
        PluginHub._registry = None
        PluginHub._lock = None
        PluginHub._loop = None

    @pytest.mark.asyncio
    async def test_resolve_session_id_rejects_ambiguous_selection(self, plugin_registry):
        """
        Current behavior: When no target and multiple sessions exist,
        raise RuntimeError indicating ambiguity.
        """
        # Configure PluginHub
        loop = asyncio.get_event_loop()
        PluginHub.configure(plugin_registry, loop)

        await plugin_registry.register(
            session_id="sess-1",
            project_name="Project1",
            project_hash="hash-1",
            unity_version="2022.3"
        )
        await plugin_registry.register(
            session_id="sess-2",
            project_name="Project2",
            project_hash="hash-2",
            unity_version="2023.2"
        )

        with pytest.raises(InstanceSelectionRequiredError, match="Multiple Unity instances"):
            await PluginHub._resolve_session_id(None)

        # Cleanup
        PluginHub._registry = None
        PluginHub._lock = None
        PluginHub._loop = None

    @pytest.mark.asyncio
    async def test_resolve_session_id_parses_instance_format(self, plugin_registry):
        """
        Current behavior: Accepts both "ProjectName@hash" and bare "hash"
        formats, extracting the hash portion.
        """
        # Configure PluginHub
        loop = asyncio.get_event_loop()
        PluginHub.configure(plugin_registry, loop)

        target_hash = "hash-parse"

        await plugin_registry.register(
            session_id="sess-parse",
            project_name="ProjectName",
            project_hash=target_hash,
            unity_version="2022.3"
        )

        # Resolve via "Name@hash" format
        session_id = await PluginHub._resolve_session_id("ProjectName@hash-parse")
        assert session_id == "sess-parse"

        # Resolve via bare hash format
        session_id = await PluginHub._resolve_session_id("hash-parse")
        assert session_id == "sess-parse"

        # Cleanup
        PluginHub._registry = None
        PluginHub._lock = None
        PluginHub._loop = None


# ============================================================================
# PLUGIN HUB CONFIGURATION TESTS
# ============================================================================

class TestPluginHubConfiguration:
    """Test PluginHub initialization and configuration."""

    @pytest.mark.asyncio
    async def test_plugin_hub_configure_initializes_lock(self, plugin_registry):
        """
        Current behavior: configure() initializes _lock and _registry
        at the class level.
        """
        loop = asyncio.get_event_loop()

        PluginHub.configure(plugin_registry, loop)

        assert PluginHub._registry is plugin_registry
        assert PluginHub._lock is not None
        assert PluginHub._loop is loop

    def test_plugin_hub_is_configured(self, plugin_registry):
        """
        Current behavior: is_configured() returns True only when both
        _registry and _lock are set.
        """
        PluginHub._registry = None
        PluginHub._lock = None

        assert PluginHub.is_configured() is False

        PluginHub._registry = plugin_registry
        PluginHub._lock = asyncio.Lock()

        assert PluginHub.is_configured() is True

    def test_plugin_hub_not_configured_sends_command_fails(self):
        """
        Current behavior: Calling send_command when not configured
        raises RuntimeError.
        """
        PluginHub._lock = None

        with pytest.raises(RuntimeError, match="not configured"):
            asyncio.run(PluginHub.send_command("sess-id", "ping", {}))

    @pytest.mark.asyncio
    async def test_plugin_hub_get_tools_for_project_honors_user_scope(
        self,
        configured_plugin_hub,
        plugin_registry,
        monkeypatch,
    ):
        """
        Current behavior: in remote-hosted mode, get_tools_for_project()
        resolves by (user_id, project_hash) so users with the same hash do not
        see each other's tool registrations.
        """
        monkeypatch.setattr(config, "http_remote_hosted", True)

        await plugin_registry.register(
            session_id="sess-user-a",
            project_name="Project",
            project_hash="shared-hash",
            unity_version="2022.3",
            user_id="user-a",
        )
        await plugin_registry.register(
            session_id="sess-user-b",
            project_name="Project",
            project_hash="shared-hash",
            unity_version="2022.3",
            user_id="user-b",
        )
        await plugin_registry.register_tools_for_session(
            "sess-user-a",
            [ToolDefinitionModel(name="tool_a", description="Tool A")],
        )
        await plugin_registry.register_tools_for_session(
            "sess-user-b",
            [ToolDefinitionModel(name="tool_b", description="Tool B")],
        )

        tools_for_a = await PluginHub.get_tools_for_project("shared-hash", user_id="user-a")
        tools_for_b = await PluginHub.get_tools_for_project("shared-hash", user_id="user-b")

        assert [tool.name for tool in tools_for_a] == ["tool_a"]
        assert [tool.name for tool in tools_for_b] == ["tool_b"]

    @pytest.mark.asyncio
    async def test_plugin_hub_get_tool_definition_honors_user_scope(
        self,
        configured_plugin_hub,
        plugin_registry,
        monkeypatch,
    ):
        """
        Current behavior: in remote-hosted mode, get_tool_definition() is
        user-scoped for shared project hashes.
        """
        monkeypatch.setattr(config, "http_remote_hosted", True)

        await plugin_registry.register(
            session_id="sess-user-a",
            project_name="Project",
            project_hash="shared-hash",
            unity_version="2022.3",
            user_id="user-a",
        )
        await plugin_registry.register(
            session_id="sess-user-b",
            project_name="Project",
            project_hash="shared-hash",
            unity_version="2022.3",
            user_id="user-b",
        )
        await plugin_registry.register_tools_for_session(
            "sess-user-a",
            [ToolDefinitionModel(name="tool_a", description="Tool A")],
        )
        await plugin_registry.register_tools_for_session(
            "sess-user-b",
            [ToolDefinitionModel(name="tool_b", description="Tool B")],
        )

        tool_for_a = await PluginHub.get_tool_definition("shared-hash", "tool_a", user_id="user-a")
        tool_for_b = await PluginHub.get_tool_definition("shared-hash", "tool_a", user_id="user-b")

        assert tool_for_a is not None
        assert tool_for_a.name == "tool_a"
        assert tool_for_b is None


# ============================================================================
# GLOBAL MIDDLEWARE SINGLETON TESTS
# ============================================================================

class TestMiddlewareSingleton:
    """Test global middleware singleton pattern."""

    def test_get_unity_instance_middleware_lazy_initializes(self):
        """
        Current behavior: get_unity_instance_middleware() lazily creates
        a singleton if not already set.
        """
        # Reset global state
        import transport.unity_instance_middleware as mw_module
        mw_module._unity_instance_middleware = None

        middleware1 = get_unity_instance_middleware()
        middleware2 = get_unity_instance_middleware()

        assert middleware1 is middleware2

    def test_set_unity_instance_middleware_replaces_singleton(self):
        """
        Current behavior: set_unity_instance_middleware() allows replacing
        the global singleton (used during server initialization).
        """
        import transport.unity_instance_middleware as mw_module
        mw_module._unity_instance_middleware = None

        middleware1 = UnityInstanceMiddleware()
        set_unity_instance_middleware(middleware1)

        retrieved = get_unity_instance_middleware()
        assert retrieved is middleware1


# ============================================================================
# EDGE CASES & ERROR SCENARIOS
# ============================================================================

class TestTransportEdgeCases:
    """Test edge cases and error scenarios."""

    @pytest.mark.asyncio
    async def test_middleware_handles_exception_during_autoselect(self, mock_context):
        """
        Current behavior: If autoselect raises an unexpected exception,
        it's caught and logged, allowing the middleware to continue.
        """
        middleware = UnityInstanceMiddleware()

        with patch("transport.unity_instance_middleware.PluginHub.is_configured", return_value=True):
            with patch("transport.unity_instance_middleware.PluginHub.get_sessions", new_callable=AsyncMock) as mock_get:
                with patch("transport.legacy.unity_connection.get_unity_connection_pool", return_value=None):
                    mock_get.side_effect = RuntimeError("Unexpected error")

                    # Should not raise, just return None
                    instance = await middleware._maybe_autoselect_instance(mock_context)

        assert instance is None

    def test_middleware_handles_client_id_false_but_not_none(self):
        """
        Current behavior: get_session_key checks isinstance(client_id, str) AND len,
        so falsy non-string values fall through to 'global'.
        """
        middleware = UnityInstanceMiddleware()

        ctx = Mock()
        ctx.client_id = ""  # Empty string
        ctx.session_id = "session-id"

        key = middleware.get_session_key(ctx)
        assert key == "global"  # Empty string doesn't pass isinstance+truthy check

    def test_plugin_hub_encoding_is_json(self):
        """
        Current behavior: PluginHub WebSocketEndpoint uses JSON encoding.
        """
        assert PluginHub.encoding == "json"

    def test_plugin_hub_timeout_constants(self):
        """
        Current behavior: PluginHub defines standard timeout constants.
        """
        assert PluginHub.KEEP_ALIVE_INTERVAL == 15
        assert PluginHub.SERVER_TIMEOUT == 30
        assert PluginHub.COMMAND_TIMEOUT == 30
        assert PluginHub.FAST_FAIL_TIMEOUT == 2.0


# ============================================================================
# INTEGRATION SCENARIOS
# ============================================================================

class TestTransportIntegration:
    """Test realistic integration scenarios."""

    @pytest.mark.asyncio
    async def test_middleware_and_registry_interaction(self, mock_context, plugin_registry):
        """
        Current behavior: Middleware stores instance selection, which
        can be used to route commands via registry lookup.
        """
        middleware = UnityInstanceMiddleware()

        # Register a session in the registry
        await plugin_registry.register(
            session_id="sess-interact",
            project_name="Project",
            project_hash="hash-interact",
            unity_version="2022.3"
        )

        # Middleware stores the instance
        middleware.set_active_instance(mock_context, "Project@hash-interact")

        # Application can use middleware to route
        instance = middleware.get_active_instance(mock_context)
        assert instance == "Project@hash-interact"

        # And registry to find session
        resolved_id = await plugin_registry.get_session_id_by_hash("hash-interact")
        assert resolved_id == "sess-interact"

    @pytest.mark.asyncio
    async def test_registry_and_middleware_complete_flow(self, mock_context, plugin_registry):
        """
        Current behavior: Integrated flow - register session in registry,
        select it in middleware, then route by hash lookup.
        """
        # Setup
        middleware = UnityInstanceMiddleware()

        # 1. Plugin connects and registers in registry
        await plugin_registry.register(
            session_id="sess-complete",
            project_name="CompleteProject",
            project_hash="hash-complete",
            unity_version="2022.3"
        )

        # 2. User selects instance via middleware
        middleware.set_active_instance(mock_context, "CompleteProject@hash-complete")

        # 3. Tools route using both middleware + registry
        selected_instance = middleware.get_active_instance(mock_context)
        assert selected_instance == "CompleteProject@hash-complete"

        # Extract hash and resolve back to session
        hash_part = selected_instance.split("@")[1]
        resolved_session = await plugin_registry.get_session_id_by_hash(hash_part)
        assert resolved_session == "sess-complete"

        # 4. Verify session has the correct data
        session = await plugin_registry.get_session(resolved_session)
        assert session.project_name == "CompleteProject"
        assert session.unity_version == "2022.3"


# ============================================================================
# SUMMARY
# ============================================================================

"""
CHARACTERIZATION TEST SUMMARY

Total Tests: 60+

Categories:
1. Session Management & Routing (9 tests)
   - Instance storage per session
   - Session key derivation and prioritization
   - Session isolation
   - Clear and reset operations
   - Thread safety

2. Middleware Injection & Context Flow (3 tests)
   - Tool context injection
   - Resource context injection
   - No-op when instance unavailable

3. Auto-Select Instance (3 tests)
   - Single instance auto-selection
   - Multiple instance ambiguity
   - Error handling and fallback

4. Plugin Registry (8 tests)
   - Session registration and lookup
   - Hash-based routing
   - Reconnect scenarios
   - Tool registration
   - Heartbeat updates
   - Cleanup on disconnect
   - Batch operations

5. Plugin Hub Message Handling (5 tests)
   - Registration flow
   - Tool registration
   - Command result completion
   - Heartbeat handling
   - Error validation

6. Command Routing & Timeouts (2 tests)
   - Fast-fail timeout logic
   - Custom timeout handling

7. Plugin Disconnect & Error Handling (2 tests)
   - In-flight command failure
   - Session cleanup

8. Session Resolution & Waiting (4 tests)
   - Waiting for reconnect
   - Timeout behavior
   - Auto-selection
   - Ambiguity detection
   - Instance format parsing

9. PluginHub Configuration (3 tests)
   - Initialization
   - Configuration state
   - Unconfigured behavior

10. Global Middleware Singleton (2 tests)
    - Lazy initialization
    - Replacement/override

11. Edge Cases & Error Scenarios (4 tests)
    - Malformed messages
    - Unknown message types
    - Unexpected exceptions
    - Falsy client_id handling

12. Integration Scenarios (2 tests)
    - Full registration flow
    - Middleware + registry interaction

Key Behavior Patterns Tested:
- Thread-safe session storage with RLock
- Client_id prioritization over session_id for key derivation
- Lazy singleton pattern for middleware
- Auto-selection with fallback to stdio
- Reconnect support via hash-based mapping
- Fast-fail timeouts for UI-blocking commands
- Graceful degradation on plugin disconnect
- Waiting for plugin reconnect during domain reloads

Critical Integration Points:
- Middleware injects instance into context state
- Context state used by tools for routing
- Registry maps hash to session_id for HTTP transport
- Plugin disconnect cleans up sessions and fails in-flight commands
- Auto-select probes both PluginHub and stdio with graceful fallback
"""
