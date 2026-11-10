"""Tests for the import_model asset-gen tool and CLI command (Sketchfab).

Pass-through tool: NO API keys, NO file bytes. Unity transport fully mocked.
"""

import asyncio
import pytest
from unittest.mock import patch, MagicMock, AsyncMock
from click.testing import CliRunner

from cli.commands.asset_gen import asset_gen
from cli.utils.config import CLIConfig
from services.registry import get_registered_tools

from services.tools import import_model as import_model_module
from services.tools.import_model import import_model


COMMAND = "import_model"
ALLOWED_KEYS = {
    "action", "query", "categories", "downloadable", "count", "cursor",
    "uid", "targetSize", "name", "outputFolder", "jobId",
}


def _call_tool(**kwargs):
    ctx = MagicMock()
    with patch.object(import_model_module, "get_unity_instance_from_context",
                      new=AsyncMock(return_value="unity-1")):
        with patch.object(import_model_module, "send_with_unity_instance",
                          new=AsyncMock(return_value={"success": True, "data": {}})) as mock_send:
            result = asyncio.run(import_model(ctx, **kwargs))
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


class TestImportModelRegistration:
    def test_tool_registered_under_asset_gen_group(self):
        tools = get_registered_tools()
        tool = next((t for t in tools if t["name"] == "import_model"), None)
        assert tool is not None
        assert tool["group"] == "asset_gen"


class TestImportModelRouting:
    def test_search_routes_to_command(self):
        _, sent = _call_tool(action="search", query="sword", count=10)
        assert _sent_command(sent) == COMMAND
        params = _sent_params(sent)
        assert params["action"] == "search"
        assert params["query"] == "sword"
        assert params["count"] == 10

    def test_preview_routes_with_uid(self):
        _, sent = _call_tool(action="preview", uid="u123")
        assert _sent_params(sent) == {"action": "preview", "uid": "u123"}

    def test_import_routes_with_param_mapping(self):
        _, sent = _call_tool(
            action="import", uid="u9", target_size=2.0,
            name="Prop", output_folder="Assets/Generated/Sketchfab",
        )
        params = _sent_params(sent)
        assert _sent_command(sent) == COMMAND
        assert params["uid"] == "u9"
        assert params["targetSize"] == 2.0
        assert params["outputFolder"] == "Assets/Generated/Sketchfab"
        assert "target_size" not in params and "output_folder" not in params

    def test_status_and_job_id_mapping(self):
        _, sent = _call_tool(action="status", job_id="j77")
        assert _sent_params(sent) == {"action": "status", "jobId": "j77"}

    def test_action_is_lowercased(self):
        _, sent = _call_tool(action="SEARCH", query="x")
        assert _sent_params(sent)["action"] == "search"

    def test_none_values_stripped(self):
        _, sent = _call_tool(action="search", query="boat")
        assert _sent_params(sent) == {"action": "search", "query": "boat"}

    def test_no_secret_keys_in_payload(self):
        _, sent = _call_tool(
            action="search", query="q", categories="cars", downloadable=True,
            count=5, cursor="c1", uid="u", target_size=1.0, name="N",
            output_folder="Assets/Generated/Sketchfab", job_id="j",
        )
        params = _sent_params(sent)
        assert set(params.keys()).issubset(ALLOWED_KEYS)
        joined = " ".join(params.keys()).lower()
        for forbidden in ("key", "secret", "token", "apikey", "password"):
            assert forbidden not in joined

    def test_non_dict_response_guarded(self):
        ctx = MagicMock()
        with patch.object(import_model_module, "get_unity_instance_from_context",
                          new=AsyncMock(return_value="u")):
            with patch.object(import_model_module, "send_with_unity_instance",
                              new=AsyncMock(return_value=None)):
                result = asyncio.run(import_model(ctx, action="status", job_id="j"))
        assert result["success"] is False


class TestImportModelCLI:
    def test_import_model_cli(self, cli_runner):
        result, mock_run = cli_runner([
            "import-model", "--uid", "abc123", "--name", "Prop",
            "--output-folder", "Assets/Props",
        ])
        assert result.exit_code == 0
        command = mock_run.call_args.args[0]
        params = mock_run.call_args.args[1]
        assert command == COMMAND
        assert params["action"] == "import"
        assert params["uid"] == "abc123"
        assert params["name"] == "Prop"
        assert params["outputFolder"] == "Assets/Props"
        assert set(params.keys()).issubset(ALLOWED_KEYS)
