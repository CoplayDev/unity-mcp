"""Tests for the blender_bridge tool and CLI command.

Pass-through tool: NO API keys, NO file bytes. Unity transport fully mocked.
"""

import asyncio
import pytest
from unittest.mock import patch, MagicMock, AsyncMock
from click.testing import CliRunner

from cli.commands.blender import blender
from cli.utils.config import CLIConfig
from services.registry import get_registered_tools

from services.tools import blender_bridge as mod
from services.tools.blender_bridge import blender_bridge


COMMAND = "blender_bridge"


def _call_tool(**kwargs):
    ctx = MagicMock()
    with patch.object(mod, "get_unity_instance_from_context",
                      new=AsyncMock(return_value="unity-1")):
        with patch.object(mod, "send_with_unity_instance",
                          new=AsyncMock(return_value={"success": True, "data": {}})) as mock_send:
            result = asyncio.run(blender_bridge(ctx, **kwargs))
    return result, mock_send.call_args.args


def _sent_command(sent_args):
    return sent_args[2]


def _sent_params(sent_args):
    return sent_args[3]


@pytest.fixture
def runner():
    return CliRunner()


@pytest.fixture
def mock_config():
    return CLIConfig(host="127.0.0.1", port=8080, timeout=30, format="text", unity_instance=None)


@pytest.fixture
def cli_runner(runner, mock_config):
    def _invoke(args):
        with patch("cli.commands.blender.get_config", return_value=mock_config):
            with patch("cli.commands.blender.run_command",
                       return_value={"success": True, "message": "OK", "data": {}}) as mock_run:
                result = runner.invoke(blender, args)
                return result, mock_run
    return _invoke


class TestBlenderBridgeRegistration:
    def test_tool_registered_under_asset_gen_group(self):
        tools = get_registered_tools()
        tool = next((t for t in tools if t["name"] == COMMAND), None)
        assert tool is not None
        assert tool["group"] == "asset_gen"


class TestBlenderBridgeRouting:
    def test_status_sends_action_only(self):
        _, sent = _call_tool(action="status")
        assert _sent_command(sent) == COMMAND
        assert _sent_params(sent) == {"action": "status"}

    def test_import_model_maps_snake_case_to_camel_case(self):
        _, sent = _call_tool(
            action="import_model", object_names=["Cube"], target_size=2.0,
            place_in_scene=False, apply_modifiers=False, position=[0.0, 1.0, 0.0],
            animation_type="generic", output_folder="Assets/Generated/Imported",
        )
        assert _sent_command(sent) == COMMAND
        assert _sent_params(sent) == {
            "action": "import_model",
            "objectNames": ["Cube"],
            "targetSize": 2.0,
            "placeInScene": False,
            "applyModifiers": False,
            "position": [0.0, 1.0, 0.0],
            "animationType": "generic",
            "outputFolder": "Assets/Generated/Imported",
        }

    def test_run_python_and_screenshot_params(self):
        _, sent = _call_tool(action="run_python", code="print(1)", timeout_seconds=30)
        assert _sent_params(sent) == {"action": "run_python", "code": "print(1)", "timeoutSeconds": 30}

        _, sent = _call_tool(action="screenshot", max_size=800)
        assert _sent_params(sent) == {"action": "screenshot", "maxSize": 800}

    def test_non_dict_result_becomes_error(self):
        ctx = MagicMock()
        with patch.object(mod, "get_unity_instance_from_context", new=AsyncMock(return_value="unity-1")):
            with patch.object(mod, "send_with_unity_instance", new=AsyncMock(return_value="boom")):
                result = asyncio.run(blender_bridge(ctx, action="status"))
        assert result == {"success": False, "message": "boom"}


class TestBlenderCli:
    def test_status(self, cli_runner):
        result, mock_run = cli_runner(["status"])
        assert result.exit_code == 0, result.output
        assert mock_run.call_args.args[0] == COMMAND
        assert mock_run.call_args.args[1] == {"action": "status"}

    def test_import_model_flags(self, cli_runner):
        result, mock_run = cli_runner([
            "import-model", "--object", "House", "--object", "Tree", "--format", "fbx",
            "--target-size", "2", "--position", "0", "1", "0", "--no-place", "--keep-modifiers",
            "--animation-type", "generic",
        ])
        assert result.exit_code == 0, result.output
        assert mock_run.call_args.args[1] == {
            "action": "import_model",
            "objectNames": ["House", "Tree"],
            "format": "fbx",
            "targetSize": 2.0,
            "position": [0.0, 1.0, 0.0],
            "placeInScene": False,
            "applyModifiers": False,
            "animationType": "generic",
        }

    def test_run_python_requires_code_or_file(self, cli_runner):
        result, mock_run = cli_runner(["run-python"])
        assert result.exit_code != 0
        assert not mock_run.called

    def test_sync_addon_force(self, cli_runner):
        result, mock_run = cli_runner(["sync-addon", "--force"])
        assert result.exit_code == 0, result.output
        assert mock_run.call_args.args[1] == {"action": "sync_addon", "force": True}
