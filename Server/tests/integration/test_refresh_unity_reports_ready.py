import pytest

from .test_helpers import DummyContext

# What Unity replies with a compile requested: it answers the moment compilation starts,
# before the domain reload, so its own snapshot is "compiling" with a hint to poll.
UNITY_REPLY_AT_COMPILE_START = {
    "success": True,
    "message": "Refresh requested.",
    "data": {
        "refresh_triggered": True,
        "compile_requested": True,
        "compile_started": True,
        "resulting_state": "compiling",
        "hint": "If Unity enters compilation/domain reload, poll the mcpforunity://editor/state resource until data.advice.ready_for_tools is true.",
    },
}


def _patch_transport(monkeypatch, refresh_reply=UNITY_REPLY_AT_COMPILE_START):
    async def fake_send_with_unity_instance(send_fn, unity_instance, command_type, params, **kwargs):
        if command_type == "refresh_unity":
            return refresh_reply
        if command_type == "get_editor_state":
            return {"success": True, "data": {"advice": {"ready_for_tools": True}}}
        raise ValueError(f"Unexpected command: {command_type}")

    import services.tools.refresh_unity as refresh_mod
    monkeypatch.setattr(refresh_mod.unity_transport, "send_with_unity_instance", fake_send_with_unity_instance)


def _poll_not_ready_then_ready(monkeypatch, not_ready_polls=2):
    """Run the real readiness poll (not the pytest short-circuit) against a compile that is still
    going for the first polls. Returns a list that records each poll's answer."""
    monkeypatch.delenv("PYTEST_CURRENT_TEST", raising=False)
    import services.tools.refresh_unity as refresh_mod
    answers = []

    async def fake_get_editor_state(ctx):
        ready = len(answers) >= not_ready_polls
        answers.append(ready)
        return {"data": {"advice": {"ready_for_tools": ready, "blocking_reasons": [] if ready else ["compiling"]}}}

    monkeypatch.setattr(refresh_mod.editor_state, "get_editor_state", fake_get_editor_state)
    return answers


@pytest.mark.asyncio
async def test_refresh_unity_reports_ready_after_waiting(monkeypatch):
    """After the server-side wait confirms readiness, the reply must not repeat Unity's
    compile-start snapshot: agents read "compiling" plus "poll until ready" as a refresh
    that hasn't finished and slept or polled on top of a wait that already happened."""
    from services.tools.refresh_unity import refresh_unity

    ctx = DummyContext()
    await ctx.set_state("unity_instance", "UnityMCPTests@cc8756d4cce0805a")
    _patch_transport(monkeypatch)
    polls = _poll_not_ready_then_ready(monkeypatch)

    resp = await refresh_unity(ctx, compile="request", wait_for_ready=True)
    payload = resp.model_dump() if hasattr(resp, "model_dump") else resp
    data = payload["data"]

    # it reported ready only after the poll saw the editor ready, not on the first answer
    assert polls == [False, False, True]
    assert data["waited_seconds"] > 0
    assert payload["success"] is True
    assert data["resulting_state"] == "idle"
    assert data["ready_confirmed"] is True
    assert data["unity_state_at_reply"] == "compiling"
    assert "poll" not in payload["message"].lower()
    assert "No need to poll" in data["hint"]
    # Unity's facts about the refresh itself are kept
    assert data["compile_started"] is True and data["refresh_triggered"] is True


@pytest.mark.asyncio
async def test_refresh_unity_without_wait_keeps_unity_reply(monkeypatch):
    """With wait_for_ready=false nothing waited, so Unity's snapshot and its hint to poll are right."""
    from services.tools.refresh_unity import refresh_unity

    ctx = DummyContext()
    await ctx.set_state("unity_instance", "UnityMCPTests@cc8756d4cce0805a")
    _patch_transport(monkeypatch)

    resp = await refresh_unity(ctx, compile="request", wait_for_ready=False)
    payload = resp.model_dump() if hasattr(resp, "model_dump") else resp

    assert payload["data"]["resulting_state"] == "compiling"
    assert "ready_confirmed" not in payload["data"]
    assert "poll" in payload["data"]["hint"]


DISCONNECT_DURING_RELOAD = {"success": False, "error": "Connection closed before reading expected bytes"}


@pytest.mark.asyncio
async def test_refresh_unity_recovered_disconnect_reports_ready(monkeypatch):
    """The reload can drop the connection before Unity's reply arrives; once the wait confirms
    readiness, that recovery reply carries the same confirmed-ready fields as the normal one."""
    from services.tools.refresh_unity import refresh_unity

    ctx = DummyContext()
    await ctx.set_state("unity_instance", "UnityMCPTests@cc8756d4cce0805a")
    _patch_transport(monkeypatch, refresh_reply=DISCONNECT_DURING_RELOAD)
    polls = _poll_not_ready_then_ready(monkeypatch)

    resp = await refresh_unity(ctx, compile="request", wait_for_ready=True)
    payload = resp.model_dump() if hasattr(resp, "model_dump") else resp
    data = payload["data"]

    assert polls == [False, False, True]
    assert payload["success"] is True
    assert data["recovered_from_disconnect"] is True
    assert data["ready_confirmed"] is True
    assert data["resulting_state"] == "idle"
    assert data["waited_seconds"] > 0
    assert "unity_state_at_reply" not in data   # Unity's reply never arrived
    assert "editor is ready" in payload["message"]


@pytest.mark.asyncio
async def test_refresh_unity_recovered_disconnect_without_wait_does_not_claim_ready(monkeypatch):
    """With wait_for_ready=false nothing waited after the reload dropped the connection, so the
    reply must not say the editor is ready (it used to)."""
    from services.tools.refresh_unity import refresh_unity

    ctx = DummyContext()
    await ctx.set_state("unity_instance", "UnityMCPTests@cc8756d4cce0805a")
    _patch_transport(monkeypatch, refresh_reply=DISCONNECT_DURING_RELOAD)

    resp = await refresh_unity(ctx, compile="request", wait_for_ready=False)
    payload = resp.model_dump() if hasattr(resp, "model_dump") else resp
    data = payload["data"]

    assert payload["success"] is True
    assert data["recovered_from_disconnect"] is True
    assert data["ready_confirmed"] is False
    assert "ready" not in payload["message"].lower()
    assert "poll" in data["hint"].lower()
