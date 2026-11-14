import pytest
import sys
import types
from types import SimpleNamespace

from .test_helpers import DummyContext
from core.config import config
# The middleware imports transport.unity_transport lazily; load it now, against the real
# plugin_hub, so that import never resolves against a test's stub (which lacks
# InstanceSelectionRequiredError) when a test here runs on its own.
import transport.unity_transport  # noqa: F401


class DummyMiddlewareContext:
    def __init__(self, ctx):
        self.fastmcp_context = ctx


@pytest.mark.asyncio
async def test_auto_selects_single_instance_via_pluginhub(monkeypatch):
    plugin_hub = types.ModuleType("transport.plugin_hub")

    class PluginHub:
        @classmethod
        def is_configured(cls) -> bool:
            return True

        @classmethod
        async def get_sessions(cls):
            raise AssertionError("get_sessions should be stubbed in test")

        @classmethod
        def recently_disconnected_instances(cls):
            return []

    plugin_hub.PluginHub = PluginHub
    monkeypatch.setitem(sys.modules, "transport.plugin_hub", plugin_hub)
    monkeypatch.delitem(sys.modules, "transport.unity_instance_middleware", raising=False)

    from transport.unity_instance_middleware import UnityInstanceMiddleware, PluginHub as ImportedPluginHub
    assert ImportedPluginHub is plugin_hub.PluginHub

    monkeypatch.setattr(config, "transport_mode", "http")

    middleware = UnityInstanceMiddleware()
    ctx = DummyContext()
    ctx.client_id = "client-1"
    middleware_context = DummyMiddlewareContext(ctx)

    call_count = {"sessions": 0}

    async def fake_get_sessions():
        call_count["sessions"] += 1
        return SimpleNamespace(
            sessions={
                "session-1": SimpleNamespace(project="Ramble", hash="deadbeef"),
            }
        )

    monkeypatch.setattr(plugin_hub.PluginHub, "get_sessions", fake_get_sessions)

    selected = await middleware._maybe_autoselect_instance(ctx)

    assert selected == "Ramble@deadbeef"
    # Auto-selection is per-call only; it must not stick as the session pin.
    assert await middleware.get_active_instance(ctx) is None
    assert call_count["sessions"] == 1

    await middleware._inject_unity_instance(middleware_context)

    assert await ctx.get_state("unity_instance") == "Ramble@deadbeef"
    # Injection re-discovered rather than reading a persisted pin.
    assert call_count["sessions"] == 2


@pytest.mark.asyncio
async def test_auto_selects_single_instance_via_stdio(monkeypatch):
    plugin_hub = types.ModuleType("transport.plugin_hub")

    class PluginHub:
        @classmethod
        def is_configured(cls) -> bool:
            return False

    plugin_hub.PluginHub = PluginHub
    monkeypatch.setitem(sys.modules, "transport.plugin_hub", plugin_hub)
    monkeypatch.delitem(sys.modules, "transport.unity_instance_middleware", raising=False)

    from transport.unity_instance_middleware import UnityInstanceMiddleware, PluginHub as ImportedPluginHub
    assert ImportedPluginHub is plugin_hub.PluginHub

    monkeypatch.setattr(config, "transport_mode", "stdio")

    middleware = UnityInstanceMiddleware()
    ctx = DummyContext()
    ctx.client_id = "client-1"
    middleware_context = DummyMiddlewareContext(ctx)

    class PoolStub:
        def discover_all_instances(self, force_refresh=False):
            assert force_refresh is True
            return [SimpleNamespace(id="UnityMCPTests@cc8756d4")]

    unity_connection = types.ModuleType("transport.legacy.unity_connection")
    unity_connection.get_unity_connection_pool = lambda: PoolStub()
    monkeypatch.setitem(sys.modules, "transport.legacy.unity_connection", unity_connection)

    selected = await middleware._maybe_autoselect_instance(ctx)

    assert selected == "UnityMCPTests@cc8756d4"
    # Auto-selection is per-call only; it must not stick as the session pin.
    assert await middleware.get_active_instance(ctx) is None

    await middleware._inject_unity_instance(middleware_context)

    assert await ctx.get_state("unity_instance") == "UnityMCPTests@cc8756d4"


