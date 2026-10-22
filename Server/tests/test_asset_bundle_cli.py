import json
from types import SimpleNamespace
from unittest.mock import Mock

import pytest
from click.testing import CliRunner

import cli.commands.asset as module


@pytest.mark.parametrize("command,action", [
    ("bundles", "list_asset_bundles"), ("bundle-info", "get_asset_bundle_info"),
])
@pytest.mark.parametrize("paging,limit,page", [([], 25, 1), (["--limit", "2", "--page", "3"], 2, 3)])
def test_bundle_cli_sends_the_read_only_action(monkeypatch, command, action, paging, limit, page):
    config = SimpleNamespace(format="json")
    monkeypatch.setattr(module, "get_config", lambda: config)
    send = Mock(return_value={"success": True, "data": {"entries": []}})
    monkeypatch.setattr(module, "run_command", send)

    result = CliRunner().invoke(module.asset, [command, "Builds/AssetBundles", *paging])

    assert result.exit_code == 0, result.output
    assert json.loads(result.output)["success"]
    send.assert_called_once_with("manage_asset", {
        "action": action, "path": "Builds/AssetBundles",
        "pageSize": limit, "pageNumber": page,
    }, config)


@pytest.mark.parametrize("command", ["bundles", "bundle-info"])
@pytest.mark.parametrize("option", [["--limit", "0"], ["--limit", "201"], ["--page", "0"]])
def test_invalid_bundle_cli_paging_never_sends_a_command(monkeypatch, command, option):
    send = Mock()
    monkeypatch.setattr(module, "run_command", send)

    result = CliRunner().invoke(module.asset, [command, "Builds", *option])

    assert result.exit_code == 2
    send.assert_not_called()
