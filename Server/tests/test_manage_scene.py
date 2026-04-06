"""Tests for manage_scene tool — multi-scene, templates, validation."""
import asyncio
from types import SimpleNamespace
from unittest.mock import AsyncMock

import pytest

from services.tools.manage_scene import manage_scene

# ── Fixture ──────────────────────────────────────────────────────────


@pytest.fixture
def mock_unity(monkeypatch):
    captured: dict[str, object] = {}

    async def fake_send(send_fn, unity_instance, tool_name, params):
        captured["unity_instance"] = unity_instance
        captured["tool_name"] = tool_name
        captured["params"] = params
        return {"success": True, "message": "ok"}

    monkeypatch.setattr(
        "services.tools.manage_scene.get_unity_instance_from_context",
        AsyncMock(return_value="unity-instance-1"),
    )
    monkeypatch.setattr(
        "services.tools.manage_scene.send_with_unity_instance",
        fake_send,
    )
    monkeypatch.setattr(
        "services.tools.manage_scene.preflight",
        AsyncMock(return_value=None),
    )
    return captured


# ── All actions forward to Unity ─────────────────────────────────────

ALL_ACTIONS = [
    "create", "load", "save", "get_hierarchy",
    "get_active", "get_build_settings", "scene_view_frame",
    "close_scene", "set_active_scene", "get_loaded_scenes",
    "move_to_scene",
    "validate",
]


@pytest.mark.parametrize("action_name", ALL_ACTIONS)
def test_every_action_forwards_to_unity(mock_unity, action_name):
    result = asyncio.run(manage_scene(SimpleNamespace(), action=action_name))
    assert result["success"] is True
    assert mock_unity["params"]["action"] == action_name
    assert mock_unity["tool_name"] == "manage_scene"


# ── Multi-scene param passthrough ────────────────────────────────────


def test_load_additive_passes_flag(mock_unity):
    result = asyncio.run(manage_scene(
        SimpleNamespace(), action="load",
        path="Assets/Scenes/Level2.unity", additive=True,
    ))
    assert result["success"] is True
    assert mock_unity["params"]["additive"] is True


def test_close_scene_passes_scene_name_and_remove(mock_unity):
    result = asyncio.run(manage_scene(
        SimpleNamespace(), action="close_scene",
        scene_name="Level2", remove_scene=True,
    ))
    assert result["success"] is True
    assert mock_unity["params"]["sceneName"] == "Level2"
    assert mock_unity["params"]["removeScene"] is True


def test_move_to_scene_passes_target(mock_unity):
    result = asyncio.run(manage_scene(
        SimpleNamespace(), action="move_to_scene",
        target="Player", scene_name="Level2",
    ))
    assert result["success"] is True
    assert mock_unity["params"]["target"] == "Player"
    assert mock_unity["params"]["sceneName"] == "Level2"


# ── Template param passthrough ───────────────────────────────────────


def test_create_with_template_passes_param(mock_unity):
    result = asyncio.run(manage_scene(
        SimpleNamespace(), action="create",
        name="TestScene", template="3d_basic",
    ))
    assert result["success"] is True
    assert mock_unity["params"]["template"] == "3d_basic"


def test_create_without_template_omits_param(mock_unity):
    result = asyncio.run(manage_scene(
        SimpleNamespace(), action="create",
        name="TestScene",
    ))
    assert result["success"] is True
    assert "template" not in mock_unity["params"]


# ── Validation param passthrough ─────────────────────────────────────


def test_validate_forwards_to_unity(mock_unity):
    result = asyncio.run(manage_scene(SimpleNamespace(), action="validate"))
    assert result["success"] is True
    assert mock_unity["params"]["action"] == "validate"
    assert "autoRepair" not in mock_unity["params"]


def test_validate_with_auto_repair(mock_unity):
    result = asyncio.run(manage_scene(
        SimpleNamespace(), action="validate", auto_repair=True,
    ))
    assert result["success"] is True
    assert mock_unity["params"]["autoRepair"] is True


# ── None params omitted ─────────────────────────────────────────────


def test_none_params_omitted(mock_unity):
    result = asyncio.run(manage_scene(SimpleNamespace(), action="get_loaded_scenes"))
    assert result["success"] is True
    params = mock_unity["params"]
    assert "sceneName" not in params
    assert "scenePath" not in params
    assert "target" not in params
    assert "removeScene" not in params
    assert "additive" not in params
    assert "template" not in params
    assert "autoRepair" not in params
