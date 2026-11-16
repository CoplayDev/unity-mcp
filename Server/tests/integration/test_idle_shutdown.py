import signal

import pytest

from core.config import config
from transport import idle_shutdown


class FakeClock:
    def __init__(self):
        self.now = 0.0

    def __call__(self):
        return self.now


class ScriptedRegistry:
    """Each check advances the clock by one interval and reports the next scripted
    connection state (True = an Editor is connected)."""

    def __init__(self, clock, states, interval):
        self.clock = clock
        self.states = list(states)
        self.interval = interval
        self.checks = 0

    async def list_sessions(self, user_id=None):
        self.checks += 1
        self.clock.now += self.interval
        if not self.states:
            raise AssertionError("watcher kept running past the scripted checks")
        return {"session": object()} if self.states.pop(0) else {}


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
