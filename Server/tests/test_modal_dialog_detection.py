"""Modal-dialog detection: telling a blocked Editor main thread apart from a busy one.

A modal dialog and an ordinary busy Unity both surface as a command timeout, but they need
opposite responses — one is cleared by answering the dialog, the other by waiting. These cover the
classification and the paths that consume it.
"""

import uuid

import pytest

from transport.plugin_hub import PluginHub


async def _async_none(ctx):
    return None


class _Ctx:
    """Minimal stand-in for a FastMCP context; wait_for_editor_ready only carries it through."""

    def __init__(self):
        self.session_id = str(uuid.uuid4())
        self._state = {}


def _liveness(*, blocked=False, stall_ms=0, title=None, body=None, buttons=None, supported=True,
              kind="dialog", answerable=None):
    modal = {"supported": supported, "blocked": blocked}
    if blocked:
        if answerable is None:
            answerable = kind == "dialog" and bool(buttons)
        modal.update({
            "kind": kind,
            "answerable": answerable,
            "title": title,
            "body": body,
            "buttons": buttons or [],
        })
    return {
        "main_thread_stall_ms": stall_ms,
        "main_thread_ticks": 42,
        "pending_commands": 1,
        "sample_age_ms": 120,
        "modal": modal,
    }


@pytest.fixture
def probe(monkeypatch):
    """Install a canned liveness answer and hand back a setter for it."""
    state = {"payload": None}

    async def fake_probe(cls, session_id):
        return state["payload"]

    monkeypatch.setattr(PluginHub, "probe_liveness", classmethod(fake_probe))
    return state


@pytest.mark.asyncio
async def test_modal_reported_as_answerable_with_buttons(probe):
    probe["payload"] = _liveness(
        blocked=True,
        stall_ms=24200,
        title="Scene(s) Have Been Modified",
        body="Do you want to reload the modified scene(s)?",
        buttons=["Reload", "Ignore"],
    )

    result = await PluginHub.describe_stall("sess", "read_console")

    assert result["success"] is False
    assert result["hint"] == "answer_dialog"
    assert result["data"]["reason"] == "modal_dialog"
    assert result["data"]["dialog"]["buttons"] == ["Reload", "Ignore"]
    assert result["data"]["dialog"]["answerable"] is True
    # The agent needs the actual question, not just that something is blocking.
    assert "Scene(s) Have Been Modified" in result["error"]
    assert "reload the modified scene(s)" in result["error"]
    assert "Reload, Ignore" in result["error"]


@pytest.mark.asyncio
async def test_unity_drawn_modal_window_is_reported_but_not_answerable(probe):
    """EditorWindow.ShowModal blocks just as hard, but paints its buttons with IMGUI.

    Reporting it as merely "busy" would tell the agent to wait for something that never clears.
    """
    probe["payload"] = _liveness(
        blocked=True, stall_ms=22790, title="Migration Step 2", kind="editor_window", buttons=[])

    result = await PluginHub.describe_stall("sess", "read_console")

    assert result["data"]["reason"] == "modal_dialog"
    assert result["hint"] == "user_action_required"
    assert result["data"]["dialog"]["answerable"] is False
    assert "Migration Step 2" in result["error"]


@pytest.mark.asyncio
async def test_modal_on_unsupported_platform_asks_for_a_human(probe):
    probe["payload"] = _liveness(
        blocked=True, stall_ms=9000, title="Save Scene", buttons=[], supported=False)

    result = await PluginHub.describe_stall("sess", "read_console")

    assert result["hint"] == "user_action_required"
    assert result["data"]["dialog"]["answerable"] is False


@pytest.mark.asyncio
async def test_long_main_thread_operation_is_a_wait_not_a_dialog(probe):
    probe["payload"] = _liveness(blocked=False, stall_ms=9000)

    result = await PluginHub.describe_stall("sess", "manage_asset")

    assert result["hint"] == "wait"
    assert result["data"]["reason"] == "main_thread_blocked"
    # Never tell the caller to go press something when there is nothing to press.
    assert "dialog" not in result["data"]


@pytest.mark.asyncio
async def test_healthy_editor_is_not_classified_as_stalled(probe):
    probe["payload"] = _liveness(blocked=False, stall_ms=40)

    assert await PluginHub.describe_stall("sess", "read_console") is None


@pytest.mark.asyncio
async def test_plugin_that_cannot_answer_falls_back_to_retry(probe):
    """An older plugin, or a genuinely gone session, must not be reported as a stall."""
    probe["payload"] = None

    assert await PluginHub.describe_stall("sess", "read_console") is None


