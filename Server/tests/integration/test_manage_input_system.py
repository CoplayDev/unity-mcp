"""
Tests for the manage_input_system tool.
Validates parameter routing for Input System operations.
"""
import pytest

from .test_helpers import DummyContext
import services.tools.manage_input_system as input_mod


def _fake_send_factory(captured: dict, response: dict = None):
    if response is None:
        response = {"success": True, "message": "OK", "data": {}}

    async def fake_send(cmd, params, **kwargs):
        captured["cmd"] = cmd
        captured["params"] = params
        return response

    return fake_send


@pytest.mark.asyncio
async def test_list_action_assets(monkeypatch):
    captured = {}
    monkeypatch.setattr(
        input_mod, "async_send_command_with_retry",
        _fake_send_factory(captured, {
            "success": True, "message": "Found 1 InputActionAsset(s).", "data": {"assets": []},
        }),
    )
    resp = await input_mod.manage_input_system(ctx=DummyContext(), action="list_action_assets")
    assert resp["success"] is True
    assert captured["cmd"] == "manage_input_system"
    assert captured["params"]["action"] == "list_action_assets"


@pytest.mark.asyncio
async def test_get_action_map(monkeypatch):
    captured = {}
    monkeypatch.setattr(
        input_mod, "async_send_command_with_retry",
        _fake_send_factory(captured),
    )
    resp = await input_mod.manage_input_system(
        ctx=DummyContext(), action="get_action_map", asset="Assets/Input.inputactions", map_name="Player",
    )
    assert resp["success"] is True
    assert captured["params"]["asset"] == "Assets/Input.inputactions"
    assert captured["params"]["map_name"] == "Player"


@pytest.mark.asyncio
async def test_get_action(monkeypatch):
    captured = {}
    monkeypatch.setattr(
        input_mod, "async_send_command_with_retry",
        _fake_send_factory(captured),
    )
    resp = await input_mod.manage_input_system(
        ctx=DummyContext(), action="get_action", asset="Assets/Input.inputactions", action_name="Jump",
    )
    assert resp["success"] is True
    assert captured["params"]["action_name"] == "Jump"


@pytest.mark.asyncio
async def test_list_devices(monkeypatch):
    captured = {}
    monkeypatch.setattr(
        input_mod, "async_send_command_with_retry",
        _fake_send_factory(captured),
    )
    resp = await input_mod.manage_input_system(ctx=DummyContext(), action="list_devices")
    assert resp["success"] is True
    assert captured["params"]["action"] == "list_devices"


@pytest.mark.asyncio
async def test_get_device(monkeypatch):
    captured = {}
    monkeypatch.setattr(
        input_mod, "async_send_command_with_retry",
        _fake_send_factory(captured),
    )
    resp = await input_mod.manage_input_system(
        ctx=DummyContext(), action="get_device", device_name="Keyboard",
    )
    assert resp["success"] is True
    assert captured["params"]["device_name"] == "Keyboard"


@pytest.mark.asyncio
async def test_list_player_inputs(monkeypatch):
    captured = {}
    monkeypatch.setattr(
        input_mod, "async_send_command_with_retry",
        _fake_send_factory(captured),
    )
    resp = await input_mod.manage_input_system(
        ctx=DummyContext(), action="list_player_inputs", page_size=10,
    )
    assert resp["success"] is True
    assert captured["params"]["page_size"] == 10


@pytest.mark.asyncio
async def test_none_params_stripped(monkeypatch):
    captured = {}
    monkeypatch.setattr(
        input_mod, "async_send_command_with_retry",
        _fake_send_factory(captured),
    )
    await input_mod.manage_input_system(ctx=DummyContext(), action="list_action_assets")
    assert set(captured["params"].keys()) == {"action"}


@pytest.mark.asyncio
async def test_python_exception_caught(monkeypatch):
    async def raising_send(cmd, params, **kwargs):
        raise ConnectionError("Unity not connected")

    monkeypatch.setattr(
        input_mod, "async_send_command_with_retry", raising_send,
    )
    resp = await input_mod.manage_input_system(ctx=DummyContext(), action="list_devices")
    assert resp["success"] is False
    assert "Unity not connected" in resp["message"]
