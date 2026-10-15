"""Model discovery actions retain catalog state across the MCP/CLI boundary."""

import asyncio
import importlib
from typing import get_args, get_type_hints
from unittest.mock import AsyncMock, MagicMock, patch

import pytest
from click.testing import CliRunner

from cli.commands.asset_gen import asset_gen
from cli.utils.config import CLIConfig


@pytest.mark.parametrize("kind,action", [
    ("audio", "list_models"), ("audio", "refresh_models"),
    ("image", "list_models"), ("image", "refresh_models"),
    ("model", "list_models"),
    ("model", "refresh_models"),
])
def test_discovery_actions_are_advertised_and_preserve_catalog_response(kind, action):
    module = importlib.import_module(f"services.tools.generate_{kind}")
    tool = getattr(module, f"generate_{kind}")
    action_type = get_args(get_type_hints(tool, include_extras=True)["action"])[0]
    assert action in get_args(action_type)
    catalog = {"success": True, "data": {
        "models": [{"id": "test/music", "status": "verified"}],
        "catalogs": [{"source": "cache", "stale": True, "refreshing": True,
                      "refresh_error": "HTTP 429", "last_verified": "2026-10-01T12:00:00Z"}],
    }}
    with patch.object(module, "get_unity_instance_from_context", AsyncMock(return_value="unity-1")), \
         patch.object(module, "send_with_unity_instance", AsyncMock(return_value=catalog)) as send:
        result = asyncio.run(tool(MagicMock(), action=action, provider="fal" if kind != "model" else "tripo"))
    assert result == catalog
    assert send.call_args.args[2] == f"generate_{kind}"
    assert send.call_args.args[3] == {"action": action, "provider": "fal" if kind != "model" else "tripo"}


@pytest.mark.parametrize("kind,refresh", [("audio", False), ("image", True), ("model", False)])
def test_cli_lists_catalog_without_generation_or_key_parameters(kind, refresh):
    config = CLIConfig(host="127.0.0.1", port=8080, timeout=30, format="json", unity_instance=None)
    arguments = ["list-models", "--kind", kind, "--provider", "fal" if kind != "model" else "meshy"]
    if refresh:
        arguments.append("--refresh")
    with patch("cli.commands.asset_gen.get_config", return_value=config), \
         patch("cli.commands.asset_gen.run_command", return_value={"success": True, "data": {"models": []}}) as run:
        result = CliRunner().invoke(asset_gen, arguments)
    assert result.exit_code == 0, result.output
    assert run.call_args.args == (f"generate_{kind}", {
        "action": "refresh_models" if refresh else "list_models",
        "provider": "fal" if kind != "model" else "meshy",
    }, config)


@pytest.mark.parametrize("arguments", [
    ["list-models", "--kind", "model", "--provider", "meshy", "--refresh"],
    ["list-models", "--kind", "audio", "--provider", "openrouter", "--refresh"],
])
def test_cli_rejects_unsupported_live_refresh_before_contacting_unity(arguments):
    with patch("cli.commands.asset_gen.run_command") as run:
        result = CliRunner().invoke(asset_gen, arguments)
    assert result.exit_code != 0
    assert "supports fal" in result.output
    run.assert_not_called()


@pytest.mark.parametrize("kind,provider", [("model", "fal"), ("image", "openrouter")])
def test_cli_refreshes_new_catalogs_and_forwards_search_and_paging(kind, provider):
    config = CLIConfig(host="127.0.0.1", port=8080, timeout=30, format="json", unity_instance=None)
    with patch("cli.commands.asset_gen.get_config", return_value=config), \
         patch("cli.commands.asset_gen.run_command", return_value={"success": True}) as run:
        result = CliRunner().invoke(asset_gen, ["list-models", "--kind", kind, "--provider", provider,
            "--refresh", "--search", "flux", "--mode", "image", "--limit", "20", "--offset", "40"])
    assert result.exit_code == 0, result.output
    assert run.call_args.args[1] == {"action": "refresh_models", "provider": provider,
        "search": "flux", "mode": "image", "limit": 20, "offset": 40}


@pytest.mark.parametrize("kind", ["audio", "image", "model"])
def test_mcp_forwards_discovery_filters(kind):
    module = importlib.import_module(f"services.tools.generate_{kind}")
    tool = getattr(module, f"generate_{kind}")
    with patch.object(module, "get_unity_instance_from_context", AsyncMock(return_value="unity-1")), \
         patch.object(module, "send_with_unity_instance", AsyncMock(return_value={"success": True})) as send:
        asyncio.run(tool(MagicMock(), action="list_models", search="new", limit=10, offset=20))
    assert send.call_args.args[3] == {"action": "list_models", "search": "new", "limit": 10, "offset": 20}
