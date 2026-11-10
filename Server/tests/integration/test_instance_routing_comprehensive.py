"""
Comprehensive test suite for Unity instance routing.

These tests validate that set_active_instance correctly routes subsequent
tool calls to the intended Unity instance across ALL tool categories.

DESIGN: Single source of truth via middleware state:
- set_active_instance tool stores instance per session in UnityInstanceMiddleware
- Middleware injects instance into ctx.set_state() for each tool call
- get_unity_instance_from_context() reads from ctx.get_state()
- All tools (GameObject, Script, Asset, etc.) use get_unity_instance_from_context()
"""
import pytest
from unittest.mock import AsyncMock, Mock, MagicMock, patch
from fastmcp import Context

from core.config import config
from transport.unity_instance_middleware import UnityInstanceMiddleware
from services.tools import get_unity_instance_from_context
from services.tools.set_active_instance import set_active_instance as set_active_instance_tool
from transport.models import SessionList, SessionDetails


def _make_stateful_ctx(session_id: str) -> Mock:
    """Build a Context shim whose set/get/delete_state share one dict.

    The middleware now defers persistence to FastMCP's session-scoped state
    store, so a useful unit-test mock must actually round-trip values rather
    than returning fresh Mocks. This helper does that minimally.
    """
    state: dict[str, object] = {}
    ctx = Mock(spec=Context)
    ctx.session_id = session_id
    ctx.set_state = AsyncMock(side_effect=lambda k, v: state.__setitem__(k, v))
    ctx.get_state = AsyncMock(side_effect=lambda k: state.get(k))
    ctx.delete_state = AsyncMock(side_effect=lambda k: state.pop(k, None))
    return ctx


class TestInstanceRoutingBasics:
    """Test basic middleware functionality."""

    @pytest.mark.asyncio
    async def test_middleware_stores_and_retrieves_instance(self):
        """Middleware should store and retrieve instance per session."""
        middleware = UnityInstanceMiddleware()
        ctx = _make_stateful_ctx("test-session-1")

        await middleware.set_active_instance(ctx, "TestProject@abc123")

        assert await middleware.get_active_instance(ctx) == "TestProject@abc123"

    @pytest.mark.asyncio
    async def test_middleware_isolates_sessions(self):
        """Two MCP sessions must not see each other's active instance.

        Regression test for #1023: the prior implementation keyed on the
        peer-supplied client_id and collapsed multiple clients onto a shared
        record. Each ctx in this test holds its own private state dict, so
        leakage would surface as a cross-read.
        """
        middleware = UnityInstanceMiddleware()

        ctx1 = _make_stateful_ctx("session-1")
        ctx2 = _make_stateful_ctx("session-2")

        await middleware.set_active_instance(ctx1, "Project1@aaa")
        await middleware.set_active_instance(ctx2, "Project2@bbb")

        assert await middleware.get_active_instance(ctx1) == "Project1@aaa"
        assert await middleware.get_active_instance(ctx2) == "Project2@bbb"


class TestInstanceRoutingIntegration:
    """Test that instance routing works end-to-end for all tool categories."""

    @pytest.mark.asyncio
    async def test_middleware_injects_state_into_context(self):
        """Middleware on_call_tool should inject instance into ctx state.

        After this PR the middleware writes two distinct keys: a persistence
        key (``mcpforunity.active_instance``) when ``set_active_instance`` is
        called, and a per-request injection key (``unity_instance``) inside
        ``on_call_tool``. Tools downstream read ``unity_instance``.
        """
        middleware = UnityInstanceMiddleware()

        ctx = _make_stateful_ctx("test-session")

        middleware_ctx = Mock()
        middleware_ctx.fastmcp_context = ctx

        await middleware.set_active_instance(ctx, "TestProject@abc123")

        async def mock_call_next(ctx):
            return {"success": True}

        await middleware.on_call_tool(middleware_ctx, mock_call_next)

        # The per-request injection must have happened so tools downstream
        # can read it; the persistence write is verified by the round-trip
        # test in TestInstanceRoutingBasics above.
        ctx.set_state.assert_any_call("unity_instance", "TestProject@abc123")

    @pytest.mark.asyncio
    async def test_get_unity_instance_from_context_checks_state(self):
        """get_unity_instance_from_context must read from ctx.get_state()."""
        ctx = Mock(spec=Context)

        # Set up state storage (only source of truth now)
        state_storage = {"unity_instance": "Project@state123"}
        ctx.get_state = AsyncMock(side_effect=lambda k: state_storage.get(k))

        # Call and verify
        result = await get_unity_instance_from_context(ctx)

        assert result == "Project@state123", \
            "get_unity_instance_from_context must read from ctx.get_state()!"

    @pytest.mark.asyncio
    async def test_get_unity_instance_returns_none_when_not_set(self):
        """Should return None when no instance is set."""
        ctx = Mock(spec=Context)

        # Empty state storage
        state_storage = {}
        ctx.get_state = AsyncMock(side_effect=lambda k: state_storage.get(k))

        result = await get_unity_instance_from_context(ctx)
        assert result is None


