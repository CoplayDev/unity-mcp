"""
Tests for the manage_dots_graphics tool.

Validates parameter routing, action dispatch, and error handling
for DOTS Graphics rendering operations.
"""
import pytest

from .test_helpers import DummyContext
import services.tools.manage_dots_graphics as graphics_mod


def _fake_send_factory(captured: dict, response: dict = None):
    if response is None:
        response = {"success": True, "message": "OK", "data": {}}

    async def fake_send(cmd, params, **kwargs):
        captured["cmd"] = cmd
        captured["params"] = params
        return response

    return fake_send


@pytest.mark.asyncio
async def test_get_render_stats(monkeypatch):
    """get_render_stats routes correctly."""
    captured = {}
    monkeypatch.setattr(
        graphics_mod, "async_send_command_with_retry",
        _fake_send_factory(captured, {
            "success": True,
            "message": "Entities Graphics stats.",
            "data": {"entities_with_material_mesh": 200},
        }),
    )

    resp = await graphics_mod.manage_dots_graphics(
        ctx=DummyContext(), action="get_render_stats"
    )

    assert resp["success"] is True
    assert captured["cmd"] == "manage_dots_graphics"
    assert captured["params"]["action"] == "get_render_stats"


@pytest.mark.asyncio
async def test_list_rendered_entities_params(monkeypatch):
    """list_rendered_entities sends page_size."""
    captured = {}
    monkeypatch.setattr(
        graphics_mod, "async_send_command_with_retry",
        _fake_send_factory(captured),
    )

    resp = await graphics_mod.manage_dots_graphics(
        ctx=DummyContext(),
        action="list_rendered_entities",
        page_size=50,
    )

    assert resp["success"] is True
    assert captured["params"]["page_size"] == 50


@pytest.mark.asyncio
async def test_get_entity_rendering_params(monkeypatch):
    """get_entity_rendering sends entity_index."""
    captured = {}
    monkeypatch.setattr(
        graphics_mod, "async_send_command_with_retry",
        _fake_send_factory(captured, {
            "success": True,
            "message": "Rendering info.",
            "data": {"entity_index": 42, "material_id": 123},
        }),
    )

    resp = await graphics_mod.manage_dots_graphics(
        ctx=DummyContext(),
        action="get_entity_rendering",
        entity_index=42,
    )

    assert resp["success"] is True
    assert captured["params"]["entity_index"] == 42


@pytest.mark.asyncio
async def test_list_registered_materials_params(monkeypatch):
    """list_registered_materials sends world and page_size."""
    captured = {}
    monkeypatch.setattr(
        graphics_mod, "async_send_command_with_retry",
        _fake_send_factory(captured),
    )

    resp = await graphics_mod.manage_dots_graphics(
        ctx=DummyContext(),
        action="list_registered_materials",
        world="Server World",
        page_size=30,
    )

    assert resp["success"] is True
    assert captured["params"]["world"] == "Server World"
    assert captured["params"]["page_size"] == 30


@pytest.mark.asyncio
async def test_list_registered_meshes_params(monkeypatch):
    """list_registered_meshes sends page_size."""
    captured = {}
    monkeypatch.setattr(
        graphics_mod, "async_send_command_with_retry",
        _fake_send_factory(captured),
    )

    resp = await graphics_mod.manage_dots_graphics(
        ctx=DummyContext(),
        action="list_registered_meshes",
        page_size=10,
    )

    assert resp["success"] is True
    assert captured["params"]["page_size"] == 10


@pytest.mark.asyncio
async def test_none_params_stripped(monkeypatch):
    """None-valued optional params are not sent."""
    captured = {}
    monkeypatch.setattr(
        graphics_mod, "async_send_command_with_retry",
        _fake_send_factory(captured),
    )

    await graphics_mod.manage_dots_graphics(
        ctx=DummyContext(),
        action="get_render_stats",
    )

    assert set(captured["params"].keys()) == {"action"}


@pytest.mark.asyncio
async def test_python_exception_caught(monkeypatch):
    """Python-side exceptions are caught and returned as failure."""

    async def raising_send(cmd, params, **kwargs):
        raise ConnectionError("Unity not connected")

    monkeypatch.setattr(
        graphics_mod, "async_send_command_with_retry",
        raising_send,
    )

    resp = await graphics_mod.manage_dots_graphics(
        ctx=DummyContext(),
        action="get_render_stats",
    )

    assert resp["success"] is False
    assert "Unity not connected" in resp["message"]
