"""Tests for manage_animation tool and CLI commands."""

import asyncio
import json
import pytest
from unittest.mock import patch, MagicMock
from click.testing import CliRunner

from cli.commands.animation import animation
from cli.utils.config import CLIConfig
from services.tools.manage_animation import (
    ALL_ACTIONS,
    ANIMATOR_ACTIONS,
    CONTROLLER_ACTIONS,
    CLIP_ACTIONS,
)


# =============================================================================
# Fixtures
# =============================================================================

@pytest.fixture
def runner():
    return CliRunner()


@pytest.fixture
def mock_config():
    return CLIConfig(
        host="127.0.0.1",
        port=8080,
        timeout=30,
        format="text",
        unity_instance=None,
    )


@pytest.fixture
def mock_success():
    return {"success": True, "message": "OK", "data": {}}


# =============================================================================
# Action Lists
# =============================================================================

class TestActionLists:
    """Verify action list completeness and consistency."""

    def test_all_actions_includes_all_prefixes(self):
        assert set(ALL_ACTIONS) == set(ANIMATOR_ACTIONS + CONTROLLER_ACTIONS + CLIP_ACTIONS)

    def test_animator_actions_prefixed(self):
        for a in ANIMATOR_ACTIONS:
            assert a.startswith("animator_"), f"{a} should start with animator_"

    def test_controller_actions_prefixed(self):
        for a in CONTROLLER_ACTIONS:
            assert a.startswith("controller_"), f"{a} should start with controller_"

    def test_clip_actions_prefixed(self):
        for a in CLIP_ACTIONS:
            assert a.startswith("clip_"), f"{a} should start with clip_"

    def test_no_duplicate_actions(self):
        assert len(ALL_ACTIONS) == len(set(ALL_ACTIONS))

    def test_expected_animator_actions_present(self):
        expected = {"animator_get_info", "animator_play", "animator_crossfade",
                    "animator_set_parameter", "animator_get_parameter",
                    "animator_set_speed", "animator_set_enabled"}
        assert expected.issubset(set(ANIMATOR_ACTIONS))

    def test_expected_controller_actions_present(self):
        expected = {"controller_create", "controller_add_state", "controller_add_transition",
                    "controller_add_parameter", "controller_get_info", "controller_assign",
                    "controller_add_layer", "controller_remove_layer", "controller_set_layer_weight",
                    "controller_create_blend_tree_1d", "controller_create_blend_tree_2d", "controller_add_blend_tree_child"}
        assert expected.issubset(set(CONTROLLER_ACTIONS))

    def test_expected_clip_actions_present(self):
        expected = {"clip_create", "clip_get_info", "clip_add_curve",
                    "clip_set_curve", "clip_set_vector_curve",
                    "clip_create_preset", "clip_assign",
                    "clip_add_event", "clip_remove_event"}
        assert expected.issubset(set(CLIP_ACTIONS))


# =============================================================================
# Tool Validation (Python-side, no Unity)
# =============================================================================

class TestManageAnimationToolValidation:
    """Test action validation in the manage_animation tool function."""

    def test_unknown_action_returns_error(self):
        from services.tools.manage_animation import manage_animation

        ctx = MagicMock()
        ctx.get_state = MagicMock(return_value=None)

        result = asyncio.run(manage_animation(ctx, action="invalid_action"))
        assert result["success"] is False
        assert "Unknown action" in result["message"]

    def test_unknown_animator_action_suggests_prefix(self):
        from services.tools.manage_animation import manage_animation

        ctx = MagicMock()
        ctx.get_state = MagicMock(return_value=None)

        result = asyncio.run(manage_animation(ctx, action="animator_nonexistent"))
        assert result["success"] is False
        assert "animator_" in result["message"]

    def test_unknown_clip_action_suggests_prefix(self):
        from services.tools.manage_animation import manage_animation

        ctx = MagicMock()
        ctx.get_state = MagicMock(return_value=None)

        result = asyncio.run(manage_animation(ctx, action="clip_nonexistent"))
        assert result["success"] is False
        assert "clip_" in result["message"]

    def test_unknown_controller_action_suggests_prefix(self):
        from services.tools.manage_animation import manage_animation

        ctx = MagicMock()
        ctx.get_state = MagicMock(return_value=None)

        result = asyncio.run(manage_animation(ctx, action="controller_nonexistent"))
        assert result["success"] is False
        assert "controller_" in result["message"]

    def test_no_prefix_action_suggests_valid_prefixes(self):
        from services.tools.manage_animation import manage_animation

        ctx = MagicMock()
        ctx.get_state = MagicMock(return_value=None)

        result = asyncio.run(manage_animation(ctx, action="bogus"))
        assert result["success"] is False
        assert "animator_" in result["message"]
        assert "controller_" in result["message"]
        assert "clip_" in result["message"]


