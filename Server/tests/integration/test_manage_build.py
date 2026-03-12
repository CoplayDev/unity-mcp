"""
Tests for the manage_build tool.
Validates parameter routing for Unity build pipeline operations.
"""
import pytest

from .test_helpers import DummyContext
import services.tools.manage_build as build_mod


def _fake_send_factory(captured: dict, response: dict = None):
    if response is None:
        response = {"success": True, "message": "OK", "data": {}}

    async def fake_send(cmd, params, **kwargs):
        captured["cmd"] = cmd
        captured["params"] = params
        return response

    return fake_send


@pytest.mark.asyncio
async def test_get_player_settings(monkeypatch):
    captured = {}
    monkeypatch.setattr(build_mod, "async_send_command_with_retry", _fake_send_factory(captured))
    resp = await build_mod.manage_build(ctx=DummyContext(), action="get_player_settings")
    assert resp["success"] is True
    assert captured["params"]["action"] == "get_player_settings"


@pytest.mark.asyncio
async def test_set_player_settings(monkeypatch):
    captured = {}
    monkeypatch.setattr(build_mod, "async_send_command_with_retry", _fake_send_factory(captured))
    resp = await build_mod.manage_build(
        ctx=DummyContext(), action="set_player_settings",
        properties='{"company_name": "TheOne"}',
    )
    assert resp["success"] is True
    assert captured["params"]["properties"] == '{"company_name": "TheOne"}'


@pytest.mark.asyncio
async def test_get_quality_settings(monkeypatch):
    captured = {}
    monkeypatch.setattr(build_mod, "async_send_command_with_retry", _fake_send_factory(captured))
    resp = await build_mod.manage_build(ctx=DummyContext(), action="get_quality_settings")
    assert resp["success"] is True


@pytest.mark.asyncio
async def test_set_quality_level(monkeypatch):
    captured = {}
    monkeypatch.setattr(build_mod, "async_send_command_with_retry", _fake_send_factory(captured))
    resp = await build_mod.manage_build(ctx=DummyContext(), action="set_quality_level", level="High")
    assert resp["success"] is True
    assert captured["params"]["level"] == "High"


@pytest.mark.asyncio
async def test_build_params(monkeypatch):
    captured = {}
    monkeypatch.setattr(build_mod, "async_send_command_with_retry", _fake_send_factory(captured))
    resp = await build_mod.manage_build(
        ctx=DummyContext(), action="build",
        target="StandaloneWindows64", output_path="Builds/game.exe",
    )
    assert resp["success"] is True
    assert captured["params"]["target"] == "StandaloneWindows64"
    assert captured["params"]["output_path"] == "Builds/game.exe"


@pytest.mark.asyncio
async def test_get_scripting_defines(monkeypatch):
    captured = {}
    monkeypatch.setattr(build_mod, "async_send_command_with_retry", _fake_send_factory(captured))
    resp = await build_mod.manage_build(
        ctx=DummyContext(), action="get_scripting_defines", platform="Standalone",
    )
    assert resp["success"] is True
    assert captured["params"]["platform"] == "Standalone"


@pytest.mark.asyncio
async def test_set_scripting_defines(monkeypatch):
    captured = {}
    monkeypatch.setattr(build_mod, "async_send_command_with_retry", _fake_send_factory(captured))
    resp = await build_mod.manage_build(
        ctx=DummyContext(), action="set_scripting_defines",
        platform="Standalone", defines="MY_DEFINE;OTHER",
    )
    assert resp["success"] is True
    assert captured["params"]["defines"] == "MY_DEFINE;OTHER"


@pytest.mark.asyncio
async def test_none_params_stripped(monkeypatch):
    captured = {}
    monkeypatch.setattr(build_mod, "async_send_command_with_retry", _fake_send_factory(captured))
    await build_mod.manage_build(ctx=DummyContext(), action="get_build_settings")
    assert set(captured["params"].keys()) == {"action"}


@pytest.mark.asyncio
async def test_python_exception_caught(monkeypatch):
    async def raising_send(cmd, params, **kwargs):
        raise ConnectionError("Unity not connected")
    monkeypatch.setattr(build_mod, "async_send_command_with_retry", raising_send)
    resp = await build_mod.manage_build(ctx=DummyContext(), action="get_player_settings")
    assert resp["success"] is False
    assert "Unity not connected" in resp["message"]
