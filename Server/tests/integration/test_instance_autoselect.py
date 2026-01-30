import asyncio
import sys
import types
from types import SimpleNamespace

from .test_helpers import DummyContext
from core.config import config


class DummyMiddlewareContext:
    def __init__(self, ctx):
        self.fastmcp_context = ctx


def test_auto_selects_single_instance_via_pluginhub(monkeypatch):
    plugin_hub = types.ModuleType("transport.plugin_hub")

    class PluginHub:
        @classmethod
        def is_configured(cls) -> bool:
            return True

        @classmethod
        async def get_sessions(cls):
            raise AssertionError("get_sessions should be stubbed in test")

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

    selected = asyncio.run(middleware._maybe_autoselect_instance(ctx))

    assert selected == "Ramble@deadbeef"
    assert middleware.get_active_instance(ctx) == "Ramble@deadbeef"
    assert call_count["sessions"] == 1

    asyncio.run(middleware._inject_unity_instance(middleware_context))

    assert ctx.get_state("unity_instance") == "Ramble@deadbeef"
    assert call_count["sessions"] == 1


def test_auto_selects_single_instance_via_stdio(monkeypatch):
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

    selected = asyncio.run(middleware._maybe_autoselect_instance(ctx))

    assert selected == "UnityMCPTests@cc8756d4"
    assert middleware.get_active_instance(ctx) == "UnityMCPTests@cc8756d4"

    asyncio.run(middleware._inject_unity_instance(middleware_context))

    assert ctx.get_state("unity_instance") == "UnityMCPTests@cc8756d4"


def test_auto_select_handles_stdio_errors(monkeypatch):
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

    selected = asyncio.run(middleware._maybe_autoselect_instance(ctx))

    assert selected is None
    assert middleware.get_active_instance(ctx) is None