# =============================================================================
# CLI Command Parameter Building
# =============================================================================

def _get_params(mock_run):
    """Helper to extract the params dict from a mock run_command call."""
    return mock_run.call_args[0][1]


class TestAnimatorCLICommands:
    """Verify CLI commands build correct parameter dicts.

    Note: _normalize_params moves non-top-level keys into 'properties' sub-dict,
    matching the VFX tool pattern. Unity C# side flattens properties into params.
    """

    def test_animator_info_builds_correct_params(self, runner, mock_config, mock_success):
        with patch("cli.commands.animation.get_config", return_value=mock_config):
            with patch("cli.commands.animation.run_command", return_value=mock_success) as mock_run:
                runner.invoke(animation, ["animator", "info", "Player"])

                mock_run.assert_called_once()
                args = mock_run.call_args
                assert args[0][0] == "manage_animation"
                params = _get_params(mock_run)
                assert params["action"] == "animator_get_info"
                assert params["target"] == "Player"

    def test_animator_play_builds_correct_params(self, runner, mock_config, mock_success):
        with patch("cli.commands.animation.get_config", return_value=mock_config):
            with patch("cli.commands.animation.run_command", return_value=mock_success) as mock_run:
                runner.invoke(animation, ["animator", "play", "Player", "Walk"])

                params = _get_params(mock_run)
                assert params["action"] == "animator_play"
                assert params["target"] == "Player"
                # stateName goes into properties (non-top-level key)
                assert params["properties"]["stateName"] == "Walk"

    def test_animator_play_with_layer(self, runner, mock_config, mock_success):
        with patch("cli.commands.animation.get_config", return_value=mock_config):
            with patch("cli.commands.animation.run_command", return_value=mock_success) as mock_run:
                runner.invoke(animation, ["animator", "play", "Player", "Attack", "--layer", "1"])

                params = _get_params(mock_run)
                assert params["action"] == "animator_play"
                assert params["properties"]["layer"] == 1

    def test_animator_crossfade_builds_correct_params(self, runner, mock_config, mock_success):
        with patch("cli.commands.animation.get_config", return_value=mock_config):
            with patch("cli.commands.animation.run_command", return_value=mock_success) as mock_run:
                runner.invoke(animation, ["animator", "crossfade", "Player", "Run", "--duration", "0.5"])

                params = _get_params(mock_run)
                assert params["action"] == "animator_crossfade"
                assert params["target"] == "Player"
                assert params["properties"]["stateName"] == "Run"
                assert params["properties"]["duration"] == 0.5

    def test_animator_set_parameter_float(self, runner, mock_config, mock_success):
        with patch("cli.commands.animation.get_config", return_value=mock_config):
            with patch("cli.commands.animation.run_command", return_value=mock_success) as mock_run:
                runner.invoke(animation, ["animator", "set-parameter", "Player", "Speed", "5.0"])

                params = _get_params(mock_run)
                assert params["action"] == "animator_set_parameter"
                assert params["target"] == "Player"
                assert params["properties"]["parameterName"] == "Speed"
                assert params["properties"]["value"] == 5.0

    def test_animator_set_parameter_with_type(self, runner, mock_config, mock_success):
        with patch("cli.commands.animation.get_config", return_value=mock_config):
            with patch("cli.commands.animation.run_command", return_value=mock_success) as mock_run:
                runner.invoke(animation, ["animator", "set-parameter", "Player", "IsRunning", "true", "--type", "bool"])

                params = _get_params(mock_run)
                assert params["properties"]["parameterName"] == "IsRunning"
                assert params["properties"]["value"] is True
                assert params["properties"]["parameterType"] == "bool"

    def test_animator_get_parameter(self, runner, mock_config, mock_success):
        with patch("cli.commands.animation.get_config", return_value=mock_config):
            with patch("cli.commands.animation.run_command", return_value=mock_success) as mock_run:
                runner.invoke(animation, ["animator", "get-parameter", "Player", "Speed"])

                params = _get_params(mock_run)
                assert params["action"] == "animator_get_parameter"
                assert params["properties"]["parameterName"] == "Speed"

    def test_animator_set_speed(self, runner, mock_config, mock_success):
        with patch("cli.commands.animation.get_config", return_value=mock_config):
            with patch("cli.commands.animation.run_command", return_value=mock_success) as mock_run:
                runner.invoke(animation, ["animator", "set-speed", "Player", "2.0"])

                params = _get_params(mock_run)
                assert params["action"] == "animator_set_speed"
                assert params["properties"]["speed"] == 2.0

    def test_search_method_forwarded(self, runner, mock_config, mock_success):
        with patch("cli.commands.animation.get_config", return_value=mock_config):
            with patch("cli.commands.animation.run_command", return_value=mock_success) as mock_run:
                runner.invoke(animation, ["animator", "info", "Player", "--search-method", "by_id"])

                params = _get_params(mock_run)
                assert params["searchMethod"] == "by_id"


