"""Stale active-instance pin tests.

A pin outlives the editor it names. While it stayed pinned it also suppressed
auto-select, so every later call failed with no_unity_session and neither
waiting nor relaunching the editor recovered: the editor re-registers under its
own name, which is not the name the pin holds.

Dropping it is only safe under two conditions, both covered here: the pinned hash
gets the same reconnect window `_resolve_session_id` gives it, so a domain reload
does not read as a departure; and a departure raises rather than falling through to
auto-select, which would silently move a call from project A to project B (#1023).
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
    # No reconnect window unless a test asks for one, so the suite does not sit out 20s
    monkeypatch.setenv("UNITY_MCP_SESSION_RESOLVE_MAX_WAIT_S", "0")


def _registered(monkeypatch, middleware, *ids):
    async def discover(_ctx):
        return [SimpleNamespace(id=i, hash=i.split("@")[-1], name=i.split("@")[0]) for i in ids]

    monkeypatch.setattr(middleware, "_discover_instances", discover)


def _registered_over_time(monkeypatch, middleware, *rounds):
    """Return a different registry on each successive poll."""
    calls = {"n": 0}

    async def discover(_ctx):
        ids = rounds[min(calls["n"], len(rounds) - 1)]
        calls["n"] += 1
        return [SimpleNamespace(id=i, hash=i.split("@")[-1], name=i.split("@")[0]) for i in ids]

    monkeypatch.setattr(middleware, "_discover_instances", discover)
    return calls


@pytest.mark.asyncio
async def test_departed_pin_is_dropped_and_refuses(http_hub, monkeypatch):
    """The ghost pin that bricked routing must not survive a call."""
    middleware = UnityInstanceMiddleware()
    ctx = FakeContext({UnityInstanceMiddleware._ACTIVE_INSTANCE_STATE_KEY: "wt-display-system@bbbb"})
    _registered(monkeypatch, middleware, "Trailblazers-1@aaaa")

    with pytest.raises(ValueError) as excinfo:
        await middleware._drop_stale_pin(ctx, "wt-display-system@bbbb")

    assert "wt-display-system@bbbb" in str(excinfo.value)
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
async def test_pin_survives_a_reload_that_ends_inside_the_wait(http_hub, monkeypatch):
    """A reloading editor is absent from a populated registry; the wait must cover it."""
    monkeypatch.setenv("UNITY_MCP_SESSION_RESOLVE_MAX_WAIT_S", "5")
    middleware = UnityInstanceMiddleware()
    ctx = FakeContext()
    calls = _registered_over_time(
        monkeypatch,
        middleware,
        ("Trailblazers-1@aaaa",),
        ("Trailblazers-1@aaaa", "Trailblazers-2@cccc"),
    )

    resolved = await middleware._drop_stale_pin(ctx, "Trailblazers-2@cccc")

    assert resolved == "Trailblazers-2@cccc"
    assert calls["n"] > 1, "must poll again rather than drop on the first miss"


@pytest.mark.asyncio
async def test_dropped_pin_never_retargets_another_project(http_hub, monkeypatch):
    """Clearing then auto-selecting the survivor is #1023 arriving through another door."""
    middleware = UnityInstanceMiddleware()
    ctx = FakeContext({UnityInstanceMiddleware._ACTIVE_INSTANCE_STATE_KEY: "project-a@bbbb"})
    _registered(monkeypatch, middleware, "project-b@aaaa")

    async def autoselect(_ctx):
        pytest.fail("auto-select must not run after a pinned instance departs")

    monkeypatch.setattr(middleware, "_maybe_autoselect_instance", autoselect)

    with pytest.raises(ValueError):
        await middleware._drop_stale_pin(ctx, "project-a@bbbb")

    assert await middleware.get_active_instance(ctx) is None
