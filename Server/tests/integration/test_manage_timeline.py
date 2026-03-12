"""
Tests for the manage_timeline tool.
Validates parameter routing for Timeline/PlayableDirector operations.
"""
import pytest

from .test_helpers import DummyContext
import services.tools.manage_timeline as timeline_mod


def _fake_send_factory(captured: dict, response: dict = None):
    if response is None:
        response = {"success": True, "message": "OK", "data": {}}

    async def fake_send(cmd, params, **kwargs):
        captured["cmd"] = cmd
        captured["params"] = params
        return response

    return fake_send


@pytest.mark.asyncio
async def test_list_directors(monkeypatch):
    captured = {}
    monkeypatch.setattr(
        timeline_mod, "async_send_command_with_retry",
        _fake_send_factory(captured, {
            "success": True, "message": "Found 2 PlayableDirector(s).", "data": {"items": []},
        }),
    )
    resp = await timeline_mod.manage_timeline(ctx=DummyContext(), action="list_directors")
    assert resp["success"] is True
    assert captured["cmd"] == "manage_timeline"
    assert captured["params"]["action"] == "list_directors"


@pytest.mark.asyncio
async def test_get_director(monkeypatch):
    captured = {}
    monkeypatch.setattr(
        timeline_mod, "async_send_command_with_retry",
        _fake_send_factory(captured),
    )
    resp = await timeline_mod.manage_timeline(
        ctx=DummyContext(), action="get_director", target="CutsceneDirector",
    )
    assert resp["success"] is True
    assert captured["params"]["target"] == "CutsceneDirector"


@pytest.mark.asyncio
async def test_play(monkeypatch):
    captured = {}
    monkeypatch.setattr(
        timeline_mod, "async_send_command_with_retry",
        _fake_send_factory(captured),
    )
    resp = await timeline_mod.manage_timeline(
        ctx=DummyContext(), action="play", target="CutsceneDirector",
    )
    assert resp["success"] is True
    assert captured["params"]["action"] == "play"


@pytest.mark.asyncio
async def test_set_time(monkeypatch):
    captured = {}
    monkeypatch.setattr(
        timeline_mod, "async_send_command_with_retry",
        _fake_send_factory(captured),
    )
    resp = await timeline_mod.manage_timeline(
        ctx=DummyContext(), action="set_time", target="Director", time=2.5,
    )
    assert resp["success"] is True
    assert captured["params"]["time"] == 2.5


@pytest.mark.asyncio
async def test_list_tracks(monkeypatch):
    captured = {}
    monkeypatch.setattr(
        timeline_mod, "async_send_command_with_retry",
        _fake_send_factory(captured),
    )
    resp = await timeline_mod.manage_timeline(
        ctx=DummyContext(), action="list_tracks", target="Director",
    )
    assert resp["success"] is True
    assert captured["params"]["action"] == "list_tracks"


@pytest.mark.asyncio
async def test_get_bindings(monkeypatch):
    captured = {}
    monkeypatch.setattr(
        timeline_mod, "async_send_command_with_retry",
        _fake_send_factory(captured),
    )
    resp = await timeline_mod.manage_timeline(
        ctx=DummyContext(), action="get_bindings", target="Director",
    )
    assert resp["success"] is True
    assert captured["params"]["action"] == "get_bindings"


@pytest.mark.asyncio
async def test_none_params_stripped(monkeypatch):
    captured = {}
    monkeypatch.setattr(
        timeline_mod, "async_send_command_with_retry",
        _fake_send_factory(captured),
    )
    await timeline_mod.manage_timeline(ctx=DummyContext(), action="list_directors")
    assert set(captured["params"].keys()) == {"action"}


@pytest.mark.asyncio
async def test_python_exception_caught(monkeypatch):
    async def raising_send(cmd, params, **kwargs):
        raise ConnectionError("Unity not connected")

    monkeypatch.setattr(
        timeline_mod, "async_send_command_with_retry", raising_send,
    )
    resp = await timeline_mod.manage_timeline(ctx=DummyContext(), action="list_directors")
    assert resp["success"] is False
    assert "Unity not connected" in resp["message"]
