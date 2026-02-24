"""
Tests for per-call unity_instance routing via middleware argument interception.

When a tool call includes unity_instance in its arguments, the middleware:
  1. Pops the key before Pydantic validation sees it
  2. Resolves it to a validated instance identifier
  3. Sets it in request-scoped state for that call only (does NOT persist to session)
"""
import asyncio
import sys
import types
from types import SimpleNamespace

import pytest

from .test_helpers import DummyContext
from core.config import config


# ---------------------------------------------------------------------------
# Helpers
# ---------------------------------------------------------------------------

class DummyMiddlewareContext:
    """Minimal MiddlewareContext stand-in with a mutable arguments dict."""

    def __init__(self, ctx, arguments: dict | None = None):
        self.fastmcp_context = ctx
        self.message = SimpleNamespace(arguments=arguments if arguments is not None else {})


def _make_middleware(monkeypatch, *, transport="stdio", plugin_hub_configured=False, sessions=None, pool_instances=None):
    """
    Build a UnityInstanceMiddleware with patched transport dependencies.

    sessions: dict of session_id -> SimpleNamespace(project=..., hash=...)
    pool_instances: list of SimpleNamespace(id=..., hash=...)
    """
    plugin_hub_mod = types.ModuleType("transport.plugin_hub")

    _sessions = sessions or {}
    _configured = plugin_hub_configured

    class FakePluginHub:
        @classmethod
        def is_configured(cls):
            return _configured

        @classmethod
        async def get_sessions(cls, user_id=None):
            return SimpleNamespace(sessions=_sessions)

        @classmethod
        async def _resolve_session_id(cls, instance, user_id=None):
            return None

    plugin_hub_mod.PluginHub = FakePluginHub
    monkeypatch.setitem(sys.modules, "transport.plugin_hub", plugin_hub_mod)
    monkeypatch.delitem(sys.modules, "transport.unity_instance_middleware", raising=False)

    from transport.unity_instance_middleware import UnityInstanceMiddleware

    middleware = UnityInstanceMiddleware()
    monkeypatch.setattr(config, "transport_mode", transport)
    monkeypatch.setattr(config, "http_remote_hosted", False)

    if pool_instances is not None:
        async def fake_discover(ctx):
            return pool_instances
        monkeypatch.setattr(middleware, "_discover_instances", fake_discover)

    return middleware


# ---------------------------------------------------------------------------
# Pop behaviour
# ---------------------------------------------------------------------------

def test_unity_instance_is_popped_from_arguments(monkeypatch):
    """unity_instance key must be removed from arguments before the tool function sees them."""
    instances = [SimpleNamespace(id="Proj@abc123", hash="abc123")]
    mw = _make_middleware(monkeypatch, pool_instances=instances)

    ctx = DummyContext()
    ctx.client_id = "client-1"
    args = {"action": "get_active", "unity_instance": "abc123"}
    mw_ctx = DummyMiddlewareContext(ctx, arguments=args)

    asyncio.run(mw._inject_unity_instance(mw_ctx))

    assert "unity_instance" not in args
    assert "action" in args  # other keys untouched


def test_arguments_without_unity_instance_untouched(monkeypatch):
    """When unity_instance is absent, arguments dict is left completely untouched."""
    mw = _make_middleware(monkeypatch, pool_instances=[SimpleNamespace(id="Proj@abc123", hash="abc123")])

    ctx = DummyContext()
    ctx.client_id = "client-1"
    # Seed a persisted instance so auto-select isn't needed
    mw.set_active_instance(ctx, "Proj@abc123")

    args = {"action": "get_active", "name": "Test"}
    mw_ctx = DummyMiddlewareContext(ctx, arguments=args)

    asyncio.run(mw._inject_unity_instance(mw_ctx))

    assert args == {"action": "get_active", "name": "Test"}


# ---------------------------------------------------------------------------
# Per-call routing (no persistence)
# ---------------------------------------------------------------------------

