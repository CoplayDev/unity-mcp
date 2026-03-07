from __future__ import annotations

import asyncio
from types import SimpleNamespace
from unittest.mock import AsyncMock

import pytest

from services.tools.manage_graphics import (
    manage_graphics,
    ALL_ACTIONS,
    VOLUME_ACTIONS,
    BAKE_ACTIONS,
    STATS_ACTIONS,
    PIPELINE_ACTIONS,
    FEATURE_ACTIONS,
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
        "services.tools.manage_graphics.get_unity_instance_from_context",
        AsyncMock(return_value="unity-instance-1"),
    )
    monkeypatch.setattr(
        "services.tools.manage_graphics.send_with_unity_instance",
        fake_send,
    )
    return captured


# ---------------------------------------------------------------------------
# Action list completeness
# ---------------------------------------------------------------------------

def test_all_actions_is_union_of_sub_lists():
    expected = set(
        ["ping"] + VOLUME_ACTIONS + BAKE_ACTIONS + STATS_ACTIONS
        + PIPELINE_ACTIONS + FEATURE_ACTIONS
    )
    assert set(ALL_ACTIONS) == expected


def test_no_duplicate_actions():
    assert len(ALL_ACTIONS) == len(set(ALL_ACTIONS))


def test_all_actions_count():
    assert len(ALL_ACTIONS) == 33


def test_volume_actions_count():
    assert len(VOLUME_ACTIONS) == 8


def test_bake_actions_count():
    assert len(BAKE_ACTIONS) == 10


def test_stats_actions_count():
    assert len(STATS_ACTIONS) == 4


def test_pipeline_actions_count():
    assert len(PIPELINE_ACTIONS) == 4


def test_feature_actions_count():
    assert len(FEATURE_ACTIONS) == 6


# ---------------------------------------------------------------------------
# Invalid / missing action
# ---------------------------------------------------------------------------

def test_unknown_action_returns_error(mock_unity):
    result = asyncio.run(
        manage_graphics(SimpleNamespace(), action="nonexistent_action")
    )
    assert result["success"] is False
    assert "Unknown action" in result["message"]
    assert "tool_name" not in mock_unity


def test_empty_action_returns_error(mock_unity):
    result = asyncio.run(
        manage_graphics(SimpleNamespace(), action="")
    )
    assert result["success"] is False


# ---------------------------------------------------------------------------
# Ping
# ---------------------------------------------------------------------------

def test_ping_sends_correct_params(mock_unity):
    result = asyncio.run(
        manage_graphics(SimpleNamespace(), action="ping")
    )
    assert result["success"] is True
    assert mock_unity["tool_name"] == "manage_graphics"
    assert mock_unity["params"]["action"] == "ping"


# ---------------------------------------------------------------------------
# Volume actions
# ---------------------------------------------------------------------------

def test_volume_create_with_all_params(mock_unity):
    result = asyncio.run(
        manage_graphics(
            SimpleNamespace(),
            action="volume_create",
            name="PostProcess Volume",
            is_global=True,
            weight=0.8,
            priority=1.0,
            profile_path="Assets/Profiles/MyProfile.asset",
            effects=[
                {"type": "Bloom", "parameters": {"intensity": 1.5}},
                {"type": "Vignette", "parameters": {"intensity": 0.3}},
            ],
        )
    )
    assert result["success"] is True
    assert mock_unity["params"]["action"] == "volume_create"
    assert mock_unity["params"]["name"] == "PostProcess Volume"
    assert mock_unity["params"]["is_global"] is True
    assert mock_unity["params"]["weight"] == 0.8
    assert mock_unity["params"]["priority"] == 1.0
    assert mock_unity["params"]["profile_path"] == "Assets/Profiles/MyProfile.asset"
    assert len(mock_unity["params"]["effects"]) == 2


def test_volume_create_minimal(mock_unity):
    result = asyncio.run(
        manage_graphics(SimpleNamespace(), action="volume_create")
    )
    assert result["success"] is True
    assert mock_unity["params"]["action"] == "volume_create"
    assert "name" not in mock_unity["params"]
    assert "effects" not in mock_unity["params"]


def test_volume_add_effect_sends_target_and_effect(mock_unity):
    result = asyncio.run(
        manage_graphics(
            SimpleNamespace(),
            action="volume_add_effect",
            target="PostProcess Volume",
            effect="Bloom",
            parameters={"intensity": 2.0, "threshold": 0.9},
        )
    )
    assert result["success"] is True
    assert mock_unity["params"]["action"] == "volume_add_effect"
    assert mock_unity["params"]["target"] == "PostProcess Volume"
    assert mock_unity["params"]["effect"] == "Bloom"
    assert mock_unity["params"]["parameters"]["intensity"] == 2.0