class TestClipCLICommands:
    """Verify clip CLI commands build correct parameter dicts."""

    def test_clip_create_builds_correct_params(self, runner, mock_config, mock_success):
        with patch("cli.commands.animation.get_config", return_value=mock_config):
            with patch("cli.commands.animation.run_command", return_value=mock_success) as mock_run:
                runner.invoke(animation, ["clip", "create", "Assets/Anim/Walk.anim", "--length", "2.0", "--loop"])

                params = _get_params(mock_run)
                assert params["action"] == "clip_create"
                assert params["clipPath"] == "Assets/Anim/Walk.anim"
                assert params["properties"]["length"] == 2.0
                assert params["properties"]["loop"] is True

    def test_clip_info_builds_correct_params(self, runner, mock_config, mock_success):
        with patch("cli.commands.animation.get_config", return_value=mock_config):
            with patch("cli.commands.animation.run_command", return_value=mock_success) as mock_run:
                runner.invoke(animation, ["clip", "info", "Assets/Anim/Walk.anim"])

                params = _get_params(mock_run)
                assert params["action"] == "clip_get_info"
                assert params["clipPath"] == "Assets/Anim/Walk.anim"

    def test_clip_add_curve_parses_keys(self, runner, mock_config, mock_success):
        with patch("cli.commands.animation.get_config", return_value=mock_config):
            with patch("cli.commands.animation.run_command", return_value=mock_success) as mock_run:
                runner.invoke(animation, [
                    "clip", "add-curve", "Assets/Anim/Bounce.anim",
                    "--property", "localPosition.y",
                    "--type", "Transform",
                    "--keys", "[[0,0],[0.5,2],[1,0]]",
                ])

                params = _get_params(mock_run)
                assert params["action"] == "clip_add_curve"
                assert params["clipPath"] == "Assets/Anim/Bounce.anim"
                # propertyPath, type, keys go into properties (non-top-level)
                assert params["properties"]["propertyPath"] == "localPosition.y"
                assert params["properties"]["type"] == "Transform"
                assert params["properties"]["keys"] == [[0, 0], [0.5, 2], [1, 0]]

    def test_clip_assign_builds_correct_params(self, runner, mock_config, mock_success):
        with patch("cli.commands.animation.get_config", return_value=mock_config):
            with patch("cli.commands.animation.run_command", return_value=mock_success) as mock_run:
                runner.invoke(animation, ["clip", "assign", "Cube", "Assets/Anim/Bounce.anim"])

                params = _get_params(mock_run)
                assert params["action"] == "clip_assign"
                assert params["target"] == "Cube"
                assert params["clipPath"] == "Assets/Anim/Bounce.anim"


