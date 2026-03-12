"""
Tests for the manage_tilemap tool.
Validates parameter routing for Unity 2D Tilemap operations.
"""
import pytest

from .test_helpers import DummyContext
import services.tools.manage_tilemap as tilemap_mod


def _fake_send_factory(captured: dict, response: dict = None):
    if response is None:
        response = {"success": True, "message": "OK", "data": {}}

    async def fake_send(cmd, params, **kwargs):
        captured["cmd"] = cmd
        captured["params"] = params
        return response

    return fake_send


@pytest.mark.asyncio
async def test_list_tilemaps(monkeypatch):
    captured = {}
    monkeypatch.setattr(
        tilemap_mod, "async_send_command_with_retry",
        _fake_send_factory(captured),
    )
    resp = await tilemap_mod.manage_tilemap(ctx=DummyContext(), action="list_tilemaps")
    assert resp["success"] is True
    assert captured["cmd"] == "manage_tilemap"
    assert captured["params"]["action"] == "list_tilemaps"


@pytest.mark.asyncio
async def test_get_info(monkeypatch):
    captured = {}
    monkeypatch.setattr(
        tilemap_mod, "async_send_command_with_retry",
        _fake_send_factory(captured),
    )
    resp = await tilemap_mod.manage_tilemap(
        ctx=DummyContext(), action="get_info", target="Ground",
    )
    assert resp["success"] is True
    assert captured["params"]["target"] == "Ground"


@pytest.mark.asyncio
async def test_get_tile(monkeypatch):
    captured = {}
    monkeypatch.setattr(
        tilemap_mod, "async_send_command_with_retry",
        _fake_send_factory(captured),
    )
    resp = await tilemap_mod.manage_tilemap(
        ctx=DummyContext(), action="get_tile", target="Ground", position="0,0,0",
    )
    assert resp["success"] is True
    assert captured["params"]["position"] == "0,0,0"


@pytest.mark.asyncio
async def test_set_tile(monkeypatch):
    captured = {}
    monkeypatch.setattr(
        tilemap_mod, "async_send_command_with_retry",
        _fake_send_factory(captured),
    )
    resp = await tilemap_mod.manage_tilemap(
        ctx=DummyContext(), action="set_tile", target="Ground",
        position="1,2,0", tile_asset="Assets/Tiles/Grass.asset",
    )
    assert resp["success"] is True
    assert captured["params"]["tile_asset"] == "Assets/Tiles/Grass.asset"


@pytest.mark.asyncio
async def test_clear_tile(monkeypatch):
    captured = {}
    monkeypatch.setattr(
        tilemap_mod, "async_send_command_with_retry",
        _fake_send_factory(captured),
    )
    resp = await tilemap_mod.manage_tilemap(
        ctx=DummyContext(), action="clear_tile", target="Ground", position="1,2,0",
    )
    assert resp["success"] is True
    assert captured["params"]["action"] == "clear_tile"


@pytest.mark.asyncio
async def test_fill_area(monkeypatch):
    captured = {}
    monkeypatch.setattr(
        tilemap_mod, "async_send_command_with_retry",
        _fake_send_factory(captured),
    )
    resp = await tilemap_mod.manage_tilemap(
        ctx=DummyContext(), action="fill_area", target="Ground",
        min="0,0,0", max="5,5,0", tile_asset="Assets/Tiles/Grass.asset",
    )
    assert resp["success"] is True
    assert captured["params"]["min"] == "0,0,0"
    assert captured["params"]["max"] == "5,5,0"


@pytest.mark.asyncio
async def test_none_params_stripped(monkeypatch):
    captured = {}
    monkeypatch.setattr(
        tilemap_mod, "async_send_command_with_retry",
        _fake_send_factory(captured),
    )
    await tilemap_mod.manage_tilemap(ctx=DummyContext(), action="list_tilemaps")
    assert set(captured["params"].keys()) == {"action"}


@pytest.mark.asyncio
async def test_python_exception_caught(monkeypatch):
    async def raising_send(cmd, params, **kwargs):
        raise ConnectionError("Unity not connected")

    monkeypatch.setattr(
        tilemap_mod, "async_send_command_with_retry", raising_send,
    )
    resp = await tilemap_mod.manage_tilemap(ctx=DummyContext(), action="list_tilemaps")
    assert resp["success"] is False
    assert "Unity not connected" in resp["message"]
