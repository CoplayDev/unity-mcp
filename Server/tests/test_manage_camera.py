from __future__ import annotations

import asyncio
from types import SimpleNamespace
from unittest.mock import AsyncMock

import pytest

from services.tools.manage_camera import (
    manage_camera,
    ALL_ACTIONS,
    SETUP_ACTIONS,
    CREATION_ACTIONS,
    CONFIGURATION_ACTIONS,
    EXTENSION_ACTIONS,
    CONTROL_ACTIONS,
    CAPTURE_ACTIONS,
)


# ---------------------------------------------------------------------------
# Fixtures
# ---------------------------------------------------------------------------

@pytest.fixture
def mock_unity(monkeypatch):
    """Patch Unity transport layer and return captured call dict."""
    captured: dict[str, object] = {}

    async def fake_send(send_fn, unity_instance, tool_name, params):
        captured["unity_instance"] = unity_instance
        captured["tool_name"] = tool_name
        captured["params"] = params
        return {"success": True, "message": "ok"}

    monkeypatch.setattr(
        "services.tools.manage_camera.get_unity_instance_from_context",
        AsyncMock(return_value="unity-instance-1"),
    )
    monkeypatch.setattr(
        "services.tools.manage_camera.send_with_unity_instance",
        fake_send,
    )
    return captured


# ---------------------------------------------------------------------------
# Action list completeness
# ---------------------------------------------------------------------------

def test_all_actions_is_union_of_sub_lists():
    expected = set(
        SETUP_ACTIONS + CREATION_ACTIONS + CONFIGURATION_ACTIONS
        + EXTENSION_ACTIONS + CONTROL_ACTIONS + CAPTURE_ACTIONS
    )
    assert set(ALL_ACTIONS) == expected


def test_no_duplicate_actions():
    assert len(ALL_ACTIONS) == len(set(ALL_ACTIONS))


def test_all_actions_count():
    assert len(ALL_ACTIONS) == 18


# ---------------------------------------------------------------------------
# Invalid / missing action
# ---------------------------------------------------------------------------

def test_unknown_action_returns_error(mock_unity):
    result = asyncio.run(
        manage_camera(SimpleNamespace(), action="nonexistent_action")
    )
    assert result["success"] is False
    assert "Unknown action" in result["message"]
    assert "tool_name" not in mock_unity


def test_empty_action_returns_error(mock_unity):
    result = asyncio.run(
        manage_camera(SimpleNamespace(), action="")
    )
    assert result["success"] is False


# ---------------------------------------------------------------------------
# Setup actions
# ---------------------------------------------------------------------------

def test_ping_sends_correct_params(mock_unity):
    result = asyncio.run(
        manage_camera(SimpleNamespace(), action="ping")
    )
    assert result["success"] is True
    assert mock_unity["tool_name"] == "manage_camera"
    assert mock_unity["params"]["action"] == "ping"


def test_ensure_brain_sends_correct_params(mock_unity):
    result = asyncio.run(
        manage_camera(
            SimpleNamespace(),
            action="ensure_brain",
            properties={"defaultBlendStyle": "EaseInOut", "defaultBlendDuration": 2.0},
        )
    )
    assert result["success"] is True
    assert mock_unity["params"]["action"] == "ensure_brain"
    assert mock_unity["params"]["properties"]["defaultBlendStyle"] == "EaseInOut"


def test_get_brain_status_sends_correct_params(mock_unity):
    result = asyncio.run(
        manage_camera(SimpleNamespace(), action="get_brain_status")
    )
    assert result["success"] is True
    assert mock_unity["params"]["action"] == "get_brain_status"


# ---------------------------------------------------------------------------
# Camera creation
# ---------------------------------------------------------------------------

def test_create_camera_with_preset(mock_unity):
    result = asyncio.run(
        manage_camera(
            SimpleNamespace(),
            action="create_camera",
            properties={
                "name": "CM ThirdPerson",
                "preset": "third_person",
                "follow": "Player",
                "lookAt": "Player",
                "priority": 10,
            },
        )
    )
    assert result["success"] is True
    assert mock_unity["params"]["action"] == "create_camera"
    props = mock_unity["params"]["properties"]
    assert props["preset"] == "third_person"
    assert props["follow"] == "Player"
    assert props["priority"] == 10