def test_volume_set_effect_sends_properties(mock_unity):
    result = asyncio.run(
        manage_graphics(
            SimpleNamespace(),
            action="volume_set_effect",
            target="PostProcess Volume",
            effect="Bloom",
            parameters={"intensity": 3.0},
        )
    )
    assert result["success"] is True
    assert mock_unity["params"]["action"] == "volume_set_effect"
    assert mock_unity["params"]["effect"] == "Bloom"
    assert mock_unity["params"]["parameters"]["intensity"] == 3.0


def test_volume_remove_effect_sends_target_and_effect(mock_unity):
    result = asyncio.run(
        manage_graphics(
            SimpleNamespace(),
            action="volume_remove_effect",
            target="PostProcess Volume",
            effect="Vignette",
        )
    )
    assert result["success"] is True
    assert mock_unity["params"]["action"] == "volume_remove_effect"
    assert mock_unity["params"]["effect"] == "Vignette"


def test_volume_get_info_sends_target(mock_unity):
    result = asyncio.run(
        manage_graphics(
            SimpleNamespace(),
            action="volume_get_info",
            target="PostProcess Volume",
        )
    )
    assert result["success"] is True
    assert mock_unity["params"]["action"] == "volume_get_info"
    assert mock_unity["params"]["target"] == "PostProcess Volume"


def test_volume_set_properties_sends_properties(mock_unity):
    result = asyncio.run(
        manage_graphics(
            SimpleNamespace(),
            action="volume_set_properties",
            target="PostProcess Volume",
            properties={"weight": 0.5, "priority": 2.0, "isGlobal": False},
        )
    )
    assert result["success"] is True
    assert mock_unity["params"]["action"] == "volume_set_properties"
    assert mock_unity["params"]["properties"]["weight"] == 0.5


def test_volume_list_effects_sends_target(mock_unity):
    result = asyncio.run(
        manage_graphics(
            SimpleNamespace(),
            action="volume_list_effects",
            target="PostProcess Volume",
        )
    )
    assert result["success"] is True
    assert mock_unity["params"]["action"] == "volume_list_effects"


def test_volume_create_profile_sends_path(mock_unity):
    result = asyncio.run(
        manage_graphics(
            SimpleNamespace(),
            action="volume_create_profile",
            path="Assets/Profiles/NewProfile.asset",
            effects=[{"type": "Bloom"}],
        )
    )
    assert result["success"] is True
    assert mock_unity["params"]["action"] == "volume_create_profile"
    assert mock_unity["params"]["path"] == "Assets/Profiles/NewProfile.asset"
    assert len(mock_unity["params"]["effects"]) == 1


# ---------------------------------------------------------------------------
# Bake actions
# ---------------------------------------------------------------------------

def test_bake_start_sends_async_flag(mock_unity):
    result = asyncio.run(
        manage_graphics(
            SimpleNamespace(),
            action="bake_start",
            async_bake=True,
        )
    )
    assert result["success"] is True
    assert mock_unity["params"]["action"] == "bake_start"
    assert mock_unity["params"]["async"] is True


def test_bake_start_sync(mock_unity):
    result = asyncio.run(
        manage_graphics(
            SimpleNamespace(),
            action="bake_start",
            async_bake=False,
        )
    )
    assert result["success"] is True
    assert mock_unity["params"]["async"] is False


def test_bake_cancel_sends_no_extra_params(mock_unity):
    result = asyncio.run(
        manage_graphics(SimpleNamespace(), action="bake_cancel")
    )
    assert result["success"] is True
    assert mock_unity["params"]["action"] == "bake_cancel"
    assert "target" not in mock_unity["params"]


def test_bake_status_sends_action(mock_unity):
    result = asyncio.run(
        manage_graphics(SimpleNamespace(), action="bake_status")
    )
    assert result["success"] is True
    assert mock_unity["params"]["action"] == "bake_status"


def test_bake_clear_sends_action(mock_unity):
    result = asyncio.run(
        manage_graphics(SimpleNamespace(), action="bake_clear")
    )
    assert result["success"] is True
    assert mock_unity["params"]["action"] == "bake_clear"