def test_inline_routes_to_specified_instance(monkeypatch):
    """Per-call unity_instance sets request state to the resolved instance."""
    instances = [SimpleNamespace(id="Proj@abc123", hash="abc123")]
    mw = _make_middleware(monkeypatch, pool_instances=instances)

    ctx = DummyContext()
    ctx.client_id = "client-1"
    mw_ctx = DummyMiddlewareContext(ctx, arguments={"unity_instance": "abc123"})

    asyncio.run(mw._inject_unity_instance(mw_ctx))

    assert ctx.get_state("unity_instance") == "Proj@abc123"


def test_inline_does_not_persist_to_session(monkeypatch):
    """Per-call unity_instance must not change the session-persisted instance."""
    instances = [
        SimpleNamespace(id="ProjA@aaa111", hash="aaa111"),
        SimpleNamespace(id="ProjB@bbb222", hash="bbb222"),
    ]
    mw = _make_middleware(monkeypatch, pool_instances=instances)

    ctx = DummyContext()
    ctx.client_id = "client-1"
    mw.set_active_instance(ctx, "ProjA@aaa111")

    # Call 1: inline override to ProjB
    mw_ctx1 = DummyMiddlewareContext(ctx, arguments={"unity_instance": "bbb222"})
    asyncio.run(mw._inject_unity_instance(mw_ctx1))
    assert ctx.get_state("unity_instance") == "ProjB@bbb222"

    # Call 2: no inline — must revert to session-persisted ProjA
    mw_ctx2 = DummyMiddlewareContext(ctx, arguments={})
    asyncio.run(mw._inject_unity_instance(mw_ctx2))
    assert ctx.get_state("unity_instance") == "ProjA@aaa111"


def test_inline_overrides_session_persisted_instance(monkeypatch):
    """Inline unity_instance takes precedence over session-persisted instance."""
    instances = [
        SimpleNamespace(id="ProjA@aaa111", hash="aaa111"),
        SimpleNamespace(id="ProjB@bbb222", hash="bbb222"),
    ]
    mw = _make_middleware(monkeypatch, pool_instances=instances)

    ctx = DummyContext()
    ctx.client_id = "client-1"
    mw.set_active_instance(ctx, "ProjA@aaa111")

    mw_ctx = DummyMiddlewareContext(ctx, arguments={"unity_instance": "ProjB@bbb222"})
    asyncio.run(mw._inject_unity_instance(mw_ctx))

    assert ctx.get_state("unity_instance") == "ProjB@bbb222"
    # Session still pinned to ProjA
    assert mw.get_active_instance(ctx) == "ProjA@aaa111"


# ---------------------------------------------------------------------------
# Port number resolution (stdio)
# ---------------------------------------------------------------------------

def test_port_number_resolves_to_name_hash_stdio(monkeypatch):
    """Bare port number resolves to the matching Name@hash in stdio mode."""
    instances = [
        SimpleNamespace(id="Proj@abc123", hash="abc123", port=6401),
        SimpleNamespace(id="Other@def456", hash="def456", port=6402),
    ]
    mw = _make_middleware(monkeypatch, transport="stdio", pool_instances=instances)

    ctx = DummyContext()
    ctx.client_id = "client-1"
    mw_ctx = DummyMiddlewareContext(ctx, arguments={"unity_instance": "6401"})

    asyncio.run(mw._inject_unity_instance(mw_ctx))

    assert ctx.get_state("unity_instance") == "Proj@abc123"


def test_port_number_not_found_raises(monkeypatch):
    """Port number with no matching instance raises ValueError."""
    instances = [SimpleNamespace(id="Proj@abc123", hash="abc123", port=6401)]
    mw = _make_middleware(monkeypatch, transport="stdio", pool_instances=instances)

    ctx = DummyContext()
    ctx.client_id = "client-1"
    mw_ctx = DummyMiddlewareContext(ctx, arguments={"unity_instance": "9999"})

    with pytest.raises(ValueError, match="No Unity instance found on port 9999"):
        asyncio.run(mw._inject_unity_instance(mw_ctx))


