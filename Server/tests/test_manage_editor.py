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
    "deploy_package", "restore_package",
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




@pytest.mark.parametrize("width,height", [(2400, 1080), (1080, 2400), (10, 10), (8192, 8192)])
def test_set_game_view_size_forwards_dimensions_and_instance(mock_unity, width, height):
    result = asyncio.run(manage_editor(SimpleNamespace(), action="set_game_view_size", width=width, height=height))
    assert result["success"] is True
    assert mock_unity["unity_instance"] == "unity-instance-1"
    assert mock_unity["params"] == {"action": "set_game_view_size", "width": width, "height": height}


@pytest.mark.parametrize("invalid", [None, 0, -1, 9, 8193, 2**63, True, 1080.5, "1080"])
@pytest.mark.parametrize("dimension", ["width", "height"])
def test_set_game_view_size_rejects_invalid_before_transport(mock_unity, dimension, invalid):
    dimensions = {"width": 2400, "height": 1080, dimension: invalid}
    result = asyncio.run(manage_editor(SimpleNamespace(), action="set_game_view_size", **dimensions))
    assert result["success"] is False
    assert dimension in result["message"]
    assert "params" not in mock_unity


def test_get_game_view_size_forwards_without_dimensions(mock_unity):
    result = asyncio.run(manage_editor(SimpleNamespace(), action="get_game_view_size"))
    assert result["success"] is True
    assert mock_unity["params"] == {"action": "get_game_view_size"}


@pytest.mark.parametrize("action", ["get_game_view_size", "play"])
def test_dimensions_are_not_silently_ignored(mock_unity, action):
    result = asyncio.run(manage_editor(SimpleNamespace(), action=action, width=2400, height=1080))
    assert result["success"] is False
    assert "params" not in mock_unity


@pytest.mark.parametrize("response", [
    {"success": True, "data": {"requested_size": {"width": 2400, "height": 1080}, "render_size": {"width": 1200, "height": 540}, "matches_requested": False}},
    {"success": False, "code": "game_view_size_readback_timeout", "data": {"preset_may_have_changed": True, "settled": False}},
])
def test_size_readback_and_partial_failure_are_preserved(monkeypatch, mock_unity, response):
    monkeypatch.setattr(manage_editor_mod, "send_with_unity_instance", AsyncMock(return_value=response))
    result = asyncio.run(manage_editor(SimpleNamespace(), action="set_game_view_size", width=2400, height=1080))
    assert result["success"] == response["success"]
    assert result["data"] == response["data"]
    if not response["success"]:
        assert result["code"] == response["code"]


def test_dimension_schema_rejects_coercion():
    from typing import get_type_hints
    from pydantic import TypeAdapter, ValidationError

    adapter = TypeAdapter(get_type_hints(manage_editor, include_extras=True)["width"])
    for invalid in [True, 1080.0, "1080", 9, 8193]:
        with pytest.raises(ValidationError):
            adapter.validate_python(invalid)
    assert adapter.validate_python(2400) == 2400
