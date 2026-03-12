"""
Tests for the manage_camera tool.
Validates parameter routing, action dispatch, and error handling
for Unity camera operations.
"""
import pytest

from .test_helpers import DummyContext
import services.tools.manage_camera as camera_mod


def _fake_send_factory(captured: dict, response: dict = None):
    if response is None:
        response = {"success": True, "message": "OK", "data": {}}

    async def fake_send(cmd, params, **kwargs):
        captured["cmd"] = cmd
        captured["params"] = params
        return response

    return fake_send


@pytest.mark.asyncio
async def test_list_cameras(monkeypatch):
    """list_cameras sends page_size."""
    captured = {}
    monkeypatch.setattr(
        camera_mod, "async_send_command_with_retry",
        _fake_send_factory(captured),
    )

    resp = await camera_mod.manage_camera(
        ctx=DummyContext(), action="list_cameras", page_size=10,
    )

    assert resp["success"] is True
    assert captured["params"]["page_size"] == 10


@pytest.mark.asyncio
async def test_get_camera(monkeypatch):
    """get_camera sends target."""
    captured = {}
    monkeypatch.setattr(
        camera_mod, "async_send_command_with_retry",
        _fake_send_factory(captured),
    )

    resp = await camera_mod.manage_camera(
        ctx=DummyContext(), action="get_camera", target="MainCamera",
    )

    assert resp["success"] is True
    assert captured["params"]["target"] == "MainCamera"


@pytest.mark.asyncio
async def test_set_camera(monkeypatch):
    """set_camera sends target and properties."""
    captured = {}
    monkeypatch.setattr(
        camera_mod, "async_send_command_with_retry",
        _fake_send_factory(captured),
    )

    resp = await camera_mod.manage_camera(
        ctx=DummyContext(), action="set_camera",
        target="MainCamera", properties='{"fov": 90}',
    )

    assert resp["success"] is True
    assert captured["params"]["properties"] == '{"fov": 90}'


@pytest.mark.asyncio
async def test_render_to_file(monkeypatch):
    """render_to_file sends target, path, width, height."""
    captured = {}
    monkeypatch.setattr(
        camera_mod, "async_send_command_with_retry",
        _fake_send_factory(captured),
    )

    resp = await camera_mod.manage_camera(
        ctx=DummyContext(), action="render_to_file",
        target="MainCamera", path="/tmp/render.png",
        width=1280, height=720,
    )

    assert resp["success"] is True
    assert captured["params"]["path"] == "/tmp/render.png"
    assert captured["params"]["width"] == 1280


@pytest.mark.asyncio
async def test_world_to_screen(monkeypatch):
    """world_to_screen sends target and position."""
    captured = {}
    monkeypatch.setattr(
        camera_mod, "async_send_command_with_retry",
        _fake_send_factory(captured),
    )

    resp = await camera_mod.manage_camera(
        ctx=DummyContext(), action="world_to_screen",
        target="MainCamera", position="10,5,3",
    )

    assert resp["success"] is True
    assert captured["params"]["position"] == "10,5,3"


@pytest.mark.asyncio
async def test_screen_to_ray(monkeypatch):
    """screen_to_ray sends target and position."""
    captured = {}
    monkeypatch.setattr(
        camera_mod, "async_send_command_with_retry",
        _fake_send_factory(captured),
    )

    resp = await camera_mod.manage_camera(
        ctx=DummyContext(), action="screen_to_ray",
        target="MainCamera", position="960,540",
    )

    assert resp["success"] is True
    assert captured["params"]["position"] == "960,540"


@pytest.mark.asyncio
async def test_get_main_camera(monkeypatch):
    """get_main_camera routes with no extra params."""
    captured = {}
    monkeypatch.setattr(
        camera_mod, "async_send_command_with_retry",
        _fake_send_factory(captured),
    )

    resp = await camera_mod.manage_camera(
        ctx=DummyContext(), action="get_main_camera",
    )

    assert resp["success"] is True
    assert set(captured["params"].keys()) == {"action"}


@pytest.mark.asyncio
async def test_none_params_stripped(monkeypatch):
    """None-valued optional params are not sent."""
    captured = {}
    monkeypatch.setattr(
        camera_mod, "async_send_command_with_retry",
        _fake_send_factory(captured),
    )

    await camera_mod.manage_camera(
        ctx=DummyContext(), action="list_cameras",
    )

    assert set(captured["params"].keys()) == {"action"}


@pytest.mark.asyncio
async def test_python_exception_caught(monkeypatch):
    """Python-side exceptions are caught and returned as failure."""

    async def raising_send(cmd, params, **kwargs):
        raise ConnectionError("Unity not connected")

    monkeypatch.setattr(
        camera_mod, "async_send_command_with_retry",
        raising_send,
    )

    resp = await camera_mod.manage_camera(
        ctx=DummyContext(), action="list_cameras",
    )

    assert resp["success"] is False
    assert "Unity not connected" in resp["message"]