def test_port_number_errors_in_http_mode(monkeypatch):
    """Bare port number raises ValueError in HTTP transport mode."""
    mw = _make_middleware(monkeypatch, transport="http")

    ctx = DummyContext()
    ctx.client_id = "client-1"
    mw_ctx = DummyMiddlewareContext(ctx, arguments={"unity_instance": "6401"})

    with pytest.raises(ValueError, match="not supported in HTTP transport mode"):
        asyncio.run(mw._inject_unity_instance(mw_ctx))


# ---------------------------------------------------------------------------
# Name@hash and hash prefix resolution
# ---------------------------------------------------------------------------

def test_name_at_hash_resolves_exactly(monkeypatch):
    """Full Name@hash resolves directly without discovery."""
    instances = [SimpleNamespace(id="Proj@abc123", hash="abc123")]
    mw = _make_middleware(monkeypatch, pool_instances=instances)

    ctx = DummyContext()
    ctx.client_id = "client-1"
    mw_ctx = DummyMiddlewareContext(ctx, arguments={"unity_instance": "Proj@abc123"})

    asyncio.run(mw._inject_unity_instance(mw_ctx))

    assert ctx.get_state("unity_instance") == "Proj@abc123"


def test_unknown_name_at_hash_raises(monkeypatch):
    """Unknown Name@hash raises ValueError."""
    instances = [SimpleNamespace(id="Proj@abc123", hash="abc123")]
    mw = _make_middleware(monkeypatch, pool_instances=instances)

    ctx = DummyContext()
    ctx.client_id = "client-1"
    mw_ctx = DummyMiddlewareContext(ctx, arguments={"unity_instance": "Ghost@deadbeef"})

    with pytest.raises(ValueError, match="not found"):
        asyncio.run(mw._inject_unity_instance(mw_ctx))


def test_hash_prefix_resolves_unique(monkeypatch):
    """Unique hash prefix resolves to the full Name@hash."""
    instances = [SimpleNamespace(id="Proj@abc123", hash="abc123")]
    mw = _make_middleware(monkeypatch, pool_instances=instances)

    ctx = DummyContext()
    ctx.client_id = "client-1"
    mw_ctx = DummyMiddlewareContext(ctx, arguments={"unity_instance": "abc"})

    asyncio.run(mw._inject_unity_instance(mw_ctx))

    assert ctx.get_state("unity_instance") == "Proj@abc123"


def test_ambiguous_hash_prefix_raises(monkeypatch):
    """Ambiguous hash prefix raises ValueError."""
    instances = [
        SimpleNamespace(id="ProjA@abc111", hash="abc111"),
        SimpleNamespace(id="ProjB@abc222", hash="abc222"),
    ]
    mw = _make_middleware(monkeypatch, pool_instances=instances)

    ctx = DummyContext()
    ctx.client_id = "client-1"
    mw_ctx = DummyMiddlewareContext(ctx, arguments={"unity_instance": "abc"})

    with pytest.raises(ValueError, match="ambiguous"):
        asyncio.run(mw._inject_unity_instance(mw_ctx))


def test_no_match_raises(monkeypatch):
    """Hash prefix matching nothing raises ValueError."""
    instances = [SimpleNamespace(id="Proj@abc123", hash="abc123")]
    mw = _make_middleware(monkeypatch, pool_instances=instances)

    ctx = DummyContext()
    ctx.client_id = "client-1"
    mw_ctx = DummyMiddlewareContext(ctx, arguments={"unity_instance": "xyz"})

    with pytest.raises(ValueError, match="No running Unity instance"):
        asyncio.run(mw._inject_unity_instance(mw_ctx))


# ---------------------------------------------------------------------------
# Edge cases
# ---------------------------------------------------------------------------

def test_none_unity_instance_falls_through_to_session(monkeypatch):
    """None value for unity_instance falls through to session-persisted instance."""
    mw = _make_middleware(monkeypatch)
    ctx = DummyContext()
    ctx.client_id = "client-1"
    mw.set_active_instance(ctx, "Proj@abc123")

    mw_ctx = DummyMiddlewareContext(ctx, arguments={"unity_instance": None, "action": "x"})

    asyncio.run(mw._inject_unity_instance(mw_ctx))

    assert ctx.get_state("unity_instance") == "Proj@abc123"