def test_create_camera_minimal(mock_unity):
    result = asyncio.run(
        manage_camera(SimpleNamespace(), action="create_camera")
    )
    assert result["success"] is True
    assert mock_unity["params"]["action"] == "create_camera"


# ---------------------------------------------------------------------------
# Configuration actions
# ---------------------------------------------------------------------------

def test_set_target_sends_follow_and_lookat(mock_unity):
    result = asyncio.run(
        manage_camera(
            SimpleNamespace(),
            action="set_target",
            target="CM Camera",
            properties={"follow": "Player", "lookAt": "Player"},
        )
    )
    assert result["success"] is True
    assert mock_unity["params"]["target"] == "CM Camera"
    assert mock_unity["params"]["properties"]["follow"] == "Player"


def test_set_lens_sends_properties(mock_unity):
    result = asyncio.run(
        manage_camera(
            SimpleNamespace(),
            action="set_lens",
            target="CM Camera",
            properties={"fieldOfView": 40.0, "nearClipPlane": 0.1},
        )
    )
    assert result["success"] is True
    assert mock_unity["params"]["properties"]["fieldOfView"] == 40.0


def test_set_priority_sends_value(mock_unity):
    result = asyncio.run(
        manage_camera(
            SimpleNamespace(),
            action="set_priority",
            target="CM Camera",
            properties={"priority": 20},
        )
    )
    assert result["success"] is True
    assert mock_unity["params"]["properties"]["priority"] == 20


def test_set_body_with_type_swap(mock_unity):
    result = asyncio.run(
        manage_camera(
            SimpleNamespace(),
            action="set_body",
            target="CM Camera",
            properties={"bodyType": "CinemachineFollow", "cameraDistance": 5.0},
        )
    )
    assert result["success"] is True
    assert mock_unity["params"]["properties"]["bodyType"] == "CinemachineFollow"
    assert mock_unity["params"]["properties"]["cameraDistance"] == 5.0


def test_set_aim_with_type_swap(mock_unity):
    result = asyncio.run(
        manage_camera(
            SimpleNamespace(),
            action="set_aim",
            target="CM Camera",
            properties={"aimType": "CinemachineHardLookAt"},
        )
    )
    assert result["success"] is True
    assert mock_unity["params"]["properties"]["aimType"] == "CinemachineHardLookAt"


def test_set_noise_sends_amplitude_frequency(mock_unity):
    result = asyncio.run(
        manage_camera(
            SimpleNamespace(),
            action="set_noise",
            target="CM Camera",
            properties={"amplitudeGain": 0.5, "frequencyGain": 1.0},
        )
    )
    assert result["success"] is True
    assert mock_unity["params"]["properties"]["amplitudeGain"] == 0.5


# ---------------------------------------------------------------------------
# Extension actions
# ---------------------------------------------------------------------------

def test_add_extension_sends_type(mock_unity):
    result = asyncio.run(
        manage_camera(
            SimpleNamespace(),
            action="add_extension",
            target="CM Camera",
            properties={"extensionType": "CinemachineDeoccluder"},
        )
    )
    assert result["success"] is True
    assert mock_unity["params"]["properties"]["extensionType"] == "CinemachineDeoccluder"


def test_remove_extension_sends_type(mock_unity):
    result = asyncio.run(
        manage_camera(
            SimpleNamespace(),
            action="remove_extension",
            target="CM Camera",
            properties={"extensionType": "CinemachineDeoccluder"},
        )
    )
    assert result["success"] is True
    assert mock_unity["params"]["properties"]["extensionType"] == "CinemachineDeoccluder"


# ---------------------------------------------------------------------------
# Control actions
# ---------------------------------------------------------------------------

def test_set_blend_sends_style_and_duration(mock_unity):
    result = asyncio.run(
        manage_camera(
            SimpleNamespace(),
            action="set_blend",
            properties={"style": "EaseInOut", "duration": 2.0},
        )
    )
    assert result["success"] is True
    assert mock_unity["params"]["properties"]["style"] == "EaseInOut"
    assert mock_unity["params"]["properties"]["duration"] == 2.0


