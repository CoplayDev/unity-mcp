"""Tests for manage_editor tool."""
import asyncio
import inspect
from types import SimpleNamespace
from unittest.mock import AsyncMock

import pytest

from services.tools.manage_editor import manage_editor
import services.tools.manage_editor as manage_editor_mod
from services.registry import get_registered_tools

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
        "services.tools.manage_editor.get_unity_instance_from_context",
        AsyncMock(return_value="unity-instance-1"),
    )
    monkeypatch.setattr(
        "services.tools.manage_editor.send_with_unity_instance",
        fake_send,
    )
    return captured


# ── Undo/Redo ────────────────────────────────────────────────────────


def test_undo_forwards_to_unity(mock_unity):
    result = asyncio.run(manage_editor(SimpleNamespace(), action="undo"))
    assert result["success"] is True
    assert mock_unity["params"]["action"] == "undo"
    assert mock_unity["tool_name"] == "manage_editor"


def test_redo_forwards_to_unity(mock_unity):
    result = asyncio.run(manage_editor(SimpleNamespace(), action="redo"))
    assert result["success"] is True
    assert mock_unity["params"]["action"] == "redo"


# ── All Unity-forwarded actions ──────────────────────────────────────

UNITY_FORWARDED_ACTIONS = [
    "play", "pause", "stop", "set_active_tool",
    "add_tag", "remove_tag", "add_layer", "remove_layer",
    "open_prefab_stage", "save_prefab_stage", "close_prefab_stage", "deploy_package", "restore_package",
    "undo", "redo",
]


@pytest.mark.parametrize("action_name", UNITY_FORWARDED_ACTIONS)
def test_every_action_forwards_to_unity(mock_unity, action_name):
    result = asyncio.run(manage_editor(SimpleNamespace(), action=action_name))
    assert result["success"] is True
    assert mock_unity["params"]["action"] == action_name


# ── Python-only actions ──────────────────────────────────────────────


def test_telemetry_status_handled_python_side(mock_unity):
    result = asyncio.run(manage_editor(SimpleNamespace(), action="telemetry_status"))
    assert result["success"] is True
    assert "telemetry_enabled" in result
    assert "params" not in mock_unity


def test_telemetry_ping_handled_python_side(mock_unity):
    result = asyncio.run(manage_editor(SimpleNamespace(), action="telemetry_ping"))
    assert result["success"] is True
    assert "params" not in mock_unity


# ── None params omitted ─────────────────────────────────────────────


def test_undo_omits_none_params(mock_unity):
    result = asyncio.run(manage_editor(SimpleNamespace(), action="undo"))
    assert result["success"] is True
    params = mock_unity["params"]
    assert "toolName" not in params
    assert "tagName" not in params
    assert "layerName" not in params


# ── open_prefab_stage ────────────────────────────────────────────────


def test_manage_editor_prefab_path_parameters_exist():
    """open_prefab_stage should expose prefab_path plus path alias parameters."""
    sig = inspect.signature(manage_editor_mod.manage_editor)
    assert "prefab_path" in sig.parameters
    assert "path" in sig.parameters
    assert sig.parameters["prefab_path"].default is None
    assert sig.parameters["path"].default is None


def test_manage_editor_description_mentions_open_prefab_stage():
    """The tool description should advertise the new prefab stage action."""
    editor_tool = next(
        (t for t in get_registered_tools() if t["name"] == "manage_editor"), None
    )
    assert editor_tool is not None
    desc = editor_tool.get("description") or editor_tool.get("kwargs", {}).get("description", "")
    assert "open_prefab_stage" in desc


def test_open_prefab_stage_forwards_prefab_path(mock_unity):
    """prefab_path should map to Unity's prefabPath parameter."""
    result = asyncio.run(
        manage_editor(
            SimpleNamespace(),
            action="open_prefab_stage",
            prefab_path="Assets/Prefabs/Test.prefab",
        )
    )
    assert result["success"] is True
    assert mock_unity["params"]["action"] == "open_prefab_stage"
    assert mock_unity["params"]["prefabPath"] == "Assets/Prefabs/Test.prefab"
    assert "path" not in mock_unity["params"]


def test_open_prefab_stage_accepts_path_alias(mock_unity):
    """path should remain available as a compatibility alias."""
    result = asyncio.run(
        manage_editor(
            SimpleNamespace(),
            action="open_prefab_stage",
            path="Assets/Prefabs/Alias.prefab",
        )
    )
    assert result["success"] is True
    assert mock_unity["params"]["action"] == "open_prefab_stage"
    assert mock_unity["params"]["path"] == "Assets/Prefabs/Alias.prefab"
    assert "prefabPath" not in mock_unity["params"]


def test_open_prefab_stage_rejects_conflicting_path_inputs(mock_unity):
    """Conflicting aliases should fail fast before sending a Unity command."""
    result = asyncio.run(
        manage_editor(
            SimpleNamespace(),
            action="open_prefab_stage",
            prefab_path="Assets/Prefabs/Primary.prefab",
            path="Assets/Prefabs/Alias.prefab",
        )
    )
    assert result["success"] is False
    assert "Provide only one of prefab_path or path" in result.get("message", "")


# ── save_prefab_stage ────────────────────────────────────────────────


def test_manage_editor_description_mentions_save_prefab_stage():
    """The tool description should advertise the save_prefab_stage action."""
    editor_tool = next(
        (t for t in get_registered_tools() if t["name"] == "manage_editor"), None
    )
    assert editor_tool is not None
    desc = editor_tool.get("description") or editor_tool.get("kwargs", {}).get("description", "")
    assert "save_prefab_stage" in desc


def test_save_prefab_stage_forwards_to_unity(mock_unity):
    """save_prefab_stage should forward to Unity without extra parameters."""
    result = asyncio.run(manage_editor(SimpleNamespace(), action="save_prefab_stage"))
    assert result["success"] is True
    assert mock_unity["params"]["action"] == "save_prefab_stage"
    assert mock_unity["tool_name"] == "manage_editor"


def test_save_prefab_stage_omits_none_params(mock_unity):
    """save_prefab_stage should not include toolName, tagName, layerName, or path params."""
    asyncio.run(manage_editor(SimpleNamespace(), action="save_prefab_stage"))
    params = mock_unity["params"]
    assert "toolName" not in params
    assert "tagName" not in params
    assert "layerName" not in params
    assert "prefabPath" not in params
    assert "path" not in params
