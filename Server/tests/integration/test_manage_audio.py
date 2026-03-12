"""
Tests for the manage_audio tool.
Validates parameter routing, action dispatch, and error handling
for Unity audio operations.
"""
import pytest

from .test_helpers import DummyContext
import services.tools.manage_audio as audio_mod


def _fake_send_factory(captured: dict, response: dict = None):
    if response is None:
        response = {"success": True, "message": "OK", "data": {}}

    async def fake_send(cmd, params, **kwargs):
        captured["cmd"] = cmd
        captured["params"] = params
        return response

    return fake_send


@pytest.mark.asyncio
async def test_list_sources(monkeypatch):
    """list_sources sends page_size."""
    captured = {}
    monkeypatch.setattr(
        audio_mod, "async_send_command_with_retry",
        _fake_send_factory(captured),
    )

    resp = await audio_mod.manage_audio(
        ctx=DummyContext(), action="list_sources", page_size=10,
    )

    assert resp["success"] is True
    assert captured["params"]["page_size"] == 10


@pytest.mark.asyncio
async def test_get_source(monkeypatch):
    """get_source sends target."""
    captured = {}
    monkeypatch.setattr(
        audio_mod, "async_send_command_with_retry",
        _fake_send_factory(captured),
    )

    resp = await audio_mod.manage_audio(
        ctx=DummyContext(), action="get_source", target="BGM",
    )

    assert resp["success"] is True
    assert captured["params"]["target"] == "BGM"


@pytest.mark.asyncio
async def test_set_source(monkeypatch):
    """set_source sends target and properties."""
    captured = {}
    monkeypatch.setattr(
        audio_mod, "async_send_command_with_retry",
        _fake_send_factory(captured),
    )

    resp = await audio_mod.manage_audio(
        ctx=DummyContext(), action="set_source",
        target="BGM", properties='{"volume": 0.5}',
    )

    assert resp["success"] is True
    assert captured["params"]["properties"] == '{"volume": 0.5}'


@pytest.mark.asyncio
async def test_play_action(monkeypatch):
    """play sends target."""
    captured = {}
    monkeypatch.setattr(
        audio_mod, "async_send_command_with_retry",
        _fake_send_factory(captured),
    )

    resp = await audio_mod.manage_audio(
        ctx=DummyContext(), action="play", target="SFX",
    )

    assert resp["success"] is True
    assert captured["params"]["action"] == "play"


@pytest.mark.asyncio
async def test_list_clips_with_filter(monkeypatch):
    """list_clips sends filter and page_size."""
    captured = {}
    monkeypatch.setattr(
        audio_mod, "async_send_command_with_retry",
        _fake_send_factory(captured),
    )

    resp = await audio_mod.manage_audio(
        ctx=DummyContext(), action="list_clips",
        filter="explosion", page_size=5,
    )

    assert resp["success"] is True
    assert captured["params"]["filter"] == "explosion"


@pytest.mark.asyncio
async def test_get_clip_info(monkeypatch):
    """get_clip_info sends target."""
    captured = {}
    monkeypatch.setattr(
        audio_mod, "async_send_command_with_retry",
        _fake_send_factory(captured),
    )

    resp = await audio_mod.manage_audio(
        ctx=DummyContext(), action="get_clip_info",
        target="Assets/Audio/bgm.wav",
    )

    assert resp["success"] is True
    assert captured["params"]["target"] == "Assets/Audio/bgm.wav"


@pytest.mark.asyncio
async def test_get_mixer(monkeypatch):
    """get_mixer sends target."""
    captured = {}
    monkeypatch.setattr(
        audio_mod, "async_send_command_with_retry",
        _fake_send_factory(captured),
    )

    resp = await audio_mod.manage_audio(
        ctx=DummyContext(), action="get_mixer", target="MainMixer",
    )

    assert resp["success"] is True
    assert captured["params"]["target"] == "MainMixer"


@pytest.mark.asyncio
async def test_set_mixer_param(monkeypatch):
    """set_mixer_param sends target, param_name, value."""
    captured = {}
    monkeypatch.setattr(
        audio_mod, "async_send_command_with_retry",
        _fake_send_factory(captured),
    )

    resp = await audio_mod.manage_audio(
        ctx=DummyContext(), action="set_mixer_param",
        target="MainMixer", param_name="MasterVolume", value=-10.0,
    )

    assert resp["success"] is True
    assert captured["params"]["param_name"] == "MasterVolume"
    assert captured["params"]["value"] == -10.0


@pytest.mark.asyncio
async def test_none_params_stripped(monkeypatch):
    """None-valued optional params are not sent."""
    captured = {}
    monkeypatch.setattr(
        audio_mod, "async_send_command_with_retry",
        _fake_send_factory(captured),
    )

    await audio_mod.manage_audio(
        ctx=DummyContext(), action="list_sources",
    )

    assert set(captured["params"].keys()) == {"action"}


@pytest.mark.asyncio
async def test_python_exception_caught(monkeypatch):
    """Python-side exceptions are caught and returned as failure."""

    async def raising_send(cmd, params, **kwargs):
        raise ConnectionError("Unity not connected")

    monkeypatch.setattr(
        audio_mod, "async_send_command_with_retry",
        raising_send,
    )

    resp = await audio_mod.manage_audio(
        ctx=DummyContext(), action="list_sources",
    )

    assert resp["success"] is False
    assert "Unity not connected" in resp["message"]