def test_force_camera_sends_target(mock_unity):
    result = asyncio.run(
        manage_camera(
            SimpleNamespace(),
            action="force_camera",
            target="CM Cinematic",
        )
    )
    assert result["success"] is True
    assert mock_unity["params"]["target"] == "CM Cinematic"


def test_release_override_sends_no_extra_params(mock_unity):
    result = asyncio.run(
        manage_camera(SimpleNamespace(), action="release_override")
    )
    assert result["success"] is True
    assert mock_unity["params"]["action"] == "release_override"
    assert "target" not in mock_unity["params"]
    assert "properties" not in mock_unity["params"]


def test_list_cameras_sends_no_extra_params(mock_unity):
    result = asyncio.run(
        manage_camera(SimpleNamespace(), action="list_cameras")
    )
    assert result["success"] is True
    assert mock_unity["params"]["action"] == "list_cameras"


# ---------------------------------------------------------------------------
# Capture actions
# ---------------------------------------------------------------------------

def test_screenshot_sends_basic_params(mock_unity):
    result = asyncio.run(
        manage_camera(
            SimpleNamespace(),
            action="screenshot",
            camera="Main Camera",
            include_image=True,
            max_resolution=512,
        )
    )
    assert result["success"] is True
    assert mock_unity["params"]["action"] == "screenshot"
    assert mock_unity["params"]["camera"] == "Main Camera"
    assert mock_unity["params"]["includeImage"] is True
    assert mock_unity["params"]["maxResolution"] == 512


def test_screenshot_with_filename(mock_unity):
    result = asyncio.run(
        manage_camera(
            SimpleNamespace(),
            action="screenshot",
            screenshot_file_name="test-capture",
            screenshot_super_size=2,
        )
    )
    assert result["success"] is True
    assert mock_unity["params"]["fileName"] == "test-capture"
    assert mock_unity["params"]["superSize"] == 2


def test_screenshot_batch_surround(mock_unity):
    result = asyncio.run(
        manage_camera(
            SimpleNamespace(),
            action="screenshot",
            batch="surround",
            view_target="Player",
            include_image=True,
        )
    )
    assert result["success"] is True
    assert mock_unity["params"]["batch"] == "surround"
    assert mock_unity["params"]["viewTarget"] == "Player"


def test_screenshot_batch_orbit(mock_unity):
    result = asyncio.run(
        manage_camera(
            SimpleNamespace(),
            action="screenshot",
            batch="orbit",
            orbit_angles=6,
            orbit_elevations=[0.0, 30.0],
            orbit_distance=10.0,
            orbit_fov=50.0,
        )
    )
    assert result["success"] is True
    assert mock_unity["params"]["batch"] == "orbit"
    assert mock_unity["params"]["orbitAngles"] == 6
    assert mock_unity["params"]["orbitElevations"] == [0.0, 30.0]
    assert mock_unity["params"]["orbitDistance"] == 10.0
    assert mock_unity["params"]["orbitFov"] == 50.0


def test_screenshot_positioned(mock_unity):
    result = asyncio.run(
        manage_camera(
            SimpleNamespace(),
            action="screenshot",
            view_position=[5.0, 3.0, -10.0],
            view_target="Player",
            include_image=True,
        )
    )
    assert result["success"] is True
    assert mock_unity["params"]["viewPosition"] == [5.0, 3.0, -10.0]
    assert mock_unity["params"]["viewTarget"] == "Player"


def test_screenshot_scene_view_capture_params(mock_unity):
    result = asyncio.run(
        manage_camera(
            SimpleNamespace(),
            action="screenshot",
            capture_source="scene_view",
            view_target="Canvas",
            include_image=True,
        )
    )
    assert result["success"] is True
    assert mock_unity["params"]["captureSource"] == "scene_view"
    assert mock_unity["params"]["viewTarget"] == "Canvas"
    assert mock_unity["params"]["includeImage"] is True


def test_screenshot_invalid_capture_source(mock_unity):
    result = asyncio.run(
        manage_camera(
            SimpleNamespace(),
            action="screenshot",
            capture_source="editor_view",
        )
    )
    assert result["success"] is False
    assert "capture_source must be either" in result["message"]
    assert "params" not in mock_unity


