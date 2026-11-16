import signal
import time

import pytest

from core.config import config
from transport import idle_shutdown
from transport.plugin_registry import PluginRegistry


class FakeClock:
    def __init__(self):
        self.now = 0.0

    def __call__(self):
        return self.now


class ScriptedRegistry:
    """Each check advances the clock by one interval and reports the next scripted
    state: True = an Editor is connected, False = none, a number = none now, but a
    session ended at that time since the previous check."""

    def __init__(self, clock, states, interval):
        self.clock = clock
        self.states = list(states)
        self.interval = interval
        self.checks = 0
        self.last_session_ended = None

    async def list_sessions(self, user_id=None):
        self.checks += 1
        self.clock.now += self.interval
        if not self.states:
            raise AssertionError("watcher kept running past the scripted checks")
        state = self.states.pop(0)
        if state is True:
            return {"session": object()}
        if state is not False:
            self.last_session_ended = state
        return {}


async def _run(states, idle_seconds=60.0, interval=15.0):
    clock = FakeClock()
    registry = ScriptedRegistry(clock, states, interval)
    requests = []
    await idle_shutdown.shutdown_when_idle(
        registry,
        idle_seconds=idle_seconds,
        check_interval=0,
        request_shutdown=lambda: requests.append(clock.now),
        clock=clock,
    )
    return registry.checks, requests


@pytest.mark.asyncio
async def test_exits_when_no_editor_ever_connects():
    checks, requests = await _run([False] * 10)
    assert checks == 4
    assert requests == [60.0]


@pytest.mark.asyncio
async def test_idle_window_starts_when_the_last_editor_leaves():
    # Editor connected for the first three checks (until t=45), gone afterwards.
    checks, requests = await _run([True] * 3 + [False] * 10)
    assert requests == [105.0]
    assert checks == 7


@pytest.mark.asyncio
async def test_reconnect_within_the_window_restarts_it():
    # Gone, back at t=60 (e.g. after a domain reload or an Editor restart), gone again.
    checks, requests = await _run([False, False, False, True] + [False] * 10)
    assert requests == [120.0]
    assert checks == 8


@pytest.mark.asyncio
async def test_session_between_polls_restarts_the_window():
    # Nothing at the first three checks; an Editor connects after t=45 and leaves at
    # t=59, so no check ever sees it. The window must run from t=59, not from start-up.
    checks, requests = await _run([False, False, False, 59.0] + [False] * 10)
    assert requests == [120.0]
    assert checks == 8


@pytest.mark.asyncio
async def test_registry_stamps_when_a_session_ends():
    registry = PluginRegistry()
    await registry.register("s1", "Proj", "hash1", "6000.3")
    assert registry.last_session_ended is None
    before = time.monotonic()
    await registry.unregister("s1")
    assert before <= registry.last_session_ended <= time.monotonic()
    stamped = registry.last_session_ended
    await registry.unregister("unknown")
    assert registry.last_session_ended == stamped


@pytest.mark.asyncio
async def test_never_exits_while_an_editor_stays_connected():
    with pytest.raises(AssertionError, match="past the scripted checks"):
        await _run([True] * 50)


@pytest.mark.parametrize(
    "transport, remote_hosted, token, expected",
    [
        ("http", False, "tok", True),
        ("http", False, None, False),   # started by hand, not by an Editor
        ("http", True, "tok", False),   # remotely hosted
        ("stdio", False, "tok", False),  # stdio lives as long as its client
    ],
)
def test_only_unity_launched_local_http_servers_stop_themselves(
    monkeypatch, transport, remote_hosted, token, expected
):
    monkeypatch.setattr(config, "transport_mode", transport)
    monkeypatch.setattr(config, "http_remote_hosted", remote_hosted)
    if token:
        monkeypatch.setenv("UNITY_MCP_INSTANCE_TOKEN", token)
    else:
        monkeypatch.delenv("UNITY_MCP_INSTANCE_TOKEN", raising=False)
    assert idle_shutdown.is_enabled() is expected


def test_request_shutdown_raises_sigint_and_marks_it_requested(monkeypatch):
    raised = []
    monkeypatch.setattr(signal, "raise_signal", raised.append)
    monkeypatch.setattr(idle_shutdown, "requested", False)
    idle_shutdown._request_shutdown()
    assert raised == [signal.SIGINT]
    assert idle_shutdown.requested is True
