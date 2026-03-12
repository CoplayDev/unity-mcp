"""
Tests for the manage_physics2d tool.
Validates parameter routing for Unity 2D physics operations.
"""
import pytest

from .test_helpers import DummyContext
import services.tools.manage_physics2d as physics2d_mod


def _fake_send_factory(captured: dict, response: dict = None):
    if response is None:
        response = {"success": True, "message": "OK", "data": {}}

    async def fake_send(cmd, params, **kwargs):
        captured["cmd"] = cmd
        captured["params"] = params
        return response

    return fake_send


@pytest.mark.asyncio
async def test_raycast(monkeypatch):
    captured = {}
    monkeypatch.setattr(
        physics2d_mod, "async_send_command_with_retry",
        _fake_send_factory(captured, {
            "success": True, "message": "2D Raycast hit.", "data": {"hit": True},
        }),
    )
    resp = await physics2d_mod.manage_physics2d(
        ctx=DummyContext(), action="raycast", origin="0,10", direction="0,-1", max_distance=50.0,
    )
    assert resp["success"] is True
    assert captured["cmd"] == "manage_physics2d"
    assert captured["params"]["origin"] == "0,10"
    assert captured["params"]["direction"] == "0,-1"


@pytest.mark.asyncio
async def test_raycast_all(monkeypatch):
    captured = {}
    monkeypatch.setattr(
        physics2d_mod, "async_send_command_with_retry",
        _fake_send_factory(captured),
    )
    resp = await physics2d_mod.manage_physics2d(
        ctx=DummyContext(), action="raycast_all", origin="0,0", direction="1,0",
    )
    assert resp["success"] is True
    assert captured["params"]["action"] == "raycast_all"


@pytest.mark.asyncio
async def test_overlap_circle(monkeypatch):
    captured = {}
    monkeypatch.setattr(
        physics2d_mod, "async_send_command_with_retry",
        _fake_send_factory(captured),
    )
    resp = await physics2d_mod.manage_physics2d(
        ctx=DummyContext(), action="overlap_circle", center="0,0", radius=5.0,
    )
    assert resp["success"] is True
    assert captured["params"]["center"] == "0,0"
    assert captured["params"]["radius"] == 5.0


@pytest.mark.asyncio
async def test_overlap_box(monkeypatch):
    captured = {}
    monkeypatch.setattr(
        physics2d_mod, "async_send_command_with_retry",
        _fake_send_factory(captured),
    )
    resp = await physics2d_mod.manage_physics2d(
        ctx=DummyContext(), action="overlap_box", center="0,0", size="10,10", angle=45.0,
    )
    assert resp["success"] is True
    assert captured["params"]["size"] == "10,10"
    assert captured["params"]["angle"] == 45.0


@pytest.mark.asyncio
async def test_list_rigidbodies(monkeypatch):
    captured = {}
    monkeypatch.setattr(
        physics2d_mod, "async_send_command_with_retry",
        _fake_send_factory(captured),
    )
    resp = await physics2d_mod.manage_physics2d(
        ctx=DummyContext(), action="list_rigidbodies", page_size=20,
    )
    assert resp["success"] is True
    assert captured["params"]["page_size"] == 20


@pytest.mark.asyncio
async def test_get_rigidbody(monkeypatch):
    captured = {}
    monkeypatch.setattr(
        physics2d_mod, "async_send_command_with_retry",
        _fake_send_factory(captured),
    )
    resp = await physics2d_mod.manage_physics2d(
        ctx=DummyContext(), action="get_rigidbody", target="Player2D",
    )
    assert resp["success"] is True
    assert captured["params"]["target"] == "Player2D"


@pytest.mark.asyncio
async def test_list_colliders(monkeypatch):
    captured = {}
    monkeypatch.setattr(
        physics2d_mod, "async_send_command_with_retry",
        _fake_send_factory(captured),
    )
    resp = await physics2d_mod.manage_physics2d(
        ctx=DummyContext(), action="list_colliders",
    )
    assert resp["success"] is True
    assert captured["params"]["action"] == "list_colliders"


@pytest.mark.asyncio
async def test_get_physics2d_settings(monkeypatch):
    captured = {}
    monkeypatch.setattr(
        physics2d_mod, "async_send_command_with_retry",
        _fake_send_factory(captured),
    )
    resp = await physics2d_mod.manage_physics2d(
        ctx=DummyContext(), action="get_physics2d_settings",
    )
    assert resp["success"] is True
    assert captured["params"]["action"] == "get_physics2d_settings"


@pytest.mark.asyncio
async def test_none_params_stripped(monkeypatch):
    captured = {}
    monkeypatch.setattr(
        physics2d_mod, "async_send_command_with_retry",
        _fake_send_factory(captured),
    )
    await physics2d_mod.manage_physics2d(ctx=DummyContext(), action="list_colliders")
    assert set(captured["params"].keys()) == {"action"}


@pytest.mark.asyncio
async def test_python_exception_caught(monkeypatch):
    async def raising_send(cmd, params, **kwargs):
        raise ConnectionError("Unity not connected")

    monkeypatch.setattr(
        physics2d_mod, "async_send_command_with_retry", raising_send,
    )
    resp = await physics2d_mod.manage_physics2d(ctx=DummyContext(), action="list_colliders")
    assert resp["success"] is False
    assert "Unity not connected" in resp["message"]