def test_screenshot_scene_view_rejects_batch_in_python(mock_unity):
    result = asyncio.run(
        manage_camera(
            SimpleNamespace(),
            action="screenshot",
            capture_source="scene_view",
            batch="surround",
        )
    )
    assert result["success"] is False
    assert "does not support batch modes" in result["message"]
    assert "params" not in mock_unity


def test_screenshot_view_target_works_without_capture_source(mock_unity):
    """view_target should work for both game_view and scene_view (no rejection)."""
    result = asyncio.run(
        manage_camera(
            SimpleNamespace(),
            action="screenshot",
            view_target="Player",
        )
    )
    assert result["success"] is True
    assert mock_unity["params"]["viewTarget"] == "Player"


def test_screenshot_multiview_sends_action(mock_unity):
    result = asyncio.run(
        manage_camera(SimpleNamespace(), action="screenshot_multiview")
    )
    assert result["success"] is True
    assert mock_unity["params"]["action"] == "screenshot_multiview"


def test_screenshot_params_not_sent_for_non_capture_actions(mock_unity):
    """Screenshot params should be ignored for non-capture actions."""
    result = asyncio.run(
        manage_camera(
            SimpleNamespace(),
            action="ping",
            camera="Main Camera",
            include_image=True,
            max_resolution=512,
        )
    )
    assert result["success"] is True
    assert "camera" not in mock_unity["params"]
    assert "includeImage" not in mock_unity["params"]


def test_screenshot_invalid_max_resolution(mock_unity):
    result = asyncio.run(
        manage_camera(
            SimpleNamespace(),
            action="screenshot",
            max_resolution=0,
        )
    )
    assert result["success"] is False
    assert "max_resolution" in result["message"]


def test_screenshot_invalid_orbit_elevations(mock_unity):
    result = asyncio.run(
        manage_camera(
            SimpleNamespace(),
            action="screenshot",
            batch="orbit",
            orbit_elevations="not-json",
        )
    )
    assert result["success"] is False
    assert "orbit_elevations" in result["message"]


# ---------------------------------------------------------------------------
# Parameter handling
# ---------------------------------------------------------------------------

def test_search_method_passed_through(mock_unity):
    result = asyncio.run(
        manage_camera(
            SimpleNamespace(),
            action="set_target",
            target="12345",
            search_method="by_id",
            properties={"follow": "Player"},
        )
    )
    assert result["success"] is True
    assert mock_unity["params"]["searchMethod"] == "by_id"


def test_none_params_omitted(mock_unity):
    result = asyncio.run(
        manage_camera(
            SimpleNamespace(),
            action="ping",
            target=None,
            search_method=None,
            properties=None,
        )
    )
    assert result["success"] is True
    assert "target" not in mock_unity["params"]
    assert "searchMethod" not in mock_unity["params"]
    assert "properties" not in mock_unity["params"]


def test_string_properties_passed_through(mock_unity):
    result = asyncio.run(
        manage_camera(
            SimpleNamespace(),
            action="create_camera",
            properties='{"name": "TestCam", "preset": "follow"}',
        )
    )
    assert result["success"] is True
    assert mock_unity["params"]["properties"] == '{"name": "TestCam", "preset": "follow"}'


def test_non_dict_response_wrapped(monkeypatch):
    """When Unity returns a non-dict, it should be wrapped."""
    monkeypatch.setattr(
        "services.tools.manage_camera.get_unity_instance_from_context",
        AsyncMock(return_value="unity-1"),
    )

    async def fake_send(send_fn, unity_instance, tool_name, params):
        return "unexpected string response"

    monkeypatch.setattr(
        "services.tools.manage_camera.send_with_unity_instance",
        fake_send,
    )

    result = asyncio.run(
        manage_camera(SimpleNamespace(), action="ping")
    )
    assert result["success"] is False
    assert "unexpected string response" in result["message"]


# ---------------------------------------------------------------------------
# Case insensitivity
# ---------------------------------------------------------------------------

def test_action_case_insensitive(mock_unity):
    result = asyncio.run(
        manage_camera(SimpleNamespace(), action="Create_Camera")
    )
    assert result["success"] is True
    assert mock_unity["params"]["action"] == "create_camera"


def test_action_uppercase(mock_unity):
    result = asyncio.run(
        manage_camera(SimpleNamespace(), action="PING")
    )
    assert result["success"] is True
    assert mock_unity["params"]["action"] == "ping"
