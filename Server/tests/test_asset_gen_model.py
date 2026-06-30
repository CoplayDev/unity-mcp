"""Tests for the generate_model asset-gen tool and CLI command.

These are pass-through tools: they carry NO API keys and NO file bytes. The Unity
transport is fully mocked, mirroring test_manage_packages.py.
"""

import asyncio
import pytest
from unittest.mock import patch, MagicMock, AsyncMock
from click.testing import CliRunner

from cli.commands.asset_gen import asset_gen
from cli.utils.config import CLIConfig
from services.registry import get_registered_tools

# Importing the module registers the tool in the global registry.
from services.tools import generate_model as generate_model_module
from services.tools.generate_model import generate_model


COMMAND = "generate_model"
# Every camelCase key the tool is allowed to send. Crucially, no key/secret param.
ALLOWED_KEYS = {
    "action", "provider", "mode", "prompt", "imagePath", "imageUrl",
    "format", "targetSize", "texture", "tier", "name", "outputFolder", "jobId",
}


# =============================================================================
# Helpers / fixtures
# =============================================================================

def _call_tool(**kwargs):
    """Invoke generate_model with the Unity transport mocked; return (result, sent_args)."""
    ctx = MagicMock()
    with patch.object(generate_model_module, "get_unity_instance_from_context",
                      new=AsyncMock(return_value="unity-1")):
        with patch.object(generate_model_module, "send_with_unity_instance",
                          new=AsyncMock(return_value={"success": True, "data": {}})) as mock_send:
            result = asyncio.run(generate_model(ctx, **kwargs))
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


# =============================================================================
# Registration
# =============================================================================

class TestGenerateModelRegistration:
    def test_tool_registered_under_asset_gen_group(self):
        tools = get_registered_tools()
        tool = next((t for t in tools if t["name"] == "generate_model"), None)
        assert tool is not None
        assert tool["group"] == "asset_gen"


# =============================================================================
# Action routing + param mapping
# =============================================================================

class TestGenerateModelRouting:
    def test_generate_routes_to_command(self):
        _, sent = _call_tool(action="generate", provider="tripo", mode="text", prompt="a chair")
        assert _sent_command(sent) == COMMAND
        assert _sent_params(sent)["action"] == "generate"

    def test_status_routes_with_job_id(self):
        _, sent = _call_tool(action="status", job_id="abc123")
        assert _sent_command(sent) == COMMAND
        params = _sent_params(sent)
        assert params["action"] == "status"
        assert params["jobId"] == "abc123"

    def test_cancel_routes_with_job_id(self):
        _, sent = _call_tool(action="cancel", job_id="zzz")
        assert _sent_params(sent) == {"action": "cancel", "jobId": "zzz"}

    def test_list_providers_routes(self):
        _, sent = _call_tool(action="list_providers")
        assert _sent_params(sent) == {"action": "list_providers"}

    def test_action_is_lowercased(self):
        _, sent = _call_tool(action="STATUS", job_id="abc")
        assert _sent_params(sent)["action"] == "status"

    def test_param_camelcase_mapping(self):
        _, sent = _call_tool(
            action="generate",
            image_path="Assets/ref.png",
            image_url="http://x/y.png",
            target_size=1.5,
            output_folder="Assets/Generated/Models",
            job_id="j1",
        )
        params = _sent_params(sent)
        assert params["imagePath"] == "Assets/ref.png"
        assert params["imageUrl"] == "http://x/y.png"
        assert params["targetSize"] == 1.5
        assert params["outputFolder"] == "Assets/Generated/Models"
        assert params["jobId"] == "j1"
        # snake_case keys must never reach Unity
        for snake in ("image_path", "image_url", "target_size", "output_folder", "job_id"):
            assert snake not in params

    def test_none_values_stripped(self):
        _, sent = _call_tool(action="generate", provider="tripo", prompt="x")
        params = _sent_params(sent)
        assert params == {"action": "generate", "provider": "tripo", "prompt": "x"}
        assert all(v is not None for v in params.values())

    def test_no_secret_keys_in_payload(self):
        """The payload must never carry a key/secret; keys are a subset of the allowed set."""
        _, sent = _call_tool(
            action="generate", provider="tripo", mode="image", prompt="p",
            image_path="a.png", image_url="b", format="fbx", target_size=2.0,
            texture=True, tier="standard", name="Chair",
            output_folder="Assets/Generated/Models", job_id="j",
        )
        params = _sent_params(sent)
        assert set(params.keys()).issubset(ALLOWED_KEYS)
        joined = " ".join(params.keys()).lower()
        for forbidden in ("key", "secret", "token", "apikey", "password"):
            assert forbidden not in joined

    def test_non_dict_response_guarded(self):
        ctx = MagicMock()
        with patch.object(generate_model_module, "get_unity_instance_from_context",
                          new=AsyncMock(return_value="u")):
            with patch.object(generate_model_module, "send_with_unity_instance",
                              new=AsyncMock(return_value="oops")):
                result = asyncio.run(generate_model(ctx, action="status", job_id="j"))
        assert result["success"] is False
        assert "oops" in result["message"]


# =============================================================================
# CLI smoke
# =============================================================================

class TestGenerateModelCLI:
    def test_generate_model_cli(self, cli_runner):
        result, mock_run = cli_runner([
            "generate-model", "--provider", "tripo", "--mode", "text",
            "--prompt", "a chair", "--target-size", "1.5",
        ])
        assert result.exit_code == 0
        command = mock_run.call_args.args[0]
        params = mock_run.call_args.args[1]
        assert command == COMMAND
        assert params["action"] == "generate"
        assert params["provider"] == "tripo"
        assert params["targetSize"] == 1.5
        assert set(params.keys()).issubset(ALLOWED_KEYS)

    def test_status_cli(self, cli_runner):
        result, mock_run = cli_runner(["status", "--job-id", "abc123"])
        assert result.exit_code == 0
        command = mock_run.call_args.args[0]
        params = mock_run.call_args.args[1]
        assert command == COMMAND
        assert params == {"action": "status", "jobId": "abc123"}