class TestRawCommand:
    """Verify raw escape-hatch command works correctly."""

    def test_raw_with_target_and_params(self, runner, mock_config, mock_success):
        with patch("cli.commands.animation.get_config", return_value=mock_config):
            with patch("cli.commands.animation.run_command", return_value=mock_success) as mock_run:
                runner.invoke(animation, [
                    "raw", "animator_play", "Player",
                    "--params", '{"stateName": "Walk"}',
                ])

                params = _get_params(mock_run)
                assert params["action"] == "animator_play"
                assert params["target"] == "Player"
                assert params["properties"]["stateName"] == "Walk"

    def test_raw_with_clip_path(self, runner, mock_config, mock_success):
        with patch("cli.commands.animation.get_config", return_value=mock_config):
            with patch("cli.commands.animation.run_command", return_value=mock_success) as mock_run:
                runner.invoke(animation, [
                    "raw", "clip_create",
                    "--clip-path", "Assets/Anim/Test.anim",
                    "--params", '{"length": 2.0}',
                ])

                params = _get_params(mock_run)
                assert params["action"] == "clip_create"
                assert params["clipPath"] == "Assets/Anim/Test.anim"
                assert params["properties"]["length"] == 2.0


# =============================================================================
# Controller CLI Commands
# =============================================================================

class TestControllerCLICommands:
    """Verify controller CLI commands build correct parameter dicts."""

    def test_controller_create_builds_correct_params(self, runner, mock_config, mock_success):
        with patch("cli.commands.animation.get_config", return_value=mock_config):
            with patch("cli.commands.animation.run_command", return_value=mock_success) as mock_run:
                runner.invoke(animation, ["controller", "create", "Assets/Anim/Player.controller"])

                params = _get_params(mock_run)
                assert params["action"] == "controller_create"
                assert params["controllerPath"] == "Assets/Anim/Player.controller"

    def test_controller_add_state_builds_correct_params(self, runner, mock_config, mock_success):
        with patch("cli.commands.animation.get_config", return_value=mock_config):
            with patch("cli.commands.animation.run_command", return_value=mock_success) as mock_run:
                runner.invoke(animation, [
                    "controller", "add-state", "Assets/Anim/Player.controller", "Walk",
                    "--clip-path", "Assets/Anim/Walk.anim",
                    "--speed", "1.5",
                ])

                params = _get_params(mock_run)
                assert params["action"] == "controller_add_state"
                assert params["controllerPath"] == "Assets/Anim/Player.controller"
                assert params["clipPath"] == "Assets/Anim/Walk.anim"
                assert params["properties"]["stateName"] == "Walk"
                assert params["properties"]["speed"] == 1.5

    def test_controller_add_state_with_default(self, runner, mock_config, mock_success):
        with patch("cli.commands.animation.get_config", return_value=mock_config):
            with patch("cli.commands.animation.run_command", return_value=mock_success) as mock_run:
                runner.invoke(animation, [
                    "controller", "add-state", "Assets/Anim/Player.controller", "Idle",
                    "--is-default",
                ])

                params = _get_params(mock_run)
                assert params["properties"]["isDefault"] is True

    def test_controller_add_transition_builds_correct_params(self, runner, mock_config, mock_success):
        with patch("cli.commands.animation.get_config", return_value=mock_config):
            with patch("cli.commands.animation.run_command", return_value=mock_success) as mock_run:
                runner.invoke(animation, [
                    "controller", "add-transition", "Assets/Anim/Player.controller",
                    "Idle", "Walk",
                    "--no-exit-time", "--duration", "0.25",
                    "--conditions", '[{"parameter":"Speed","mode":"greater","threshold":0.1}]',
                ])

                params = _get_params(mock_run)
                assert params["action"] == "controller_add_transition"
                assert params["controllerPath"] == "Assets/Anim/Player.controller"
                assert params["properties"]["fromState"] == "Idle"
                assert params["properties"]["toState"] == "Walk"
                assert params["properties"]["hasExitTime"] is False
                assert params["properties"]["duration"] == 0.25
                assert len(params["properties"]["conditions"]) == 1
                assert params["properties"]["conditions"][0]["parameter"] == "Speed"

    def test_controller_add_parameter_builds_correct_params(self, runner, mock_config, mock_success):
        with patch("cli.commands.animation.get_config", return_value=mock_config):
            with patch("cli.commands.animation.run_command", return_value=mock_success) as mock_run:
                runner.invoke(animation, [
                    "controller", "add-parameter", "Assets/Anim/Player.controller",
                    "Speed", "--type", "float", "--default-value", "0.0",
                ])

                params = _get_params(mock_run)
                assert params["action"] == "controller_add_parameter"
                assert params["controllerPath"] == "Assets/Anim/Player.controller"
                assert params["properties"]["parameterName"] == "Speed"
                assert params["properties"]["parameterType"] == "float"
                assert params["properties"]["defaultValue"] == 0.0

    def test_controller_add_parameter_trigger(self, runner, mock_config, mock_success):
        with patch("cli.commands.animation.get_config", return_value=mock_config):
            with patch("cli.commands.animation.run_command", return_value=mock_success) as mock_run:
                runner.invoke(animation, [
                    "controller", "add-parameter", "Assets/Anim/Player.controller",
                    "Jump", "--type", "trigger",
                ])

                params = _get_params(mock_run)
                assert params["properties"]["parameterType"] == "trigger"

    def test_controller_info_builds_correct_params(self, runner, mock_config, mock_success):
        with patch("cli.commands.animation.get_config", return_value=mock_config):
            with patch("cli.commands.animation.run_command", return_value=mock_success) as mock_run:
                runner.invoke(animation, ["controller", "info", "Assets/Anim/Player.controller"])

                params = _get_params(mock_run)
                assert params["action"] == "controller_get_info"
                assert params["controllerPath"] == "Assets/Anim/Player.controller"

    def test_controller_assign_builds_correct_params(self, runner, mock_config, mock_success):
        with patch("cli.commands.animation.get_config", return_value=mock_config):
            with patch("cli.commands.animation.run_command", return_value=mock_success) as mock_run:
                runner.invoke(animation, [
                    "controller", "assign", "Assets/Anim/Player.controller", "Player",
                ])

                params = _get_params(mock_run)
                assert params["action"] == "controller_assign"
                assert params["controllerPath"] == "Assets/Anim/Player.controller"
                assert params["target"] == "Player"

    def test_controller_assign_with_search_method(self, runner, mock_config, mock_success):
        with patch("cli.commands.animation.get_config", return_value=mock_config):
            with patch("cli.commands.animation.run_command", return_value=mock_success) as mock_run:
                runner.invoke(animation, [
                    "controller", "assign", "Assets/Anim/Player.controller", "Player",
                    "--search-method", "by_name",
                ])

                params = _get_params(mock_run)
                assert params["searchMethod"] == "by_name"


