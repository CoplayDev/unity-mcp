"""Tests for the generate_audio asset-gen tool and CLI command (fal.ai).

Pass-through tool: NO API keys, NO file bytes. Unity transport fully mocked.
"""

import asyncio
import pytest
from unittest.mock import patch, MagicMock, AsyncMock
from click.testing import CliRunner

from cli.commands.asset_gen import asset_gen
from cli.utils.config import CLIConfig
from services.registry import get_registered_tools

from services.tools import generate_audio as generate_audio_module
from services.tools.generate_audio import generate_audio


COMMAND = "generate_audio"
ALLOWED_KEYS = {
    "action", "provider", "prompt", "model", "duration", "name", "outputFolder", "jobId",
}


def _call_tool(**kwargs):
    ctx = MagicMock()
    with patch.object(generate_audio_module, "get_unity_instance_from_context",
                      new=AsyncMock(return_value="unity-1")):
        with patch.object(generate_audio_module, "send_with_unity_instance",
                          new=AsyncMock(return_value={"success": True, "data": {}})) as mock_send:
            result = asyncio.run(generate_audio(ctx, **kwargs))
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


class TestGenerateAudioRegistration:
    def test_tool_registered_under_asset_gen_group(self):
        tools = get_registered_tools()
        tool = next((t for t in tools if t["name"] == "generate_audio"), None)
        assert tool is not None
        assert tool["group"] == "asset_gen"


class TestGenerateAudioRouting:
    def test_generate_routes_to_command(self):
        _, sent = _call_tool(action="generate", provider="fal", prompt="8-bit coin pickup")
        assert _sent_command(sent) == COMMAND
        assert _sent_params(sent)["action"] == "generate"

    def test_status_and_job_id_mapping(self):
        _, sent = _call_tool(action="status", job_id="j5")
        assert _sent_params(sent) == {"action": "status", "jobId": "j5"}

    def test_param_camelcase_mapping(self):
        _, sent = _call_tool(
            action="generate", provider="fal", prompt="rain", duration=30.0,
            output_folder="Assets/Generated/Audio",
        )
        params = _sent_params(sent)
        assert params["outputFolder"] == "Assets/Generated/Audio"
        assert params["duration"] == 30.0
        for snake in ("output_folder", "job_id"):
            assert snake not in params

    def test_action_is_lowercased(self):
        _, sent = _call_tool(action="GENERATE", prompt="p")
        assert _sent_params(sent)["action"] == "generate"

    def test_omitting_model_drops_it(self):
        # Load-bearing precondition for the GUI-selected default: an omitted model must NOT be
        # sent, so the C# side falls back to the panel selection / catalog default.
        _, sent = _call_tool(action="generate", provider="fal", prompt="p")
        params = _sent_params(sent)
        assert "model" not in params
        assert params == {"action": "generate", "provider": "fal", "prompt": "p"}

    def test_explicit_model_passes_through(self):
        _, sent = _call_tool(action="generate", provider="fal", prompt="p",
                             model="cassetteai/sound-effects-generator")
        assert _sent_params(sent)["model"] == "cassetteai/sound-effects-generator"

    def test_no_secret_keys_in_payload(self):
        _, sent = _call_tool(
            action="generate", provider="fal", prompt="p",
            model="fal-ai/stable-audio-25/text-to-audio", duration=12.5, name="Sfx",
            output_folder="Assets/Generated/Audio", job_id="j",
        )
        params = _sent_params(sent)
        assert set(params.keys()).issubset(ALLOWED_KEYS)
        joined = " ".join(params.keys()).lower()
        for forbidden in ("key", "secret", "token", "apikey", "password"):
            assert forbidden not in joined

    def test_non_dict_response_guarded(self):
        ctx = MagicMock()
        with patch.object(generate_audio_module, "get_unity_instance_from_context",
                          new=AsyncMock(return_value="u")):
            with patch.object(generate_audio_module, "send_with_unity_instance",
                              new=AsyncMock(return_value=42)):
                result = asyncio.run(generate_audio(ctx, action="status", job_id="j"))
        assert result["success"] is False
        assert "42" in result["message"]


class TestGenerateAudioCLI:
    def test_generate_audio_cli(self, cli_runner):
        result, mock_run = cli_runner([
            "generate-audio", "--provider", "fal", "--prompt", "ambient", "--duration", "30",
        ])
        assert result.exit_code == 0
        command = mock_run.call_args.args[0]
        params = mock_run.call_args.args[1]
        assert command == COMMAND
        assert params["action"] == "generate"
        assert params["provider"] == "fal"
        assert params["duration"] == 30.0
        assert set(params.keys()).issubset(ALLOWED_KEYS)
