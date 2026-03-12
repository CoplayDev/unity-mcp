"""
Tests for the manage_navigation tool.
Validates parameter routing for Unity AI Navigation operations.
"""
import pytest

from .test_helpers import DummyContext
import services.tools.manage_navigation as nav_mod


def _fake_send_factory(captured: dict, response: dict = None):
    if response is None:
        response = {"success": True, "message": "OK", "data": {}}

    async def fake_send(cmd, params, **kwargs):
        captured["cmd"] = cmd
        captured["params"] = params
        return response

    return fake_send


@pytest.mark.asyncio
async def test_list_surfaces(monkeypatch):
    captured = {}
    monkeypatch.setattr(nav_mod, "async_send_command_with_retry", _fake_send_factory(captured))
    resp = await nav_mod.manage_navigation(ctx=DummyContext(), action="list_surfaces", page_size=10)
    assert resp["success"] is True
    assert captured["params"]["page_size"] == 10


@pytest.mark.asyncio
async def test_bake(monkeypatch):
    captured = {}
    monkeypatch.setattr(nav_mod, "async_send_command_with_retry", _fake_send_factory(captured))
    resp = await nav_mod.manage_navigation(ctx=DummyContext(), action="bake", target="Ground")
    assert resp["success"] is True
    assert captured["params"]["target"] == "Ground"


@pytest.mark.asyncio
async def test_clear(monkeypatch):
    captured = {}
    monkeypatch.setattr(nav_mod, "async_send_command_with_retry", _fake_send_factory(captured))
    resp = await nav_mod.manage_navigation(ctx=DummyContext(), action="clear", target="Ground")
    assert resp["success"] is True


@pytest.mark.asyncio
async def test_get_agent(monkeypatch):
    captured = {}
    monkeypatch.setattr(nav_mod, "async_send_command_with_retry", _fake_send_factory(captured))
    resp = await nav_mod.manage_navigation(ctx=DummyContext(), action="get_agent", target="Enemy")
    assert resp["success"] is True
    assert captured["params"]["target"] == "Enemy"


@pytest.mark.asyncio
async def test_set_agent_destination(monkeypatch):
    captured = {}
    monkeypatch.setattr(nav_mod, "async_send_command_with_retry", _fake_send_factory(captured))
    resp = await nav_mod.manage_navigation(
        ctx=DummyContext(), action="set_agent_destination",
        target="Enemy", position="10,0,5",
    )
    assert resp["success"] is True
    assert captured["params"]["position"] == "10,0,5"


@pytest.mark.asyncio
async def test_sample_position(monkeypatch):
    captured = {}
    monkeypatch.setattr(nav_mod, "async_send_command_with_retry", _fake_send_factory(captured))
    resp = await nav_mod.manage_navigation(
        ctx=DummyContext(), action="sample_position",
        position="5,0,5", max_distance=10.0,
    )
    assert resp["success"] is True
    assert captured["params"]["max_distance"] == 10.0


@pytest.mark.asyncio
async def test_calculate_path(monkeypatch):
    captured = {}
    monkeypatch.setattr(nav_mod, "async_send_command_with_retry", _fake_send_factory(captured))
    resp = await nav_mod.manage_navigation(
        ctx=DummyContext(), action="calculate_path",
        start="0,0,0", end="10,0,10", area_mask=-1,
    )
    assert resp["success"] is True
    assert captured["params"]["start"] == "0,0,0"
    assert captured["params"]["end"] == "10,0,10"


@pytest.mark.asyncio
async def test_none_params_stripped(monkeypatch):
    captured = {}
    monkeypatch.setattr(nav_mod, "async_send_command_with_retry", _fake_send_factory(captured))
    await nav_mod.manage_navigation(ctx=DummyContext(), action="list_obstacles")
    assert set(captured["params"].keys()) == {"action"}


@pytest.mark.asyncio
async def test_python_exception_caught(monkeypatch):
    async def raising_send(cmd, params, **kwargs):
        raise ConnectionError("Unity not connected")
    monkeypatch.setattr(nav_mod, "async_send_command_with_retry", raising_send)
    resp = await nav_mod.manage_navigation(ctx=DummyContext(), action="list_surfaces")
    assert resp["success"] is False