@pytest.mark.asyncio
async def test_liveness_probe_does_not_classify_itself(monkeypatch):
    """Classifying the probe's own timeout would recurse until the stack ran out."""
    calls = []

    async def fake_probe(cls, session_id):
        calls.append(session_id)
        return _liveness(blocked=True, stall_ms=5000, title="x", buttons=["ok"])

    monkeypatch.setattr(PluginHub, "probe_liveness", classmethod(fake_probe))

    assert await PluginHub.describe_stall("sess", "liveness") is None
    assert await PluginHub.describe_stall("sess", "answer_dialog") is None
    assert calls == []


@pytest.mark.asyncio
async def test_off_main_thread_commands_get_a_short_timeout():
    """They are answered on the receive thread; waiting the full command timeout is pointless."""
    assert PluginHub._OFF_MAIN_THREAD_COMMANDS == {"liveness", "answer_dialog"}
    assert PluginHub.LIVENESS_TIMEOUT < PluginHub.COMMAND_TIMEOUT


@pytest.mark.asyncio
async def test_wait_for_editor_ready_abandons_polling_on_a_modal(monkeypatch):
    """Polling a dialog for the full timeout delays an answer the caller could already act on."""
    monkeypatch.delenv("PYTEST_CURRENT_TEST", raising=False)

    from services.tools import refresh_unity as mod

    blocked_response = {
        "success": False,
        "error": "Unity's main thread is blocked by a modal dialog: 'Save Scene'",
        "hint": "answer_dialog",
        "data": {"reason": "modal_dialog", "dialog": {"buttons": ["Save", "Don't Save"]}},
    }

    polls = 0

    async def fake_get_editor_state(ctx):
        nonlocal polls
        polls += 1
        return blocked_response

    monkeypatch.setattr(mod.editor_state, "get_editor_state", fake_get_editor_state)

    ready, elapsed, blocked = await mod.wait_for_editor_ready(_Ctx(), timeout_s=10.0)

    assert ready is False
    assert blocked is blocked_response
    assert elapsed < 5.0, "should bail on the first poll, not wait out the timeout"
    assert polls == 1


@pytest.mark.asyncio
async def test_wait_for_editor_ready_keeps_waiting_through_a_busy_main_thread(monkeypatch):
    """A long operation clears on its own, so this one must not give up early."""
    monkeypatch.delenv("PYTEST_CURRENT_TEST", raising=False)

    from services.tools import refresh_unity as mod

    polls = 0

    async def fake_get_editor_state(ctx):
        nonlocal polls
        polls += 1
        if polls < 3:
            return {
                "success": False,
                "hint": "wait",
                "data": {"reason": "main_thread_blocked"},
            }
        return {"data": {"advice": {"ready_for_tools": True, "blocking_reasons": []}}}

    monkeypatch.setattr(mod.editor_state, "get_editor_state", fake_get_editor_state)

    ready, _, blocked = await mod.wait_for_editor_ready(_Ctx(), timeout_s=10.0)

    assert ready is True
    assert blocked is None
    assert polls == 3


@pytest.mark.asyncio
async def test_read_path_reports_answerable_not_platform_support(monkeypatch):
    """A Unity-drawn modal is inspectable but not pressable; conflating the two invites a
    press the Editor always refuses."""
    from services.tools import answer_dialog as mod

    async def fake_send(send_fn, unity_instance, command, params, **kwargs):
        return {
            "success": True,
            "data": _liveness(
                blocked=True, stall_ms=5000, title="Migration Step 2",
                kind="editor_window", buttons=[]),
        }

    monkeypatch.setattr(mod.unity_transport, "send_with_unity_instance", fake_send)
    monkeypatch.setattr(mod, "get_unity_instance_from_context", _async_none)

    resp = await mod.answer_dialog(_Ctx())
    data = resp.model_dump() if hasattr(resp, "model_dump") else resp

    assert data["data"]["blocked"] is True
    assert data["data"]["dialog"]["answerable"] is False
    assert data["data"]["dialog"]["title"] == "Migration Step 2"


@pytest.mark.asyncio
async def test_read_path_propagates_transport_failure(monkeypatch):
    """A failed probe carries no modal; reporting that as "nothing is blocking" would hide the
    error behind a confident all-clear."""
    from services.tools import answer_dialog as mod

    failure = {
        "success": False,
        "error": "Unity session not available; please retry",
        "hint": "retry",
        "data": {"reason": "no_unity_session"},
    }

    async def fake_send(send_fn, unity_instance, command, params, **kwargs):
        return failure

    monkeypatch.setattr(mod.unity_transport, "send_with_unity_instance", fake_send)
    monkeypatch.setattr(mod, "get_unity_instance_from_context", _async_none)

    resp = await mod.answer_dialog(_Ctx())
    data = resp.model_dump() if hasattr(resp, "model_dump") else resp

    assert data["success"] is False
    assert data["hint"] == "retry"
    assert "session not available" in data["error"]
