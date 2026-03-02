"""
Tests for the manage_dots_physics tool.

Validates parameter routing, action dispatch, and error handling
for DOTS Physics debugging operations.
"""
import pytest

from .test_helpers import DummyContext
import services.tools.manage_dots_physics as physics_mod


def _fake_send_factory(captured: dict, response: dict = None):
    if response is None:
        response = {"success": True, "message": "OK", "data": {}}

    async def fake_send(cmd, params, **kwargs):
        captured["cmd"] = cmd
        captured["params"] = params
        return response

    return fake_send


@pytest.mark.asyncio
async def test_get_physics_world(monkeypatch):
    """get_physics_world routes correctly."""
    captured = {}
    monkeypatch.setattr(
        physics_mod, "async_send_command_with_retry",
        _fake_send_factory(captured, {
            "success": True,
            "message": "Physics world info.",
            "data": {"num_bodies": 100, "num_dynamic_bodies": 50},
        }),
    )

    resp = await physics_mod.manage_dots_physics(
        ctx=DummyContext(), action="get_physics_world"
    )

    assert resp["success"] is True
    assert captured["cmd"] == "manage_dots_physics"
    assert captured["params"]["action"] == "get_physics_world"


@pytest.mark.asyncio
async def test_raycast_params(monkeypatch):
    """raycast sends origin, direction, max_distance."""
    captured = {}
    monkeypatch.setattr(
        physics_mod, "async_send_command_with_retry",
        _fake_send_factory(captured, {
            "success": True,
            "message": "Raycast: 2 hits.",
            "data": {"hit_count": 2, "hits": []},
        }),
    )

    resp = await physics_mod.manage_dots_physics(
        ctx=DummyContext(),
        action="raycast",
        origin="0,10,0",
        direction="0,-1,0",
        max_distance=50.0,
    )

    assert resp["success"] is True
    assert captured["params"]["origin"] == "0,10,0"
    assert captured["params"]["direction"] == "0,-1,0"
    assert captured["params"]["max_distance"] == 50.0


@pytest.mark.asyncio
async def test_overlap_aabb_params(monkeypatch):
    """overlap_aabb sends min, max, page_size."""
    captured = {}
    monkeypatch.setattr(
        physics_mod, "async_send_command_with_retry",
        _fake_send_factory(captured),
    )

    resp = await physics_mod.manage_dots_physics(
        ctx=DummyContext(),
        action="overlap_aabb",
        min="-5,-5,-5",
        max="5,5,5",
        page_size=10,
    )

    assert resp["success"] is True
    assert captured["params"]["min"] == "-5,-5,-5"
    assert captured["params"]["max"] == "5,5,5"
    assert captured["params"]["page_size"] == 10


@pytest.mark.asyncio
async def test_list_colliders_params(monkeypatch):
    """list_colliders sends world and page_size."""
    captured = {}
    monkeypatch.setattr(
        physics_mod, "async_send_command_with_retry",
        _fake_send_factory(captured),
    )

    resp = await physics_mod.manage_dots_physics(
        ctx=DummyContext(),
        action="list_colliders",
        world="Server World",
        page_size=50,
    )

    assert resp["success"] is True
    assert captured["params"]["world"] == "Server World"
    assert captured["params"]["page_size"] == 50


@pytest.mark.asyncio
async def test_get_body_params(monkeypatch):
    """get_body sends body_index."""
    captured = {}
    monkeypatch.setattr(
        physics_mod, "async_send_command_with_retry",
        _fake_send_factory(captured, {
            "success": True,
            "message": "Body details.",
            "data": {"body_index": 5, "is_dynamic": True},
        }),
    )

    resp = await physics_mod.manage_dots_physics(
        ctx=DummyContext(),
        action="get_body",
        body_index=5,
    )

    assert resp["success"] is True
    assert captured["params"]["body_index"] == 5


@pytest.mark.asyncio
async def test_none_params_stripped(monkeypatch):
    """None-valued optional params are not sent."""
    captured = {}
    monkeypatch.setattr(
        physics_mod, "async_send_command_with_retry",
        _fake_send_factory(captured),
    )

    await physics_mod.manage_dots_physics(
        ctx=DummyContext(),
        action="get_physics_world",
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

    resp = await physics_mod.manage_dots_physics(
        ctx=DummyContext(),
        action="get_physics_world",
    )

    assert resp["success"] is False
    assert "Unity not connected" in resp["message"]
