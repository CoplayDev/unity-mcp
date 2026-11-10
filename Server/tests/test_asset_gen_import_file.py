"""Tests for the import_model_file asset-gen tool and CLI command (local model import).

Pass-through tool: NO API keys, NO file bytes. Unity transport fully mocked.
"""

import asyncio
import pytest
from unittest.mock import patch, MagicMock, AsyncMock
from click.testing import CliRunner

from cli.commands.asset_gen import asset_gen
from cli.utils.config import CLIConfig
from services.registry import get_registered_tools

from services.tools import import_model_file as mod
from services.tools.import_model_file import import_model_file


COMMAND = "import_model_file"
ALLOWED_KEYS = {"sourcePath", "name", "outputFolder", "targetSize", "animationType"}


def _call_tool(**kwargs):
    ctx = MagicMock()
    with patch.object(mod, "get_unity_instance_from_context",
                      new=AsyncMock(return_value="unity-1")):
        with patch.object(mod, "send_with_unity_instance",
                          new=AsyncMock(return_value={"success": True, "data": {}})) as mock_send:
            result = asyncio.run(import_model_file(ctx, **kwargs))
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
        with patch("cli.commands.asset_gen.get_config", return_value=mock_config):
            with patch("cli.commands.asset_gen.run_command",
                       return_value={"success": True, "message": "OK", "data": {}}) as mock_run:
                result = runner.invoke(asset_gen, args)
                return result, mock_run
    return _invoke


class TestImportModelFileRegistration:
    def test_tool_registered_under_asset_gen_group(self):
        tools = get_registered_tools()
        tool = next((t for t in tools if t["name"] == "import_model_file"), None)
        assert tool is not None
        assert tool["group"] == "asset_gen"


class TestImportModelFileRouting:
    def test_routes_to_command_with_param_mapping(self):
        _, sent = _call_tool(
            source_path="/tmp/cube.fbx", name="Cube",
            output_folder="Assets/Generated/Imported", target_size=2.0,
        )
        assert _sent_command(sent) == COMMAND
        params = _sent_params(sent)
        assert params["sourcePath"] == "/tmp/cube.fbx"
        assert params["name"] == "Cube"
        assert params["outputFolder"] == "Assets/Generated/Imported"
        assert params["targetSize"] == 2.0
        assert "source_path" not in params and "output_folder" not in params and "target_size" not in params

    def test_none_values_stripped(self):
        _, sent = _call_tool(source_path="/tmp/x.obj")
        assert _sent_params(sent) == {"sourcePath": "/tmp/x.obj"}

    def test_animation_type_mapped(self):
        _, sent = _call_tool(source_path="/tmp/rig.fbx", animation_type="generic")
        params = _sent_params(sent)
        assert params["animationType"] == "generic"
        assert "animation_type" not in params
        assert set(params.keys()).issubset(ALLOWED_KEYS)

    def test_no_secret_keys_in_payload(self):
        _, sent = _call_tool(
            source_path="/tmp/a.glb", name="N",
            output_folder="Assets/Generated/Imported", target_size=1.0,
        )
        params = _sent_params(sent)
        assert set(params.keys()).issubset(ALLOWED_KEYS)
        joined = " ".join(params.keys()).lower()
        for forbidden in ("key", "secret", "token", "apikey", "password"):
            assert forbidden not in joined

    def test_non_dict_response_guarded(self):
        ctx = MagicMock()
        with patch.object(mod, "get_unity_instance_from_context",
                          new=AsyncMock(return_value="u")):
            with patch.object(mod, "send_with_unity_instance",
                              new=AsyncMock(return_value=None)):
                result = asyncio.run(import_model_file(ctx, source_path="/tmp/x.obj"))
        assert result["success"] is False


class TestImportModelFileCLI:
    def test_import_model_file_cli(self, cli_runner):
        result, mock_run = cli_runner([
            "import-model-file", "--source-path", "/tmp/cube.fbx",
            "--name", "Cube", "--output-folder", "Assets/Props", "--target-size", "1.5",
        ])
        assert result.exit_code == 0
        command = mock_run.call_args.args[0]
        params = mock_run.call_args.args[1]
        assert command == COMMAND
        assert params["sourcePath"] == "/tmp/cube.fbx"
        assert params["name"] == "Cube"
        assert params["outputFolder"] == "Assets/Props"
        assert params["targetSize"] == 1.5
        assert set(params.keys()).issubset(ALLOWED_KEYS)

    def test_import_model_file_cli_animation_type(self, cli_runner):
        result, mock_run = cli_runner([
            "import-model-file", "--source-path", "/tmp/rig.fbx", "--animation-type", "humanoid",
        ])
        assert result.exit_code == 0
        params = mock_run.call_args.args[1]
        assert params["animationType"] == "humanoid"
        assert set(params.keys()).issubset(ALLOWED_KEYS)
