import pytest

from .test_helpers import DummyContext
import services.tools.manage_scene as manage_scene_mod
from services.tools.manage_scene import _extract_images


# ---------------------------------------------------------------------------
# _extract_images unit tests
# ---------------------------------------------------------------------------

def test_extract_images_returns_none_for_non_dict():
    assert _extract_images("not a dict", "screenshot") is None


def test_extract_images_returns_none_for_failed_response():
    assert _extract_images({"success": False}, "screenshot") is None


def test_extract_images_returns_none_when_no_base64():
    resp = {"success": True, "data": {"filePath": "Assets/shot.png"}}
    assert _extract_images(resp, "screenshot") is None


def test_extract_images_screenshot_returns_tool_result():
    resp = {
        "success": True,
        "message": "ok",
        "data": {
            "filePath": "Assets/shot.png",
            "imageBase64": "iVBOR_FAKE_PNG_DATA",
            "imageWidth": 512,
            "imageHeight": 512,
        },
    }
    result = _extract_images(resp, "screenshot")
    assert result is not None
    # Should have TextContent + ImageContent
    assert len(result.content) == 2
    assert result.content[0].type == "text"
    assert result.content[1].type == "image"
    assert result.content[1].data == "iVBOR_FAKE_PNG_DATA"
    assert result.content[1].mimeType == "image/png"
    # Text block should NOT contain base64
    assert "iVBOR_FAKE_PNG_DATA" not in result.content[0].text


def test_extract_images_batch_surround_returns_tool_result():
    resp = {
        "success": True,
        "message": "ok",
        "data": {
            "sceneCenter": [0, 0, 0],
            "sceneRadius": 10.0,
            "screenshots": [
                {"angle": "front", "imageBase64": "FRONT64", "imageWidth": 256, "imageHeight": 256},
                {"angle": "back", "imageBase64": "BACK64", "imageWidth": 256, "imageHeight": 256},
            ],
        },
    }
    result = _extract_images(resp, "screenshot")
    assert result is not None
    # 1 text summary + 2*(label + image) = 5 blocks
    assert len(result.content) == 5
    assert result.content[0].type == "text"
    assert result.content[1].type == "text"  # angle label
    assert result.content[2].type == "image"
    assert result.content[2].data == "FRONT64"
    assert result.content[3].type == "text"  # angle label
    assert result.content[4].type == "image"
    assert result.content[4].data == "BACK64"
    # Text summary should NOT contain base64
    assert "FRONT64" not in result.content[0].text


def test_extract_images_batch_no_screenshots():
    resp = {"success": True, "data": {"screenshots": []}}
    assert _extract_images(resp, "screenshot") is None


def test_extract_images_positioned_returns_tool_result():
    resp = {
        "success": True,
        "message": "ok",
        "data": {
            "imageBase64": "VIEW_B64",
            "imageWidth": 640,
            "imageHeight": 480,
            "viewPosition": [0, 10, -10],
            "lookAt": [0, 0, 0],
        },
    }
    result = _extract_images(resp, "screenshot")
    assert result is not None
    assert len(result.content) == 2
    assert result.content[1].data == "VIEW_B64"


def test_extract_images_unknown_action():
    resp = {"success": True, "data": {"imageBase64": "STUFF"}}
    assert _extract_images(resp, "get_hierarchy") is None


# ---------------------------------------------------------------------------
# manage_scene param pass-through tests
# ---------------------------------------------------------------------------

@pytest.mark.asyncio
async def test_screenshot_camera_and_include_image_params(monkeypatch):
    """New camera, include_image, and max_resolution params are forwarded."""
    captured = {}

    async def fake_send(cmd, params, **kwargs):
        captured["params"] = params
        return {"success": True, "data": {"filePath": "Assets/shot.png"}}

    monkeypatch.setattr(manage_scene_mod, "async_send_command_with_retry", fake_send)

    resp = await manage_scene_mod.manage_scene(
        ctx=DummyContext(),
        action="screenshot",
        camera="MainCamera",
        include_image=True,
        max_resolution=256,
    )

    p = captured["params"]
    assert p["action"] == "screenshot"
    assert p["camera"] == "MainCamera"
    assert p["includeImage"] is True
    assert p["maxResolution"] == 256