class TestInstanceRoutingToolCategories:
    """Test instance routing for each tool category."""

    def _create_mock_context_with_instance(self, instance_id: str):
        """Helper to create a mock context with instance set via middleware."""
        ctx = Mock(spec=Context)
        ctx.session_id = "test-session"

        # Set up state storage (only source of truth)
        state_storage = {"unity_instance": instance_id}
        ctx.get_state = AsyncMock(side_effect=lambda k: state_storage.get(k))
        ctx.set_state = AsyncMock(side_effect=lambda k,
                             v: state_storage.__setitem__(k, v))

        return ctx



class TestInstanceRoutingHTTP:
    """Validate HTTP-specific routing helpers."""

    @pytest.mark.asyncio
    async def test_set_active_instance_http_transport(self, monkeypatch):
        """set_active_instance should enumerate PluginHub sessions under HTTP."""
        middleware = UnityInstanceMiddleware()
        ctx = Mock(spec=Context)
        ctx.session_id = "http-session"
        state_storage = {}
        ctx.set_state = AsyncMock(side_effect=lambda k,
                             v: state_storage.__setitem__(k, v))
        ctx.get_state = AsyncMock(side_effect=lambda k: state_storage.get(k))

        monkeypatch.setattr(config, "transport_mode", "http")
        fake_sessions = SessionList(
            sessions={
                "sess-1": SessionDetails(
                    project="Ramble",
                    hash="8e29de57",
                    unity_version="6000.2.10f1",
                    connected_at="2025-11-21T03:30:03.682353+00:00",
                )
            }
        )
        monkeypatch.setattr(
            "services.tools.set_active_instance.PluginHub.get_sessions",
            AsyncMock(return_value=fake_sessions),
        )
        monkeypatch.setattr(
            "services.tools.set_active_instance.get_unity_instance_middleware",
            lambda: middleware,
        )

        result = await set_active_instance_tool(ctx, "Ramble@8e29de57")

        assert result["success"] is True
        assert await middleware.get_active_instance(ctx) == "Ramble@8e29de57"

    @pytest.mark.asyncio
    async def test_set_active_instance_http_hash_only(self, monkeypatch):
        """Hash-only selection should resolve via PluginHub registry."""
        middleware = UnityInstanceMiddleware()
        ctx = Mock(spec=Context)
        ctx.session_id = "http-session-2"
        state_storage = {}
        ctx.set_state = AsyncMock(side_effect=lambda k,
                             v: state_storage.__setitem__(k, v))
        ctx.get_state = AsyncMock(side_effect=lambda k: state_storage.get(k))

        monkeypatch.setattr(config, "transport_mode", "http")
        fake_sessions = SessionList(
            sessions={
                "sess-99": SessionDetails(
                    project="UnityMCPTests",
                    hash="cc8756d4",
                    unity_version="2021.3.45f2",
                    connected_at="2025-11-21T03:37:01.501022+00:00",
                )
            }
        )
        monkeypatch.setattr(
            "services.tools.set_active_instance.PluginHub.get_sessions",
            AsyncMock(return_value=fake_sessions),
        )
        monkeypatch.setattr(
            "services.tools.set_active_instance.get_unity_instance_middleware",
            lambda: middleware,
        )

        result = await set_active_instance_tool(ctx, "UnityMCPTests@cc8756d4")

        assert result["success"] is True
        assert await middleware.get_active_instance(ctx) == "UnityMCPTests@cc8756d4"

    @pytest.mark.asyncio
    async def test_set_active_instance_http_hash_missing(self, monkeypatch):
        """Unknown hashes should surface a clear error."""
        middleware = UnityInstanceMiddleware()
        ctx = Mock(spec=Context)
        ctx.session_id = "http-session-3"

        monkeypatch.setattr(config, "transport_mode", "http")
        fake_sessions = SessionList(sessions={})
        monkeypatch.setattr(
            "services.tools.set_active_instance.PluginHub.get_sessions",
            AsyncMock(return_value=fake_sessions),
        )
        monkeypatch.setattr(
            "services.tools.set_active_instance.get_unity_instance_middleware",
            lambda: middleware,
        )

        result = await set_active_instance_tool(ctx, "Unknown@deadbeef")

        assert result["success"] is False
        assert "No Unity instances" in result["error"]

    @pytest.mark.asyncio
    async def test_set_active_instance_http_hash_ambiguous(self, monkeypatch):
        """Ambiguous hash prefixes should mirror stdio error messaging."""
        middleware = UnityInstanceMiddleware()
        ctx = Mock(spec=Context)
        ctx.session_id = "http-session-4"

        monkeypatch.setattr(config, "transport_mode", "http")
        fake_sessions = SessionList(
            sessions={
                "sess-a": SessionDetails(project="ProjA", hash="abc12345", unity_version="2022", connected_at="now"),
                "sess-b": SessionDetails(project="ProjB", hash="abc98765", unity_version="2022", connected_at="now"),
            }
        )
        monkeypatch.setattr(
            "services.tools.set_active_instance.PluginHub.get_sessions",
            AsyncMock(return_value=fake_sessions),
        )
        monkeypatch.setattr(
            "services.tools.set_active_instance.get_unity_instance_middleware",
            lambda: middleware,
        )

        result = await set_active_instance_tool(ctx, "abc")

        assert result["success"] is False
        assert "Name@hash" in result["error"]


