from __future__ import annotations

import asyncio
from types import SimpleNamespace
from unittest.mock import AsyncMock

import pytest

from services.tools.pick_gameobject_from_image import DESCRIPTION, pick_gameobject_from_image


@pytest.fixture
def mock_unity(monkeypatch):
    captured: dict[str, object] = {}

    async def fake_send(send_fn, unity_instance, tool_name, params):
        captured["unity_instance"] = unity_instance
        captured["tool_name"] = tool_name
        captured["params"] = params
        return {"success": True, "message": "ok", "data": {"hit": False}}

    monkeypatch.setattr(
        "services.tools.pick_gameobject_from_image.get_unity_instance_from_context",
        AsyncMock(return_value="unity-instance-1"),
    )
    monkeypatch.setattr(
        "services.tools.pick_gameobject_from_image.send_with_unity_instance",
        fake_send,
    )
    return captured


def test_forwards_core_parameters(mock_unity):
    pick_view = {
        "position": [0, 1, -10],
        "rotation": [0, 0, 0],
        "projection": "perspective",
        "fieldOfView": 60,
        "nearClipPlane": 0.3,
        "farClipPlane": 1000,
        "aspect": 1.777,
        "viewportWidth": 1280,
        "viewportHeight": 720,
    }

    result = asyncio.run(
        pick_gameobject_from_image(
            SimpleNamespace(),
            image_x=320,
            image_y=180,
            image_width=640,
            image_height=360,
            scale_x=2,
            scale_y=2,
            viewport_width=1280,
            viewport_height=720,
            dimension="3d",
            camera="MainCamera",
            pick_view=pick_view,
            layer_mask="Default",
            max_distance=200,
            query_trigger_interaction="Ignore",
        )
    )

    assert result["success"] is True
    assert mock_unity["tool_name"] == "pick_gameobject_from_image"
    params = mock_unity["params"]
    assert params["imageX"] == 320
    assert params["imageY"] == 180
    assert params["imageWidth"] == 640
    assert params["imageHeight"] == 360
    assert params["scaleX"] == 2
    assert params["scaleY"] == 2
    assert params["viewportWidth"] == 1280
    assert params["viewportHeight"] == 720
    assert params["dimension"] == "3d"
    assert params["camera"] == "MainCamera"
    assert params["pickView"] == pick_view
    assert params["layerMask"] == "Default"
    assert params["maxDistance"] == 200
    assert params["queryTriggerInteraction"] == "Ignore"


def test_requires_pick_view_or_camera(mock_unity):
    result = asyncio.run(
        pick_gameobject_from_image(
            SimpleNamespace(),
            image_x=10,
            image_y=10,
            image_width=100,
            image_height=100,
        )
    )

    assert result["success"] is False
    assert "pick_view" in result["message"]
    assert "tool_name" not in mock_unity


@pytest.mark.parametrize(
    ("kwargs", "message"),
    [
        ({"image_width": 0}, "image_width"),
        ({"image_height": -1}, "image_height"),
        ({"image_x": 100}, "image_x"),
        ({"image_y": 100}, "image_y"),
        ({"dimension": "4d"}, "dimension"),
        ({"scale_x": 0}, "scale_x"),
        ({"scale_y": -2}, "scale_y"),
        ({"viewport_width": 0}, "viewport_width"),
        ({"viewport_height": 0}, "viewport_height"),
        ({"max_distance": 0}, "max_distance"),
    ],
)
def test_invalid_values_return_errors(mock_unity, kwargs, message):
    args = {
        "image_x": 10,
        "image_y": 10,
        "image_width": 100,
        "image_height": 100,
        "camera": "MainCamera",
    }
    args.update(kwargs)

    result = asyncio.run(pick_gameobject_from_image(SimpleNamespace(), **args))

    assert result["success"] is False
    assert message in result["message"]
    assert "tool_name" not in mock_unity


def test_pick_view_json_string_is_parsed(mock_unity):
    result = asyncio.run(
        pick_gameobject_from_image(
            SimpleNamespace(),
            image_x=10,
            image_y=10,
            image_width=100,
            image_height=100,
            pick_view='{"position":[0,0,-10],"rotation":[0,0,0],"projection":"perspective","fieldOfView":60}',
        )
    )

    assert result["success"] is True
    assert mock_unity["params"]["pickView"]["position"] == [0, 0, -10]


def test_description_mentions_preconditions_and_unsupported_modes():
    assert "Unity screenshots" in DESCRIPTION
    assert "not arbitrary external images" in DESCRIPTION
    assert "pick_view" in DESCRIPTION
    assert "capture_source='scene_view'" in DESCRIPTION
    assert "mesh-intersection" in DESCRIPTION
    assert "without Colliders" in DESCRIPTION
    assert "culling mask" in DESCRIPTION
    assert "screenshot_multiview" in DESCRIPTION
    assert "batch='surround'" in DESCRIPTION
    assert "batch='orbit'" in DESCRIPTION
    assert "UI GraphicRaycaster" in DESCRIPTION
    assert "Renderer-only objects in non-Scene View" in DESCRIPTION