@pytest.mark.asyncio
async def test_no_autoselect_while_another_bridge_recently_disconnected(monkeypatch):
    plugin_hub = types.ModuleType("transport.plugin_hub")

    class PluginHub:
        @classmethod
        def is_configured(cls) -> bool:
            return True

        @classmethod
        async def get_sessions(cls):
            return SimpleNamespace(
                sessions={
                    "session-1": SimpleNamespace(project="Playground", hash="aaaa1111"),
                }
            )

        @classmethod
        def recently_disconnected_instances(cls):
            return ["Sandbox@bbbb2222"]

    plugin_hub.PluginHub = PluginHub
    monkeypatch.setitem(sys.modules, "transport.plugin_hub", plugin_hub)
    monkeypatch.delitem(sys.modules, "transport.unity_instance_middleware", raising=False)

    from transport.unity_instance_middleware import UnityInstanceMiddleware, PluginHub as ImportedPluginHub
    assert ImportedPluginHub is plugin_hub.PluginHub

    monkeypatch.setattr(config, "transport_mode", "http")

    middleware = UnityInstanceMiddleware()
    ctx = DummyContext()
    ctx.client_id = "client-1"

    selected = await middleware._maybe_autoselect_instance(ctx)

    # The sole survivor may be the wrong editor while another bridge restarts.
    assert selected is None
    assert await middleware.get_active_instance(ctx) is None


@pytest.mark.asyncio
async def test_declined_autoselect_clears_the_previous_calls_instance(monkeypatch):
    plugin_hub = types.ModuleType("transport.plugin_hub")

    class PluginHub:
        ghosts: list[str] = []

        @classmethod
        def is_configured(cls) -> bool:
            return True

        @classmethod
        async def get_sessions(cls):
            return SimpleNamespace(
                sessions={
                    "session-1": SimpleNamespace(project="Playground", hash="aaaa1111"),
                }
            )

        @classmethod
        def recently_disconnected_instances(cls):
            return list(cls.ghosts)

        @classmethod
        async def _resolve_session_id(cls, unity_instance, user_id=None):
            return "session-1"

    plugin_hub.PluginHub = PluginHub
    monkeypatch.setitem(sys.modules, "transport.plugin_hub", plugin_hub)
    monkeypatch.delitem(sys.modules, "transport.unity_instance_middleware", raising=False)

    from transport.unity_instance_middleware import UnityInstanceMiddleware

    monkeypatch.setattr(config, "transport_mode", "http")

    middleware = UnityInstanceMiddleware()
    ctx = DummyContext()
    ctx.client_id = "client-1"
    middleware_context = DummyMiddlewareContext(ctx)

    # First call: the sole instance is auto-selected for this call.
    await middleware._inject_unity_instance(middleware_context)
    assert await ctx.get_state("unity_instance") == "Playground@aaaa1111"

    # Next call on the same session: another bridge just dropped, so auto-selection declines.
    # The state store is session-scoped; the first call's instance must not route this one.
    PluginHub.ghosts = ["Sandbox@bbbb2222"]
    await middleware._inject_unity_instance(middleware_context)
    assert await ctx.get_state("unity_instance") is None


@pytest.mark.asyncio
async def test_auto_select_handles_stdio_errors(monkeypatch):
    plugin_hub = types.ModuleType("transport.plugin_hub")

    class PluginHub:
        @classmethod
        def is_configured(cls) -> bool:
            return False

    plugin_hub.PluginHub = PluginHub
    monkeypatch.setitem(sys.modules, "transport.plugin_hub", plugin_hub)
    monkeypatch.delitem(sys.modules, "transport.unity_instance_middleware", raising=False)

    from transport.unity_instance_middleware import UnityInstanceMiddleware, PluginHub as ImportedPluginHub
    assert ImportedPluginHub is plugin_hub.PluginHub

    middleware = UnityInstanceMiddleware()
    ctx = DummyContext()
    ctx.client_id = "client-1"

    class PoolStub:
        def discover_all_instances(self, force_refresh=False):
            raise ConnectionError("stdio unavailable")

    unity_connection = types.ModuleType("transport.legacy.unity_connection")
    unity_connection.get_unity_connection_pool = lambda: PoolStub()
    monkeypatch.setitem(sys.modules, "transport.legacy.unity_connection", unity_connection)

    selected = await middleware._maybe_autoselect_instance(ctx)

    assert selected is None
    assert await middleware.get_active_instance(ctx) is None