def test_bake_reflection_probe_sends_target(mock_unity):
    result = asyncio.run(
        manage_graphics(
            SimpleNamespace(),
            action="bake_reflection_probe",
            target="ReflectionProbe1",
        )
    )
    assert result["success"] is True
    assert mock_unity["params"]["action"] == "bake_reflection_probe"
    assert mock_unity["params"]["target"] == "ReflectionProbe1"


def test_bake_get_settings_sends_action(mock_unity):
    result = asyncio.run(
        manage_graphics(SimpleNamespace(), action="bake_get_settings")
    )
    assert result["success"] is True
    assert mock_unity["params"]["action"] == "bake_get_settings"


def test_bake_set_settings_sends_settings(mock_unity):
    result = asyncio.run(
        manage_graphics(
            SimpleNamespace(),
            action="bake_set_settings",
            settings={"lightmapResolution": 40, "bounces": 3},
        )
    )
    assert result["success"] is True
    assert mock_unity["params"]["action"] == "bake_set_settings"
    assert mock_unity["params"]["settings"]["lightmapResolution"] == 40
    assert mock_unity["params"]["settings"]["bounces"] == 3


def test_bake_create_light_probe_group_sends_params(mock_unity):
    result = asyncio.run(
        manage_graphics(
            SimpleNamespace(),
            action="bake_create_light_probe_group",
            name="LightProbes",
            position=[0.0, 1.0, 0.0],
            grid_size=[3, 3, 3],
            spacing=2.5,
        )
    )
    assert result["success"] is True
    assert mock_unity["params"]["action"] == "bake_create_light_probe_group"
    assert mock_unity["params"]["name"] == "LightProbes"
    assert mock_unity["params"]["position"] == [0.0, 1.0, 0.0]
    assert mock_unity["params"]["grid_size"] == [3, 3, 3]
    assert mock_unity["params"]["spacing"] == 2.5


def test_bake_create_reflection_probe_sends_params(mock_unity):
    result = asyncio.run(
        manage_graphics(
            SimpleNamespace(),
            action="bake_create_reflection_probe",
            name="Probe1",
            position=[5.0, 2.0, -3.0],
            size=[10.0, 10.0, 10.0],
            resolution=256,
            mode="Baked",
            hdr=True,
            box_projection=True,
        )
    )
    assert result["success"] is True
    assert mock_unity["params"]["action"] == "bake_create_reflection_probe"
    assert mock_unity["params"]["name"] == "Probe1"
    assert mock_unity["params"]["position"] == [5.0, 2.0, -3.0]
    assert mock_unity["params"]["size"] == [10.0, 10.0, 10.0]
    assert mock_unity["params"]["resolution"] == 256
    assert mock_unity["params"]["mode"] == "Baked"
    assert mock_unity["params"]["hdr"] is True
    assert mock_unity["params"]["box_projection"] is True


def test_bake_set_probe_positions_sends_positions(mock_unity):
    positions = [[0.0, 0.0, 0.0], [1.0, 2.0, 3.0], [5.0, 5.0, 5.0]]
    result = asyncio.run(
        manage_graphics(
            SimpleNamespace(),
            action="bake_set_probe_positions",
            target="LightProbes",
            positions=positions,
        )
    )
    assert result["success"] is True
    assert mock_unity["params"]["action"] == "bake_set_probe_positions"
    assert mock_unity["params"]["target"] == "LightProbes"
    assert mock_unity["params"]["positions"] == positions


# ---------------------------------------------------------------------------
# Stats actions
# ---------------------------------------------------------------------------

def test_stats_get_sends_action(mock_unity):
    result = asyncio.run(
        manage_graphics(SimpleNamespace(), action="stats_get")
    )
    assert result["success"] is True
    assert mock_unity["params"]["action"] == "stats_get"


def test_stats_list_counters_sends_action(mock_unity):
    result = asyncio.run(
        manage_graphics(SimpleNamespace(), action="stats_list_counters")
    )
    assert result["success"] is True
    assert mock_unity["params"]["action"] == "stats_list_counters"


def test_stats_set_scene_debug_sends_mode(mock_unity):
    result = asyncio.run(
        manage_graphics(
            SimpleNamespace(),
            action="stats_set_scene_debug",
            mode="Wireframe",
        )
    )
    assert result["success"] is True
    assert mock_unity["params"]["action"] == "stats_set_scene_debug"
    assert mock_unity["params"]["mode"] == "Wireframe"


def test_stats_get_memory_sends_action(mock_unity):
    result = asyncio.run(
        manage_graphics(SimpleNamespace(), action="stats_get_memory")
    )
    assert result["success"] is True
    assert mock_unity["params"]["action"] == "stats_get_memory"