# =============================================================================
# Vector Curve and Preset CLI Commands
# =============================================================================

class TestVectorCurveAndPresetCLICommands:
    """Verify vector curve and preset CLI commands build correct parameter dicts."""

    def test_clip_set_vector_curve_builds_correct_params(self, runner, mock_config, mock_success):
        with patch("cli.commands.animation.get_config", return_value=mock_config):
            with patch("cli.commands.animation.run_command", return_value=mock_success) as mock_run:
                runner.invoke(animation, [
                    "clip", "set-vector-curve", "Assets/Anim/Move.anim",
                    "--property", "localPosition",
                    "--keys", '[{"time":0,"value":[0,1,-10]},{"time":1,"value":[2,1,-10]}]',
                ])

                params = _get_params(mock_run)
                assert params["action"] == "clip_set_vector_curve"
                assert params["clipPath"] == "Assets/Anim/Move.anim"
                assert params["properties"]["property"] == "localPosition"
                assert params["properties"]["keys"] == [
                    {"time": 0, "value": [0, 1, -10]},
                    {"time": 1, "value": [2, 1, -10]},
                ]

    def test_clip_set_vector_curve_with_type(self, runner, mock_config, mock_success):
        with patch("cli.commands.animation.get_config", return_value=mock_config):
            with patch("cli.commands.animation.run_command", return_value=mock_success) as mock_run:
                runner.invoke(animation, [
                    "clip", "set-vector-curve", "Assets/Anim/Scale.anim",
                    "--property", "localScale",
                    "--type", "Transform",
                    "--keys", '[{"time":0,"value":[1,1,1]},{"time":1,"value":[2,2,2]}]',
                ])

                params = _get_params(mock_run)
                assert params["properties"]["type"] == "Transform"

    def test_clip_create_preset_builds_correct_params(self, runner, mock_config, mock_success):
        with patch("cli.commands.animation.get_config", return_value=mock_config):
            with patch("cli.commands.animation.run_command", return_value=mock_success) as mock_run:
                runner.invoke(animation, [
                    "clip", "create-preset", "Assets/Anim/Bounce.anim", "bounce",
                    "--duration", "2.0", "--amplitude", "0.5",
                ])

                params = _get_params(mock_run)
                assert params["action"] == "clip_create_preset"
                assert params["clipPath"] == "Assets/Anim/Bounce.anim"
                assert params["properties"]["preset"] == "bounce"
                assert params["properties"]["duration"] == 2.0
                assert params["properties"]["amplitude"] == 0.5

    def test_clip_create_preset_no_loop(self, runner, mock_config, mock_success):
        with patch("cli.commands.animation.get_config", return_value=mock_config):
            with patch("cli.commands.animation.run_command", return_value=mock_success) as mock_run:
                runner.invoke(animation, [
                    "clip", "create-preset", "Assets/Anim/Spin.anim", "spin", "--no-loop",
                ])

                params = _get_params(mock_run)
                assert params["properties"]["loop"] is False

    def test_clip_create_preset_all_presets_accepted(self, runner, mock_config, mock_success):
        """Verify all preset names are accepted by the CLI."""
        presets = ["bounce", "rotate", "pulse", "fade", "shake", "hover", "spin",
                   "sway", "bob", "wiggle", "blink", "slide_in", "elastic"]
        for preset in presets:
            with patch("cli.commands.animation.get_config", return_value=mock_config):
                with patch("cli.commands.animation.run_command", return_value=mock_success) as mock_run:
                    result = runner.invoke(animation, [
                        "clip", "create-preset", f"Assets/Anim/{preset}.anim", preset,
                    ])
                    assert result.exit_code == 0, f"Preset '{preset}' failed: {result.output}"

    def test_clip_add_event_builds_correct_params(self, runner, mock_config, mock_success):
        with patch("cli.commands.animation.get_config", return_value=mock_config):
            with patch("cli.commands.animation.run_command", return_value=mock_success) as mock_run:
                runner.invoke(animation, [
                    "clip", "add-event", "Assets/Anim/Attack.anim",
                    "--function", "OnAttackHit", "--time", "0.5",
                    "--string-param", "sword", "--float-param", "10.5", "--int-param", "2"
                ])

                params = _get_params(mock_run)
                assert params["action"] == "clip_add_event"
                assert params["clipPath"] == "Assets/Anim/Attack.anim"
                assert params["properties"]["functionName"] == "OnAttackHit"
                assert params["properties"]["time"] == 0.5
                assert params["properties"]["stringParameter"] == "sword"
                assert params["properties"]["floatParameter"] == 10.5
                assert params["properties"]["intParameter"] == 2

    def test_clip_remove_event_by_index(self, runner, mock_config, mock_success):
        with patch("cli.commands.animation.get_config", return_value=mock_config):
            with patch("cli.commands.animation.run_command", return_value=mock_success) as mock_run:
                runner.invoke(animation, [
                    "clip", "remove-event", "Assets/Anim/Attack.anim",
                    "--event-index", "0"
                ])

                params = _get_params(mock_run)
                assert params["action"] == "clip_remove_event"
                assert params["properties"]["eventIndex"] == 0

    def test_clip_remove_event_by_function(self, runner, mock_config, mock_success):
        with patch("cli.commands.animation.get_config", return_value=mock_config):
            with patch("cli.commands.animation.run_command", return_value=mock_success) as mock_run:
                runner.invoke(animation, [
                    "clip", "remove-event", "Assets/Anim/Attack.anim",
                    "--function", "OnAttackHit", "--time", "0.5"
                ])

                params = _get_params(mock_run)
                assert params["action"] == "clip_remove_event"
                assert params["properties"]["functionName"] == "OnAttackHit"
                assert params["properties"]["time"] == 0.5