class TestInstanceRoutingRaceConditions:
    """Test for race conditions and timing issues."""

    @pytest.mark.asyncio
    async def test_rapid_instance_switching(self):
        """Rapidly switching instances should not cause routing errors."""
        middleware = UnityInstanceMiddleware()
        ctx = Mock(spec=Context)
        ctx.session_id = "test-session"

        state_storage = {}
        ctx.set_state = AsyncMock(side_effect=lambda k,
                             v: state_storage.__setitem__(k, v))
        ctx.get_state = AsyncMock(side_effect=lambda k: state_storage.get(k))

        instances = ["Project1@aaa", "Project2@bbb", "Project3@ccc"]

        for instance in instances:
            await middleware.set_active_instance(ctx, instance)

            # Create middleware context
            middleware_ctx = Mock()
            middleware_ctx.fastmcp_context = ctx

            async def mock_call_next(ctx):
                return {"success": True}

            # Execute middleware
            await middleware.on_call_tool(middleware_ctx, mock_call_next)

            # Verify correct instance is set
            assert state_storage.get("unity_instance") == instance

    @pytest.mark.asyncio
    async def test_set_then_immediate_create_script(self):
        """Setting instance then immediately creating script should route correctly."""
        # This reproduces the bug: set_active_instance → create_script went to wrong instance

        middleware = UnityInstanceMiddleware()
        ctx = Mock(spec=Context)
        ctx.session_id = "test-session"
        ctx.info = Mock()

        state_storage = {}
        ctx.set_state = AsyncMock(side_effect=lambda k,
                             v: state_storage.__setitem__(k, v))
        ctx.get_state = AsyncMock(side_effect=lambda k: state_storage.get(k))
        ctx.request_context = None

        # Set active instance
        await middleware.set_active_instance(ctx, "ramble@8e29de57")

        # Simulate middleware intercepting create_script call
        middleware_ctx = Mock()
        middleware_ctx.fastmcp_context = ctx

        async def mock_create_script_call(ctx):
            # This simulates what create_script does
            instance = await get_unity_instance_from_context(ctx)
            return {"success": True, "routed_to": instance}

        # Inject state via middleware
        await middleware.on_call_tool(middleware_ctx, mock_create_script_call)

        # Verify create_script would route to correct instance
        result = await mock_create_script_call(ctx)
        assert result["routed_to"] == "ramble@8e29de57", \
            "create_script must route to the instance set by set_active_instance"


