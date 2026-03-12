"""
Tests for the manage_physics tool.
Validates parameter routing, action dispatch, and error handling
for classic Unity 3D physics operations.
"""
import pytest

from .test_helpers import DummyContext
import services.tools.manage_physics as physics_mod


def _fake_send_factory(captured: dict, response: dict = None):
    if response is None:
        response = {"success": True, "message": "OK", "data": {}}

    async def fake_send(cmd, params, **kwargs):
        captured["cmd"] = cmd
        captured["params"] = params
        return response

    return fake_send


@pytest.mark.asyncio
async def test_raycast_params(monkeypatch):
    """raycast sends origin, direction, max_distance, layer_mask."""
    captured = {}
    monkeypatch.setattr(
        physics_mod, "async_send_command_with_retry",
        _fake_send_factory(captured, {
            "success": True, "message": "Raycast hit.",
            "data": {"point": "0,0,0", "distance": 5.0},
        }),
    )

    resp = await physics_mod.manage_physics(
        ctx=DummyContext(), action="raycast",
        origin="0,10,0", direction="0,-1,0",
        max_distance=50.0, layer_mask=-1,
    )

    assert resp["success"] is True
    assert captured["cmd"] == "manage_physics"
    assert captured["params"]["origin"] == "0,10,0"
    assert captured["params"]["direction"] == "0,-1,0"
    assert captured["params"]["max_distance"] == 50.0


@pytest.mark.asyncio
async def test_raycast_all_params(monkeypatch):
    """raycast_all routes correctly."""
    captured = {}
    monkeypatch.setattr(
        physics_mod, "async_send_command_with_retry",
        _fake_send_factory(captured),
    )

    resp = await physics_mod.manage_physics(
        ctx=DummyContext(), action="raycast_all",
        origin="0,10,0", direction="0,-1,0",
    )

    assert resp["success"] is True
    assert captured["params"]["action"] == "raycast_all"


@pytest.mark.asyncio
async def test_overlap_sphere_params(monkeypatch):
    """overlap_sphere sends center, radius, layer_mask."""
    captured = {}
    monkeypatch.setattr(
        physics_mod, "async_send_command_with_retry",
        _fake_send_factory(captured),
    )

    resp = await physics_mod.manage_physics(
        ctx=DummyContext(), action="overlap_sphere",
        center="0,0,0", radius=10.0, layer_mask=-1,
    )

    assert resp["success"] is True
    assert captured["params"]["center"] == "0,0,0"
    assert captured["params"]["radius"] == 10.0


@pytest.mark.asyncio
async def test_overlap_box_params(monkeypatch):
    """overlap_box sends center, half_extents."""
    captured = {}
    monkeypatch.setattr(
        physics_mod, "async_send_command_with_retry",
        _fake_send_factory(captured),
    )

    resp = await physics_mod.manage_physics(
        ctx=DummyContext(), action="overlap_box",
        center="0,0,0", half_extents="5,5,5",
    )

    assert resp["success"] is True
    assert captured["params"]["half_extents"] == "5,5,5"


@pytest.mark.asyncio
async def test_list_rigidbodies(monkeypatch):
    """list_rigidbodies sends page_size."""
    captured = {}
    monkeypatch.setattr(
        physics_mod, "async_send_command_with_retry",
        _fake_send_factory(captured),
    )

    resp = await physics_mod.manage_physics(
        ctx=DummyContext(), action="list_rigidbodies", page_size=20,
    )

    assert resp["success"] is True
    assert captured["params"]["page_size"] == 20


@pytest.mark.asyncio
async def test_get_rigidbody(monkeypatch):
    """get_rigidbody sends target."""
    captured = {}
    monkeypatch.setattr(
        physics_mod, "async_send_command_with_retry",
        _fake_send_factory(captured, {
            "success": True, "message": "Rigidbody detail.",
            "data": {"mass": 1.0, "is_kinematic": False},
        }),
    )

    resp = await physics_mod.manage_physics(
        ctx=DummyContext(), action="get_rigidbody", target="Player",
    )

    assert resp["success"] is True
    assert captured["params"]["target"] == "Player"


@pytest.mark.asyncio
async def test_set_rigidbody(monkeypatch):
    """set_rigidbody sends target and properties."""
    captured = {}
    monkeypatch.setattr(
        physics_mod, "async_send_command_with_retry",
        _fake_send_factory(captured),
    )

    resp = await physics_mod.manage_physics(
        ctx=DummyContext(), action="set_rigidbody",
        target="Player", properties='{"mass": 5.0}',
    )

    assert resp["success"] is True
    assert captured["params"]["properties"] == '{"mass": 5.0}'


@pytest.mark.asyncio
async def test_get_physics_settings(monkeypatch):
    """get_physics_settings routes correctly with no extra params."""
    captured = {}
    monkeypatch.setattr(
        physics_mod, "async_send_command_with_retry",
        _fake_send_factory(captured),
    )

    resp = await physics_mod.manage_physics(
        ctx=DummyContext(), action="get_physics_settings",
    )

    assert resp["success"] is True
    assert set(captured["params"].keys()) == {"action"}


@pytest.mark.asyncio
async def test_none_params_stripped(monkeypatch):
    """None-valued optional params are not sent."""
    captured = {}
    monkeypatch.setattr(
        physics_mod, "async_send_command_with_retry",
        _fake_send_factory(captured),
    )

    await physics_mod.manage_physics(
        ctx=DummyContext(), action="list_colliders",
    )

    assert set(captured["params"].keys()) == {"action"}


@pytest.mark.asyncio
async def test_python_exception_caught(monkeypatch):
    """Python-side exceptions are caught and returned as failure."""

    async def raising_send(cmd, params, **kwargs):
        raise ConnectionError("Unity not connected")

    monkeypatch.setattr(
        physics_mod, "async_send_command_with_retry",
        raising_send,
    )

    resp = await physics_mod.manage_physics(
        ctx=DummyContext(), action="raycast",
        origin="0,0,0", direction="0,-1,0",
    )

    assert resp["success"] is False
    assert "Unity not connected" in resp["message"]