# ---------------------------------------------------------------------------
# Pipeline actions
# ---------------------------------------------------------------------------

def test_pipeline_get_info_sends_action(mock_unity):
    result = asyncio.run(
        manage_graphics(SimpleNamespace(), action="pipeline_get_info")
    )
    assert result["success"] is True
    assert mock_unity["params"]["action"] == "pipeline_get_info"


def test_pipeline_set_quality_sends_level(mock_unity):
    result = asyncio.run(
        manage_graphics(
            SimpleNamespace(),
            action="pipeline_set_quality",
            level="Ultra",
        )
    )
    assert result["success"] is True
    assert mock_unity["params"]["action"] == "pipeline_set_quality"
    assert mock_unity["params"]["level"] == "Ultra"


def test_pipeline_get_settings_sends_action(mock_unity):
    result = asyncio.run(
        manage_graphics(SimpleNamespace(), action="pipeline_get_settings")
    )
    assert result["success"] is True
    assert mock_unity["params"]["action"] == "pipeline_get_settings"


def test_pipeline_set_settings_sends_settings(mock_unity):
    result = asyncio.run(
        manage_graphics(
            SimpleNamespace(),
            action="pipeline_set_settings",
            settings={"renderScale": 1.5, "msaa": 4},
        )
    )
    assert result["success"] is True
    assert mock_unity["params"]["action"] == "pipeline_set_settings"
    assert mock_unity["params"]["settings"]["renderScale"] == 1.5
    assert mock_unity["params"]["settings"]["msaa"] == 4


# ---------------------------------------------------------------------------
# Feature actions
# ---------------------------------------------------------------------------

def test_feature_list_sends_action(mock_unity):
    result = asyncio.run(
        manage_graphics(SimpleNamespace(), action="feature_list")
    )
    assert result["success"] is True
    assert mock_unity["params"]["action"] == "feature_list"


def test_feature_add_sends_type_and_name(mock_unity):
    result = asyncio.run(
        manage_graphics(
            SimpleNamespace(),
            action="feature_add",
            feature_type="RenderObjects",
            name="DrawOpaqueOutline",
            material="Assets/Materials/Outline.mat",
        )
    )
    assert result["success"] is True
    assert mock_unity["params"]["action"] == "feature_add"
    assert mock_unity["params"]["type"] == "RenderObjects"
    assert mock_unity["params"]["name"] == "DrawOpaqueOutline"
    assert mock_unity["params"]["material"] == "Assets/Materials/Outline.mat"


def test_feature_remove_sends_index(mock_unity):
    result = asyncio.run(
        manage_graphics(
            SimpleNamespace(),
            action="feature_remove",
            index=2,
        )
    )
    assert result["success"] is True
    assert mock_unity["params"]["action"] == "feature_remove"
    assert mock_unity["params"]["index"] == 2


def test_feature_configure_sends_index_and_settings(mock_unity):
    result = asyncio.run(
        manage_graphics(
            SimpleNamespace(),
            action="feature_configure",
            index=0,
            settings={"renderPassEvent": "AfterRenderingOpaques", "layerMask": 1},
        )
    )
    assert result["success"] is True
    assert mock_unity["params"]["action"] == "feature_configure"
    assert mock_unity["params"]["index"] == 0
    assert mock_unity["params"]["settings"]["renderPassEvent"] == "AfterRenderingOpaques"


def test_feature_toggle_sends_index_and_active(mock_unity):
    result = asyncio.run(
        manage_graphics(
            SimpleNamespace(),
            action="feature_toggle",
            index=1,
            active=False,
        )
    )
    assert result["success"] is True
    assert mock_unity["params"]["action"] == "feature_toggle"
    assert mock_unity["params"]["index"] == 1
    assert mock_unity["params"]["active"] is False


def test_feature_reorder_sends_order(mock_unity):
    result = asyncio.run(
        manage_graphics(
            SimpleNamespace(),
            action="feature_reorder",
            order=[2, 0, 1],
        )
    )
    assert result["success"] is True
    assert mock_unity["params"]["action"] == "feature_reorder"
    assert mock_unity["params"]["order"] == [2, 0, 1]


# ---------------------------------------------------------------------------
# Parameter handling
# ---------------------------------------------------------------------------