@pytest.mark.asyncio
async def test_screenshot_batch_surround_params(monkeypatch):
    """batch='surround' and max_resolution are forwarded."""
    captured = {}

    async def fake_send(cmd, params, **kwargs):
        captured["params"] = params
        return {"success": True, "data": {"screenshots": []}}

    monkeypatch.setattr(manage_scene_mod, "async_send_command_with_retry", fake_send)

    resp = await manage_scene_mod.manage_scene(
        ctx=DummyContext(),
        action="screenshot",
        batch="surround",
        max_resolution=128,
    )

    p = captured["params"]
    assert p["action"] == "screenshot"
    assert p["batch"] == "surround"
    assert p["maxResolution"] == 128


@pytest.mark.asyncio
async def test_screenshot_positioned_params(monkeypatch):
    """look_at and view_position params are forwarded."""
    captured = {}

    async def fake_send(cmd, params, **kwargs):
        captured["params"] = params
        return {"success": True, "data": {}}

    monkeypatch.setattr(manage_scene_mod, "async_send_command_with_retry", fake_send)

    await manage_scene_mod.manage_scene(
        ctx=DummyContext(),
        action="screenshot",
        look_at="Player",
        view_position=[0, 10, -10],
        view_rotation=[45, 0, 0],
        max_resolution=512,
    )

    p = captured["params"]
    assert p["action"] == "screenshot"
    assert p["lookAt"] == "Player"
    assert p["viewPosition"] == [0, 10, -10]
    assert p["viewRotation"] == [45, 0, 0]
    assert p["maxResolution"] == 512


@pytest.mark.asyncio
async def test_screenshot_batch_with_look_at_params(monkeypatch):
    """batch='surround' + look_at centers surround on the target."""
    captured = {}

    async def fake_send(cmd, params, **kwargs):
        captured["params"] = params
        return {"success": True, "data": {"screenshots": []}}

    monkeypatch.setattr(manage_scene_mod, "async_send_command_with_retry", fake_send)

    await manage_scene_mod.manage_scene(
        ctx=DummyContext(),
        action="screenshot",
        batch="surround",
        look_at="Enemy",
        max_resolution=256,
    )

    p = captured["params"]
    assert p["action"] == "screenshot"
    assert p["batch"] == "surround"
    assert p["lookAt"] == "Enemy"


@pytest.mark.asyncio
async def test_scene_view_frame_params(monkeypatch):
    captured = {}

    async def fake_send(cmd, params, **kwargs):
        captured["params"] = params
        return {"success": True, "data": {}}

    monkeypatch.setattr(manage_scene_mod, "async_send_command_with_retry", fake_send)

    await manage_scene_mod.manage_scene(
        ctx=DummyContext(),
        action="scene_view_frame",
        scene_view_target="Player",
    )

    p = captured["params"]
    assert p["action"] == "scene_view_frame"
    assert p["sceneViewTarget"] == "Player"


@pytest.mark.asyncio
async def test_screenshot_returns_tool_result_with_image(monkeypatch):
    """When Unity returns imageBase64, manage_scene should return a ToolResult."""

    async def fake_send(cmd, params, **kwargs):
        return {
            "success": True,
            "message": "ok",
            "data": {
                "filePath": "Assets/shot.png",
                "imageBase64": "FAKE_B64",
                "imageWidth": 256,
                "imageHeight": 256,
            },
        }

    monkeypatch.setattr(manage_scene_mod, "async_send_command_with_retry", fake_send)

    result = await manage_scene_mod.manage_scene(
        ctx=DummyContext(),
        action="screenshot",
        include_image=True,
    )

    from fastmcp.server.server import ToolResult
    assert isinstance(result, ToolResult)
    assert any(getattr(c, "data", None) == "FAKE_B64" for c in result.content)