class TestLayerCLICommands:
    """Test layer management CLI commands."""

    def test_controller_add_layer_builds_correct_params(self, runner, mock_config, mock_success):
        with patch("cli.commands.animation.get_config", return_value=mock_config):
            with patch("cli.commands.animation.run_command", return_value=mock_success) as mock_run:
                runner.invoke(animation, [
                    "controller", "add-layer", "Assets/Anim/Player.controller", "UpperBody",
                    "--weight", "0.8", "--blending-mode", "additive"
                ])

                params = _get_params(mock_run)
                assert params["action"] == "controller_add_layer"
                assert params["properties"]["layerName"] == "UpperBody"
                assert params["properties"]["weight"] == 0.8
                assert params["properties"]["blendingMode"] == "additive"

    def test_controller_remove_layer_by_index(self, runner, mock_config, mock_success):
        with patch("cli.commands.animation.get_config", return_value=mock_config):
            with patch("cli.commands.animation.run_command", return_value=mock_success) as mock_run:
                runner.invoke(animation, [
                    "controller", "remove-layer", "Assets/Anim/Player.controller",
                    "--layer-index", "1"
                ])

                params = _get_params(mock_run)
                assert params["action"] == "controller_remove_layer"
                assert params["properties"]["layerIndex"] == 1

    def test_controller_set_layer_weight(self, runner, mock_config, mock_success):
        with patch("cli.commands.animation.get_config", return_value=mock_config):
            with patch("cli.commands.animation.run_command", return_value=mock_success) as mock_run:
                runner.invoke(animation, [
                    "controller", "set-layer-weight", "Assets/Anim/Player.controller", "0.5",
                    "--layer-name", "UpperBody"
                ])

                params = _get_params(mock_run)
                assert params["action"] == "controller_set_layer_weight"
                assert params["properties"]["weight"] == 0.5
                assert params["properties"]["layerName"] == "UpperBody"


