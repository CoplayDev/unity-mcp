"""
Tests for the manage_cinemachine tool.
Validates parameter routing for Cinemachine operations.
"""
import pytest

from .test_helpers import DummyContext
import services.tools.manage_cinemachine as cine_mod


def _fake_send_factory(captured: dict, response: dict = None):
    if response is None:
        response = {"success": True, "message": "OK", "data": {}}

    async def fake_send(cmd, params, **kwargs):
        captured["cmd"] = cmd
        captured["params"] = params
        return response

    return fake_send


@pytest.mark.asyncio
async def test_list_vcams(monkeypatch):
    captured = {}
    monkeypatch.setattr(
        cine_mod, "async_send_command_with_retry",
        _fake_send_factory(captured, {
            "success": True, "message": "Found 3 CinemachineCamera(s).", "data": {"items": []},
        }),
    )
    resp = await cine_mod.manage_cinemachine(ctx=DummyContext(), action="list_vcams")
    assert resp["success"] is True
    assert captured["cmd"] == "manage_cinemachine"
    assert captured["params"]["action"] == "list_vcams"


@pytest.mark.asyncio
async def test_get_vcam(monkeypatch):
    captured = {}
    monkeypatch.setattr(
        cine_mod, "async_send_command_with_retry",
        _fake_send_factory(captured),
    )
    resp = await cine_mod.manage_cinemachine(
        ctx=DummyContext(), action="get_vcam", target="FollowCam",
    )
    assert resp["success"] is True
    assert captured["params"]["target"] == "FollowCam"


@pytest.mark.asyncio
async def test_set_vcam(monkeypatch):
    captured = {}
    monkeypatch.setattr(
        cine_mod, "async_send_command_with_retry",
        _fake_send_factory(captured),
    )
    resp = await cine_mod.manage_cinemachine(
        ctx=DummyContext(), action="set_vcam", target="FollowCam",
        properties='{"priority": 20, "enabled": true}',
    )
    assert resp["success"] is True
    assert captured["params"]["properties"] == '{"priority": 20, "enabled": true}'


@pytest.mark.asyncio
async def test_get_brain(monkeypatch):
    captured = {}
    monkeypatch.setattr(
        cine_mod, "async_send_command_with_retry",
        _fake_send_factory(captured),
    )
    resp = await cine_mod.manage_cinemachine(ctx=DummyContext(), action="get_brain")
    assert resp["success"] is True
    assert captured["params"]["action"] == "get_brain"


@pytest.mark.asyncio
async def test_set_priority(monkeypatch):
    captured = {}
    monkeypatch.setattr(
        cine_mod, "async_send_command_with_retry",
        _fake_send_factory(captured),
    )
    resp = await cine_mod.manage_cinemachine(
        ctx=DummyContext(), action="set_priority", target="FollowCam", priority=15,
    )
    assert resp["success"] is True
    assert captured["params"]["priority"] == 15


@pytest.mark.asyncio
async def test_list_blends(monkeypatch):
    captured = {}
    monkeypatch.setattr(
        cine_mod, "async_send_command_with_retry",
        _fake_send_factory(captured),
    )
    resp = await cine_mod.manage_cinemachine(ctx=DummyContext(), action="list_blends")
    assert resp["success"] is True
    assert captured["params"]["action"] == "list_blends"


@pytest.mark.asyncio
async def test_none_params_stripped(monkeypatch):
    captured = {}
    monkeypatch.setattr(
        cine_mod, "async_send_command_with_retry",
        _fake_send_factory(captured),
    )
    await cine_mod.manage_cinemachine(ctx=DummyContext(), action="list_vcams")
    assert set(captured["params"].keys()) == {"action"}


@pytest.mark.asyncio
async def test_python_exception_caught(monkeypatch):
    async def raising_send(cmd, params, **kwargs):
        raise ConnectionError("Unity not connected")

    monkeypatch.setattr(
        cine_mod, "async_send_command_with_retry", raising_send,
    )
    resp = await cine_mod.manage_cinemachine(ctx=DummyContext(), action="list_vcams")
    assert resp["success"] is False
    assert "Unity not connected" in resp["message"]
