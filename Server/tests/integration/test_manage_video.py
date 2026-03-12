"""
Tests for the manage_video tool.
Validates parameter routing for Unity VideoPlayer operations.
"""
import pytest

from .test_helpers import DummyContext
import services.tools.manage_video as video_mod


def _fake_send_factory(captured: dict, response: dict = None):
    if response is None:
        response = {"success": True, "message": "OK", "data": {}}

    async def fake_send(cmd, params, **kwargs):
        captured["cmd"] = cmd
        captured["params"] = params
        return response

    return fake_send


@pytest.mark.asyncio
async def test_list_players(monkeypatch):
    captured = {}
    monkeypatch.setattr(
        video_mod, "async_send_command_with_retry",
        _fake_send_factory(captured),
    )
    resp = await video_mod.manage_video(ctx=DummyContext(), action="list_players")
    assert resp["success"] is True
    assert captured["cmd"] == "manage_video"
    assert captured["params"]["action"] == "list_players"


@pytest.mark.asyncio
async def test_get_player(monkeypatch):
    captured = {}
    monkeypatch.setattr(
        video_mod, "async_send_command_with_retry",
        _fake_send_factory(captured),
    )
    resp = await video_mod.manage_video(
        ctx=DummyContext(), action="get_player", target="IntroVideo",
    )
    assert resp["success"] is True
    assert captured["params"]["target"] == "IntroVideo"


@pytest.mark.asyncio
async def test_set_player(monkeypatch):
    captured = {}
    monkeypatch.setattr(
        video_mod, "async_send_command_with_retry",
        _fake_send_factory(captured),
    )
    resp = await video_mod.manage_video(
        ctx=DummyContext(), action="set_player", target="IntroVideo",
        properties='{"playback_speed": 2.0, "loop": true}',
    )
    assert resp["success"] is True
    assert captured["params"]["properties"] == '{"playback_speed": 2.0, "loop": true}'


@pytest.mark.asyncio
async def test_play(monkeypatch):
    captured = {}
    monkeypatch.setattr(
        video_mod, "async_send_command_with_retry",
        _fake_send_factory(captured),
    )
    resp = await video_mod.manage_video(
        ctx=DummyContext(), action="play", target="IntroVideo",
    )
    assert resp["success"] is True
    assert captured["params"]["action"] == "play"


@pytest.mark.asyncio
async def test_set_time(monkeypatch):
    captured = {}
    monkeypatch.setattr(
        video_mod, "async_send_command_with_retry",
        _fake_send_factory(captured),
    )
    resp = await video_mod.manage_video(
        ctx=DummyContext(), action="set_time", target="IntroVideo", time=30.0,
    )
    assert resp["success"] is True
    assert captured["params"]["time"] == 30.0


@pytest.mark.asyncio
async def test_none_params_stripped(monkeypatch):
    captured = {}
    monkeypatch.setattr(
        video_mod, "async_send_command_with_retry",
        _fake_send_factory(captured),
    )
    await video_mod.manage_video(ctx=DummyContext(), action="list_players")
    assert set(captured["params"].keys()) == {"action"}


@pytest.mark.asyncio
async def test_python_exception_caught(monkeypatch):
    async def raising_send(cmd, params, **kwargs):
        raise ConnectionError("Unity not connected")

    monkeypatch.setattr(
        video_mod, "async_send_command_with_retry", raising_send,
    )
    resp = await video_mod.manage_video(ctx=DummyContext(), action="list_players")
    assert resp["success"] is False
    assert "Unity not connected" in resp["message"]