class TestBlendTreeCLICommands:
    """Test blend tree CLI commands."""

    def test_controller_create_blend_tree_1d_builds_correct_params(self, runner, mock_config, mock_success):
        with patch("cli.commands.animation.get_config", return_value=mock_config):
            with patch("cli.commands.animation.run_command", return_value=mock_success) as mock_run:
                runner.invoke(animation, [
                    "controller", "create-blend-tree-1d", "Assets/Anim/Player.controller", "Locomotion",
                    "--blend-param", "Speed", "--layer-index", "0"
                ])

                params = _get_params(mock_run)
                assert params["action"] == "controller_create_blend_tree_1d"
                assert params["properties"]["stateName"] == "Locomotion"
                assert params["properties"]["blendParameter"] == "Speed"
                assert params["properties"]["layerIndex"] == 0

    def test_controller_create_blend_tree_2d_builds_correct_params(self, runner, mock_config, mock_success):
        with patch("cli.commands.animation.get_config", return_value=mock_config):
            with patch("cli.commands.animation.run_command", return_value=mock_success) as mock_run:
                runner.invoke(animation, [
                    "controller", "create-blend-tree-2d", "Assets/Anim/Player.controller", "Movement",
                    "--blend-param-x", "VelocityX", "--blend-param-y", "VelocityZ",
                    "--blend-type", "freeformdirectional2d"
                ])

                params = _get_params(mock_run)
                assert params["action"] == "controller_create_blend_tree_2d"
                assert params["properties"]["stateName"] == "Movement"
                assert params["properties"]["blendParameterX"] == "VelocityX"
                assert params["properties"]["blendParameterY"] == "VelocityZ"
                assert params["properties"]["blendType"] == "freeformdirectional2d"

    def test_controller_add_blend_tree_child_1d(self, runner, mock_config, mock_success):
        with patch("cli.commands.animation.get_config", return_value=mock_config):
            with patch("cli.commands.animation.run_command", return_value=mock_success) as mock_run:
                runner.invoke(animation, [
                    "controller", "add-blend-tree-child", "Assets/Anim/Player.controller", "Locomotion",
                    "--clip-path", "Assets/Anim/Walk.anim", "--threshold", "1.0"
                ])

                params = _get_params(mock_run)
                assert params["action"] == "controller_add_blend_tree_child"
                # clipPath is a top-level key
                assert params["clipPath"] == "Assets/Anim/Walk.anim"
                assert params["properties"]["stateName"] == "Locomotion"
                assert params["properties"]["threshold"] == 1.0

    def test_controller_add_blend_tree_child_2d(self, runner, mock_config, mock_success):
        with patch("cli.commands.animation.get_config", return_value=mock_config):
            with patch("cli.commands.animation.run_command", return_value=mock_success) as mock_run:
                runner.invoke(animation, [
                    "controller", "add-blend-tree-child", "Assets/Anim/Player.controller", "Movement",
                    "--clip-path", "Assets/Anim/WalkForward.anim", "--position", "0", "1"
                ])

                params = _get_params(mock_run)
                assert params["action"] == "controller_add_blend_tree_child"
                assert params["properties"]["stateName"] == "Movement"
                assert params["properties"]["position"] == [0, 1]
