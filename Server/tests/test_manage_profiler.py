from __future__ import annotations

import asyncio
from types import SimpleNamespace
from unittest.mock import AsyncMock

import pytest

from services.tools.manage_profiler import (
    manage_profiler,
    ALL_ACTIONS,
    SESSION_ACTIONS,
    COUNTER_ACTIONS,
    MEMORY_SNAPSHOT_ACTIONS,
    FRAME_DEBUGGER_ACTIONS,
    UTILITY_ACTIONS,
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
    assert len(ALL_ACTIONS) == 14


def test_no_duplicate_actions():
    assert len(ALL_ACTIONS) == len(set(ALL_ACTIONS))


def test_session_actions():
    expected = {"profiler_start", "profiler_stop", "profiler_status", "profiler_set_areas"}
    assert set(SESSION_ACTIONS) == expected


def test_counter_actions():
    expected = {"get_frame_timing", "get_counters", "get_object_memory"}
    assert set(COUNTER_ACTIONS) == expected


def test_memory_snapshot_actions():
    expected = {"memory_take_snapshot", "memory_list_snapshots", "memory_compare_snapshots"}
    assert set(MEMORY_SNAPSHOT_ACTIONS) == expected


def test_frame_debugger_actions():
    expected = {"frame_debugger_enable", "frame_debugger_disable", "frame_debugger_get_events"}
    assert set(FRAME_DEBUGGER_ACTIONS) == expected


def test_utility_actions():
    assert UTILITY_ACTIONS == ["ping"]


def test_all_actions_is_union():
    expected = set(UTILITY_ACTIONS + SESSION_ACTIONS + COUNTER_ACTIONS + MEMORY_SNAPSHOT_ACTIONS + FRAME_DEBUGGER_ACTIONS)
    assert set(ALL_ACTIONS) == expected


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

@pytest.mark.parametrize("action_name", [
    "ping",
    "profiler_start", "profiler_stop", "profiler_status", "profiler_set_areas",
    "get_frame_timing", "get_counters", "get_object_memory",
    "memory_take_snapshot", "memory_list_snapshots", "memory_compare_snapshots",
    "frame_debugger_enable", "frame_debugger_disable", "frame_debugger_get_events",
])
def test_every_action_forwards_to_unity(mock_unity, action_name):
    result = asyncio.run(
        manage_profiler(SimpleNamespace(), action=action_name)
    )
    assert result["success"] is True
    assert mock_unity["tool_name"] == "manage_profiler"
    assert mock_unity["params"]["action"] == action_name


def test_uses_unity_instance_from_context(mock_unity):
    asyncio.run(
        manage_profiler(SimpleNamespace(), action="get_frame_timing")
    )
    assert mock_unity["unity_instance"] == "unity-instance-1"


# ---------------------------------------------------------------------------
# Param forwarding
# ---------------------------------------------------------------------------

def test_get_counters_forwards_category(mock_unity):
    result = asyncio.run(
        manage_profiler(SimpleNamespace(), action="get_counters", category="Render")
    )
    assert result["success"] is True
    assert mock_unity["params"]["category"] == "Render"


def test_get_counters_forwards_counter_names(mock_unity):
    result = asyncio.run(
        manage_profiler(
            SimpleNamespace(), action="get_counters",
            category="Render", counters=["Draw Calls Count", "Batches Count"],
        )
    )
    assert result["success"] is True
    assert mock_unity["params"]["counters"] == ["Draw Calls Count", "Batches Count"]


def test_get_counters_omits_none_counters(mock_unity):
    result = asyncio.run(
        manage_profiler(SimpleNamespace(), action="get_counters", category="Memory")
    )
    assert result["success"] is True
    assert "counters" not in mock_unity["params"]


def test_profiler_start_forwards_log_file(mock_unity):
    result = asyncio.run(
        manage_profiler(SimpleNamespace(), action="profiler_start", log_file="/tmp/profile.raw")
    )
    assert result["success"] is True
    assert mock_unity["params"]["log_file"] == "/tmp/profile.raw"


def test_profiler_start_forwards_callstacks(mock_unity):
    result = asyncio.run(
        manage_profiler(SimpleNamespace(), action="profiler_start", enable_callstacks=True)
    )
    assert result["success"] is True
    assert mock_unity["params"]["enable_callstacks"] is True


def test_profiler_set_areas_forwards_areas(mock_unity):
    areas = {"CPU": True, "Audio": False}
    result = asyncio.run(
        manage_profiler(SimpleNamespace(), action="profiler_set_areas", areas=areas)
    )
    assert result["success"] is True
    assert mock_unity["params"]["areas"] == areas


def test_get_object_memory_forwards_path(mock_unity):
    result = asyncio.run(
        manage_profiler(SimpleNamespace(), action="get_object_memory", object_path="/Player/Mesh")
    )
    assert result["success"] is True
    assert mock_unity["params"]["object_path"] == "/Player/Mesh"


def test_memory_take_snapshot_forwards_path(mock_unity):
    result = asyncio.run(
        manage_profiler(SimpleNamespace(), action="memory_take_snapshot", snapshot_path="/tmp/snap.snap")
    )
    assert result["success"] is True
    assert mock_unity["params"]["snapshot_path"] == "/tmp/snap.snap"


def test_memory_compare_forwards_both_paths(mock_unity):
    result = asyncio.run(
        manage_profiler(
            SimpleNamespace(), action="memory_compare_snapshots",
            snapshot_a="/tmp/a.snap", snapshot_b="/tmp/b.snap",
        )
    )
    assert result["success"] is True
    assert mock_unity["params"]["snapshot_a"] == "/tmp/a.snap"
    assert mock_unity["params"]["snapshot_b"] == "/tmp/b.snap"


def test_frame_debugger_get_events_forwards_paging(mock_unity):
    result = asyncio.run(
        manage_profiler(
            SimpleNamespace(), action="frame_debugger_get_events",
            page_size=25, cursor=50,
        )
    )
    assert result["success"] is True
    assert mock_unity["params"]["page_size"] == 25
    assert mock_unity["params"]["cursor"] == 50


def test_action_only_params_no_extras(mock_unity):
    result = asyncio.run(
        manage_profiler(SimpleNamespace(), action="profiler_stop")
    )
    assert result["success"] is True
    assert mock_unity["params"] == {"action": "profiler_stop"}


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
        manage_profiler(SimpleNamespace(), action="PROFILER_STATUS")
    )
    assert result["success"] is True
    assert mock_unity["params"]["action"] == "profiler_status"


# ---------------------------------------------------------------------------
# Non-dict response wrapped
# ---------------------------------------------------------------------------

def test_non_dict_response_wrapped(monkeypatch):
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
# Tool registration
# ---------------------------------------------------------------------------

def test_tool_registered_with_profiling_group():
    from services.registry.tool_registry import _tool_registry

    profiler_tools = [
        t for t in _tool_registry if t.get("name") == "manage_profiler"
    ]
    assert len(profiler_tools) == 1
    assert profiler_tools[0]["group"] == "profiling"
