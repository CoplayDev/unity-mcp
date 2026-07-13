"""Stale active-instance pin tests.

A pin outlives the editor it names. While it stayed pinned it also suppressed
auto-select, so every later call failed with no_unity_session and neither
waiting nor relaunching the editor recovered: the editor re-registers under its
own name, which is not the name the pin holds. The pin is now dropped once some
other instance is registered.
"""

from types import SimpleNamespace

import pytest

import transport.unity_transport as unity_transport
from transport.plugin_hub import PluginHub
from transport.unity_instance_middleware import UnityInstanceMiddleware


class FakeContext:
    """Minimal ctx shim over the session-scoped state the middleware uses."""

    def __init__(self, state: dict | None = None):
        self._state = state or {}

    async def get_state(self, key: str):
        return self._state.get(key)

    async def set_state(self, key: str, value) -> None:
        self._state[key] = value


@pytest.fixture
def http_hub(monkeypatch):
    monkeypatch.setattr(unity_transport, "_is_http_transport", lambda: True)
    monkeypatch.setattr(PluginHub, "is_configured", classmethod(lambda cls: True))


def _registered(monkeypatch, middleware, *ids):
    async def discover(_ctx):
        return [SimpleNamespace(id=i, hash=i.split("@")[-1], name=i.split("@")[0]) for i in ids]

    monkeypatch.setattr(middleware, "_discover_instances", discover)


@pytest.mark.asyncio
async def test_pin_naming_an_unregistered_instance_is_dropped(http_hub, monkeypatch):
    """The ghost pin that bricked routing must not survive a call."""
    middleware = UnityInstanceMiddleware()
    ctx = FakeContext()
    _registered(monkeypatch, middleware, "Trailblazers-1@aaaa")

    resolved = await middleware._drop_stale_pin(ctx, "wt-display-system@bbbb")

    assert resolved is None
    assert await middleware.get_active_instance(ctx) is None


@pytest.mark.asyncio
async def test_pin_naming_a_registered_instance_is_kept(http_hub, monkeypatch):
    """A live pin is the user's choice and must be left alone."""
    middleware = UnityInstanceMiddleware()
    ctx = FakeContext()
    _registered(monkeypatch, middleware, "Trailblazers-1@aaaa", "Trailblazers-2@cccc")

    resolved = await middleware._drop_stale_pin(ctx, "Trailblazers-2@cccc")

    assert resolved == "Trailblazers-2@cccc"


@pytest.mark.asyncio
async def test_pin_is_kept_while_no_instance_is_registered(http_hub, monkeypatch):
    """An empty registry is a domain reload in flight, not a dead instance."""
    middleware = UnityInstanceMiddleware()
    ctx = FakeContext()
    _registered(monkeypatch, middleware)

    resolved = await middleware._drop_stale_pin(ctx, "Trailblazers-1@aaaa")

    assert resolved == "Trailblazers-1@aaaa"


@pytest.mark.asyncio
async def test_dropped_pin_falls_through_to_the_sole_live_instance(http_hub, monkeypatch):
    """Dropping the ghost must let auto-select reach the healthy editor."""
    middleware = UnityInstanceMiddleware()
    ctx = FakeContext({middleware._ACTIVE_INSTANCE_STATE_KEY: "wt-display-system@bbbb"})
    _registered(monkeypatch, middleware, "Trailblazers-1@aaaa")

    async def autoselect(_ctx):
        await middleware.set_active_instance(_ctx, "Trailblazers-1@aaaa")
        return "Trailblazers-1@aaaa"

    monkeypatch.setattr(middleware, "_maybe_autoselect_instance", autoselect)

    active = await middleware.get_active_instance(ctx)
    active = await middleware._drop_stale_pin(ctx, active)
    if not active:
        active = await middleware._maybe_autoselect_instance(ctx)

    assert active == "Trailblazers-1@aaaa"