def test_none_params_omitted(mock_unity):
    result = asyncio.run(
        manage_graphics(
            SimpleNamespace(),
            action="ping",
            target=None,
            effect=None,
            parameters=None,
            properties=None,
            settings=None,
            name=None,
            is_global=None,
            weight=None,
            priority=None,
            profile_path=None,
            effects=None,
            path=None,
            level=None,
            position=None,
            grid_size=None,
            spacing=None,
            size=None,
            resolution=None,
            mode=None,
            hdr=None,
            box_projection=None,
            positions=None,
            index=None,
            active=None,
            order=None,
            async_bake=None,
            feature_type=None,
            material=None,
        )
    )
    assert result["success"] is True
    # Only "action" key should be present
    assert mock_unity["params"] == {"action": "ping"}


def test_non_none_params_included(mock_unity):
    result = asyncio.run(
        manage_graphics(
            SimpleNamespace(),
            action="volume_create",
            name="Vol1",
            is_global=False,
            weight=0.5,
        )
    )
    assert result["success"] is True
    assert mock_unity["params"]["name"] == "Vol1"
    assert mock_unity["params"]["is_global"] is False
    assert mock_unity["params"]["weight"] == 0.5
    # Other optional params should not be present
    assert "target" not in mock_unity["params"]
    assert "effect" not in mock_unity["params"]
    assert "settings" not in mock_unity["params"]


def test_async_bake_maps_to_async_key(mock_unity):
    """The async_bake Python param maps to 'async' in the params dict."""
    result = asyncio.run(
        manage_graphics(
            SimpleNamespace(),
            action="bake_start",
            async_bake=True,
        )
    )
    assert result["success"] is True
    assert "async" in mock_unity["params"]
    assert mock_unity["params"]["async"] is True
    assert "async_bake" not in mock_unity["params"]


def test_feature_type_maps_to_type_key(mock_unity):
    """The feature_type Python param maps to 'type' in the params dict."""
    result = asyncio.run(
        manage_graphics(
            SimpleNamespace(),
            action="feature_add",
            feature_type="ScreenSpaceAmbientOcclusion",
        )
    )
    assert result["success"] is True
    assert "type" in mock_unity["params"]
    assert mock_unity["params"]["type"] == "ScreenSpaceAmbientOcclusion"
    assert "feature_type" not in mock_unity["params"]


def test_non_dict_response_wrapped(monkeypatch):
    """When Unity returns a non-dict, it should be wrapped."""
    monkeypatch.setattr(
        "services.tools.manage_graphics.get_unity_instance_from_context",
        AsyncMock(return_value="unity-1"),
    )

    async def fake_send(send_fn, unity_instance, tool_name, params):
        return "unexpected string response"

    monkeypatch.setattr(
        "services.tools.manage_graphics.send_with_unity_instance",
        fake_send,
    )

    result = asyncio.run(
        manage_graphics(SimpleNamespace(), action="ping")
    )
    assert result["success"] is False
    assert "unexpected string response" in result["message"]


# ---------------------------------------------------------------------------
# Case insensitivity
# ---------------------------------------------------------------------------

def test_action_case_insensitive(mock_unity):
    result = asyncio.run(
        manage_graphics(SimpleNamespace(), action="Volume_Create")
    )
    assert result["success"] is True
    assert mock_unity["params"]["action"] == "volume_create"


def test_action_uppercase(mock_unity):
    result = asyncio.run(
        manage_graphics(SimpleNamespace(), action="PING")
    )
    assert result["success"] is True
    assert mock_unity["params"]["action"] == "ping"


def test_action_mixed_case_bake(mock_unity):
    result = asyncio.run(
        manage_graphics(SimpleNamespace(), action="Bake_Start")
    )
    assert result["success"] is True
    assert mock_unity["params"]["action"] == "bake_start"


# ---------------------------------------------------------------------------
# All actions forward correctly
# ---------------------------------------------------------------------------

@pytest.mark.parametrize("action_name", ALL_ACTIONS)
def test_every_action_forwards_to_unity(mock_unity, action_name):
    """Every valid action should be forwarded to Unity without error."""
    result = asyncio.run(
        manage_graphics(SimpleNamespace(), action=action_name)
    )
    assert result["success"] is True
    assert mock_unity["tool_name"] == "manage_graphics"
    assert mock_unity["params"]["action"] == action_name


# ---------------------------------------------------------------------------
# Tool registration
# ---------------------------------------------------------------------------

def test_tool_registered_with_graphics_group():
    from services.registry.tool_registry import _tool_registry

    graphics_tools = [
        t for t in _tool_registry if t.get("name") == "manage_graphics"
    ]
    assert len(graphics_tools) == 1
    assert graphics_tools[0]["group"] == "graphics"