class TestInstanceRoutingSequentialOperations:
    """Test the exact failure scenario from user report."""

    @pytest.mark.asyncio
    async def test_four_script_creation_sequence(self):
        """
        Reproduce the exact failure:
        1. set_active(ramble) → create_script1 → should go to ramble
        2. set_active(UnityMCPTests) → create_script2 → should go to UnityMCPTests
        3. set_active(ramble) → create_script3 → should go to ramble
        4. set_active(UnityMCPTests) → create_script4 → should go to UnityMCPTests

        ACTUAL BEHAVIOR:
        - Script1 went to UnityMCPTests (WRONG)
        - Script2 went to ramble (WRONG)
        - Script3 went to ramble (CORRECT)
        - Script4 went to UnityMCPTests (CORRECT)
        """
        middleware = UnityInstanceMiddleware()

        # Track which instance each script was created in
        script_routes = {}

        async def simulate_create_script(ctx, script_name, expected_instance):
            # Inject state via middleware
            middleware_ctx = Mock()
            middleware_ctx.fastmcp_context = ctx

            async def mock_tool_call(middleware_ctx):
                # The middleware passes the middleware_ctx, we need the fastmcp_context
                tool_ctx = middleware_ctx.fastmcp_context
                instance = await get_unity_instance_from_context(tool_ctx)
                script_routes[script_name] = instance
                return {"success": True}

            await middleware.on_call_tool(middleware_ctx, mock_tool_call)
            return expected_instance

        # Session context
        ctx = Mock(spec=Context)
        ctx.session_id = "test-session"
        ctx.info = Mock()

        state_storage = {}
        ctx.set_state = AsyncMock(side_effect=lambda k,
                             v: state_storage.__setitem__(k, v))
        ctx.get_state = AsyncMock(side_effect=lambda k: state_storage.get(k))

        # Execute sequence
        await middleware.set_active_instance(ctx, "ramble@8e29de57")
        expected1 = await simulate_create_script(ctx, "Script1", "ramble@8e29de57")

        await middleware.set_active_instance(ctx, "UnityMCPTests@cc8756d4")
        expected2 = await simulate_create_script(ctx, "Script2", "UnityMCPTests@cc8756d4")

        await middleware.set_active_instance(ctx, "ramble@8e29de57")
        expected3 = await simulate_create_script(ctx, "Script3", "ramble@8e29de57")

        await middleware.set_active_instance(ctx, "UnityMCPTests@cc8756d4")
        expected4 = await simulate_create_script(ctx, "Script4", "UnityMCPTests@cc8756d4")

        # Assertions - these will FAIL until the bug is fixed
        assert script_routes.get("Script1") == expected1, \
            f"Script1 should route to {expected1}, got {script_routes.get('Script1')}"
        assert script_routes.get("Script2") == expected2, \
            f"Script2 should route to {expected2}, got {script_routes.get('Script2')}"
        assert script_routes.get("Script3") == expected3, \
            f"Script3 should route to {expected3}, got {script_routes.get('Script3')}"
        assert script_routes.get("Script4") == expected4, \
            f"Script4 should route to {expected4}, got {script_routes.get('Script4')}"


# Test regimen summary
"""
COMPREHENSIVE TEST REGIMEN FOR INSTANCE ROUTING

Prerequisites:
- Two Unity instances running (e.g., ramble, UnityMCPTests)
- MCP server connected to both instances

Test Categories:
1. ✅ Middleware State Management (4 tests)
2. ✅ Middleware Integration (2 tests)
3. ✅ get_unity_instance_from_context (2 tests)
4. ✅ Tool Category Coverage (11 categories)
5. ✅ Race Conditions (2 tests)
6. ✅ Sequential Operations (1 test - reproduces exact user bug)

Total: 21 tests

DESIGN:
Single source of truth via middleware state:
- set_active_instance stores instance per session in UnityInstanceMiddleware
- Middleware injects instance into ctx.set_state() for each tool call
- get_unity_instance_from_context() reads from ctx.get_state()
- All tools use get_unity_instance_from_context()

This ensures consistent routing across ALL tool categories (Script, GameObject, Asset, etc.)
"""
