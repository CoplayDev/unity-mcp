"""Tests for the generate_image asset-gen tool and CLI command (fal/OpenRouter).

Pass-through tool: NO API keys, NO file bytes. Unity transport fully mocked.
"""

import asyncio
import pytest
from unittest.mock import patch, MagicMock, AsyncMock
from click.testing import CliRunner

from cli.commands.asset_gen import asset_gen
from cli.utils.config import CLIConfig
from services.registry import get_registered_tools

from services.tools import generate_image as generate_image_module
from services.tools.generate_image import generate_image


COMMAND = "generate_image"
ALLOWED_KEYS = {
    "action", "provider", "mode", "prompt", "imagePath", "imageUrl",
    "model", "transparent", "width", "height", "name", "outputFolder", "jobId",
}


def _call_tool(**kwargs):
    ctx = MagicMock()
    with patch.object(generate_image_module, "get_unity_instance_from_context",
                      new=AsyncMock(return_value="unity-1")):
        with patch.object(generate_image_module, "send_with_unity_instance",
                          new=AsyncMock(return_value={"success": True, "data": {}})) as mock_send:
            result = asyncio.run(generate_image(ctx, **kwargs))
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


class TestGenerateImageRegistration:
    def test_tool_registered_under_asset_gen_group(self):
        tools = get_registered_tools()
        tool = next((t for t in tools if t["name"] == "generate_image"), None)
        assert tool is not None
        assert tool["group"] == "asset_gen"


class TestGenerateImageRouting:
    def test_generate_routes_to_command(self):
        _, sent = _call_tool(action="generate", provider="fal", prompt="a stone texture")
        assert _sent_command(sent) == COMMAND
        assert _sent_params(sent)["action"] == "generate"

    def test_remove_background_routes(self):
        _, sent = _call_tool(action="remove_background", image_path="Assets/x.png")
        params = _sent_params(sent)
        assert params["action"] == "remove_background"
        assert params["imagePath"] == "Assets/x.png"
        assert "image_path" not in params

    def test_status_and_job_id_mapping(self):
        _, sent = _call_tool(action="status", job_id="j5")
        assert _sent_params(sent) == {"action": "status", "jobId": "j5"}

    def test_param_camelcase_mapping(self):
        _, sent = _call_tool(
            action="generate", provider="fal", mode="image",
            image_path="a.png", image_url="http://b", width=512, height=512,
            output_folder="Assets/Generated/Images",
        )
        params = _sent_params(sent)
        assert params["imagePath"] == "a.png"
        assert params["imageUrl"] == "http://b"
        assert params["outputFolder"] == "Assets/Generated/Images"
        assert params["width"] == 512 and params["height"] == 512
        for snake in ("image_path", "image_url", "output_folder"):
            assert snake not in params

    def test_action_is_lowercased(self):
        _, sent = _call_tool(action="GENERATE", prompt="p")
        assert _sent_params(sent)["action"] == "generate"

    def test_none_values_stripped(self):
        _, sent = _call_tool(action="generate", provider="fal", prompt="p")
        assert _sent_params(sent) == {"action": "generate", "provider": "fal", "prompt": "p"}

    def test_no_secret_keys_in_payload(self):
        _, sent = _call_tool(
            action="generate", provider="fal", mode="text", prompt="p",
            image_path="a.png", image_url="b", model="flux", transparent=True,
            width=256, height=256, name="Tex",
            output_folder="Assets/Generated/Images", job_id="j",
        )
        params = _sent_params(sent)
        assert set(params.keys()).issubset(ALLOWED_KEYS)
        joined = " ".join(params.keys()).lower()
        for forbidden in ("key", "secret", "token", "apikey", "password"):
            assert forbidden not in joined

    def test_non_dict_response_guarded(self):
        ctx = MagicMock()
        with patch.object(generate_image_module, "get_unity_instance_from_context",
                          new=AsyncMock(return_value="u")):
            with patch.object(generate_image_module, "send_with_unity_instance",
                              new=AsyncMock(return_value=42)):
                result = asyncio.run(generate_image(ctx, action="status", job_id="j"))
        assert result["success"] is False
        assert "42" in result["message"]


class TestGenerateImageCLI:
    def test_generate_image_cli(self, cli_runner):
        result, mock_run = cli_runner([
            "generate-image", "--provider", "fal", "--prompt", "a stone texture",
            "--width", "512", "--transparent",
        ])
        assert result.exit_code == 0
        command = mock_run.call_args.args[0]
        params = mock_run.call_args.args[1]
        assert command == COMMAND
        assert params["action"] == "generate"
        assert params["provider"] == "fal"
        assert params["width"] == 512
        assert params["transparent"] is True
        assert set(params.keys()).issubset(ALLOWED_KEYS)