def test_empty_string_unity_instance_falls_through_to_session(monkeypatch):
    """Empty string unity_instance falls through to session-persisted instance."""
    mw = _make_middleware(monkeypatch)
    ctx = DummyContext()
    ctx.client_id = "client-1"
    mw.set_active_instance(ctx, "Proj@abc123")

    mw_ctx = DummyMiddlewareContext(ctx, arguments={"unity_instance": "  "})

    asyncio.run(mw._inject_unity_instance(mw_ctx))

    assert ctx.get_state("unity_instance") == "Proj@abc123"


def test_resource_read_unaffected(monkeypatch):
    """on_read_resource with no .arguments attribute routes via session state normally."""
    mw = _make_middleware(monkeypatch)
    ctx = DummyContext()
    ctx.client_id = "client-1"
    mw.set_active_instance(ctx, "Proj@abc123")

    # ReadResourceRequestParams has .uri not .arguments
    resource_ctx = SimpleNamespace(
        fastmcp_context=ctx,
        message=SimpleNamespace(uri="mcpforunity://scene/active"),
    )

    asyncio.run(mw._inject_unity_instance(resource_ctx))

    assert ctx.get_state("unity_instance") == "Proj@abc123"


# ---------------------------------------------------------------------------
# set_active_instance tool: port number support
# ---------------------------------------------------------------------------

def test_set_active_instance_port_stdio(monkeypatch):
    """set_active_instance accepts a port number in stdio mode and resolves to Name@hash."""
    monkeypatch.setattr(config, "transport_mode", "stdio")
    monkeypatch.setattr(config, "http_remote_hosted", False)

    from transport.unity_instance_middleware import UnityInstanceMiddleware, set_unity_instance_middleware
    mw = UnityInstanceMiddleware()
    set_unity_instance_middleware(mw)

    pool_instance = SimpleNamespace(id="Proj@abc123", hash="abc123", port=6401)

    class FakePool:
        def discover_all_instances(self, force_refresh=False):
            return [pool_instance]

    import services.tools.set_active_instance as sat
    monkeypatch.setattr(sat, "get_unity_connection_pool", lambda: FakePool())

    from services.tools.set_active_instance import set_active_instance

    ctx = DummyContext()
    ctx.client_id = "client-1"

    result = asyncio.run(set_active_instance(ctx, instance="6401"))

    assert result["success"] is True
    assert result["data"]["instance"] == "Proj@abc123"
    assert mw.get_active_instance(ctx) == "Proj@abc123"


def test_set_active_instance_port_http_errors(monkeypatch):
    """set_active_instance rejects port numbers in HTTP mode."""
    monkeypatch.setattr(config, "transport_mode", "http")
    monkeypatch.setattr(config, "http_remote_hosted", False)

    from services.tools.set_active_instance import set_active_instance

    ctx = DummyContext()
    ctx.client_id = "client-1"

    result = asyncio.run(set_active_instance(ctx, instance="6401"))

    assert result["success"] is False
    assert "not supported in HTTP transport mode" in result["error"]


# ---------------------------------------------------------------------------
# batch_execute rejects inner unity_instance
# ---------------------------------------------------------------------------

def test_batch_execute_rejects_inner_unity_instance():
    """batch_execute raises ValueError when an inner command contains unity_instance."""
    from services.tools.batch_execute import batch_execute

    ctx = DummyContext()
    ctx.client_id = "client-1"
    ctx._state["unity_instance"] = "Proj@abc123"

    commands = [
        {"tool": "manage_scene", "params": {"action": "get_active", "unity_instance": "6402"}},
    ]

    with pytest.raises(ValueError, match="Per-command instance routing is not supported inside batch_execute"):
        asyncio.run(batch_execute(ctx, commands=commands))
