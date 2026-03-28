from __future__ import annotations

import asyncio
from types import SimpleNamespace
from unittest.mock import AsyncMock

import pytest

from services.tools.manage_profiler import (
    manage_profiler,
    PROFILER_ACTIONS,
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
        "services.tools.manage_profiler.get_unity_instance_from_context",
        AsyncMock(return_value="unity-instance-1"),
    )
    monkeypatch.setattr(
        "services.tools.manage_profiler.send_with_unity_instance",
        fake_send,
    )
    return captured


# ---------------------------------------------------------------------------
# Action list completeness
# ---------------------------------------------------------------------------

def test_profiler_actions_count():
    assert len(PROFILER_ACTIONS) == 5


def test_no_duplicate_actions():
    assert len(PROFILER_ACTIONS) == len(set(PROFILER_ACTIONS))


def test_expected_actions_present():
    expected = {
        "get_frame_timing",
        "get_script_timing",
        "get_physics_timing",
        "get_gc_alloc",
        "get_animation_timing",
    }
    assert set(PROFILER_ACTIONS) == expected


# ---------------------------------------------------------------------------
# Invalid / missing action
# ---------------------------------------------------------------------------

def test_unknown_action_returns_error(mock_unity):
    result = asyncio.run(
        manage_profiler(SimpleNamespace(), action="nonexistent_action")
    )
    assert result["success"] is False
    assert "Unknown action" in result["message"]
    assert "tool_name" not in mock_unity


def test_empty_action_returns_error(mock_unity):
    result = asyncio.run(
        manage_profiler(SimpleNamespace(), action="")
    )
    assert result["success"] is False
    assert "Unknown action" in result["message"]
    assert "tool_name" not in mock_unity


# ---------------------------------------------------------------------------
# Each action forwards correctly
# ---------------------------------------------------------------------------

@pytest.mark.parametrize("action_name", PROFILER_ACTIONS)
def test_every_action_forwards_to_unity(mock_unity, action_name):
    """Every valid action should be forwarded to Unity without error."""
    result = asyncio.run(
        manage_profiler(SimpleNamespace(), action=action_name)
    )
    assert result["success"] is True
    assert mock_unity["tool_name"] == "manage_profiler"
    assert mock_unity["params"]["action"] == action_name


def test_uses_unity_instance_from_context(mock_unity):
    """manage_profiler should forward the context-derived Unity instance."""
    asyncio.run(
        manage_profiler(SimpleNamespace(), action="get_frame_timing")
    )
    assert mock_unity["unity_instance"] == "unity-instance-1"


def test_get_frame_timing_sends_correct_params(mock_unity):
    result = asyncio.run(
        manage_profiler(SimpleNamespace(), action="get_frame_timing")
    )
    assert result["success"] is True
    assert mock_unity["tool_name"] == "manage_profiler"
    assert mock_unity["params"] == {"action": "get_frame_timing"}


def test_get_script_timing_sends_correct_params(mock_unity):
    result = asyncio.run(
        manage_profiler(SimpleNamespace(), action="get_script_timing")
    )
    assert result["success"] is True
    assert mock_unity["params"] == {"action": "get_script_timing"}


def test_get_physics_timing_sends_correct_params(mock_unity):
    result = asyncio.run(
        manage_profiler(SimpleNamespace(), action="get_physics_timing")
    )
    assert result["success"] is True
    assert mock_unity["params"] == {"action": "get_physics_timing"}


def test_get_gc_alloc_sends_correct_params(mock_unity):
    result = asyncio.run(
        manage_profiler(SimpleNamespace(), action="get_gc_alloc")
    )
    assert result["success"] is True
    assert mock_unity["params"] == {"action": "get_gc_alloc"}


def test_get_animation_timing_sends_correct_params(mock_unity):
    result = asyncio.run(
        manage_profiler(SimpleNamespace(), action="get_animation_timing")
    )
    assert result["success"] is True
    assert mock_unity["params"] == {"action": "get_animation_timing"}


# ---------------------------------------------------------------------------
# Case insensitivity
# ---------------------------------------------------------------------------

def test_action_case_insensitive(mock_unity):
    result = asyncio.run(
        manage_profiler(SimpleNamespace(), action="Get_Frame_Timing")
    )
    assert result["success"] is True
    assert mock_unity["params"]["action"] == "get_frame_timing"


def test_action_uppercase(mock_unity):
    result = asyncio.run(
        manage_profiler(SimpleNamespace(), action="GET_GC_ALLOC")
    )
    assert result["success"] is True
    assert mock_unity["params"]["action"] == "get_gc_alloc"


# ---------------------------------------------------------------------------
# Non-dict response wrapped
# ---------------------------------------------------------------------------

def test_non_dict_response_wrapped(monkeypatch):
    """When Unity returns a non-dict, it should be wrapped."""
    monkeypatch.setattr(
        "services.tools.manage_profiler.get_unity_instance_from_context",
        AsyncMock(return_value="unity-1"),
    )

    async def fake_send(send_fn, unity_instance, tool_name, params):
        return "unexpected string response"

    monkeypatch.setattr(
        "services.tools.manage_profiler.send_with_unity_instance",
        fake_send,
    )

    result = asyncio.run(
        manage_profiler(SimpleNamespace(), action="get_frame_timing")
    )
    assert result["success"] is False
    assert "unexpected string response" in result["message"]


# ---------------------------------------------------------------------------
# Only action param is sent (no extra keys)
# ---------------------------------------------------------------------------

def test_only_action_in_params(mock_unity):
    result = asyncio.run(
        manage_profiler(SimpleNamespace(), action="get_animation_timing")
    )
    assert result["success"] is True
    assert mock_unity["params"] == {"action": "get_animation_timing"}


# ---------------------------------------------------------------------------
# Tool registration
# ---------------------------------------------------------------------------

def test_tool_registered_with_core_group():
    from services.registry.tool_registry import _tool_registry

    profiler_tools = [
        t for t in _tool_registry if t.get("name") == "manage_profiler"
    ]
    assert len(profiler_tools) == 1
    assert profiler_tools[0]["group"] == "core"
