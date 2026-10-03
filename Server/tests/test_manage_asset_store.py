"""Tests for manage_asset_store tool and CLI commands."""

import asyncio
import pytest
from unittest.mock import patch, MagicMock, AsyncMock
from click.testing import CliRunner

from cli.commands.asset_store import asset_store
from cli.utils.config import CLIConfig
from services.tools.manage_asset_store import ALL_ACTIONS


# =============================================================================
# Fixtures
# =============================================================================

@pytest.fixture
def runner():
    """Return a Click CLI test runner."""
    return CliRunner()


@pytest.fixture
def mock_config():
    """Return a default CLIConfig for testing."""
    return CLIConfig(
        host="127.0.0.1",
        port=8080,
        timeout=30,
        format="text",
        unity_instance=None,
    )


@pytest.fixture
def mock_success():
    """Return a generic success response."""
    return {"success": True, "message": "OK", "data": {}}


@pytest.fixture
def cli_runner(runner, mock_config, mock_success):
    """Invoke an asset-store CLI command with run_command mocked out.

    Usage::

        def test_something(cli_runner):
            result, mock_run = cli_runner(["list"])
            assert result.exit_code == 0
            params = mock_run.call_args.args[1]
            assert params["action"] == "list_purchases"
    """
    def _invoke(args):
        with patch("cli.commands.asset_store.get_config", return_value=mock_config):
            with patch("cli.commands.asset_store.run_command", return_value=mock_success) as mock_run:
                result = runner.invoke(asset_store, args)
                return result, mock_run
    return _invoke


# =============================================================================
# Action Lists
# =============================================================================

class TestActionLists:
    """Verify action list completeness and consistency."""

    def test_all_actions_is_not_empty(self):
        assert len(ALL_ACTIONS) > 0

    def test_no_duplicate_actions(self):
        assert len(ALL_ACTIONS) == len(set(ALL_ACTIONS))

    def test_expected_auth_actions_present(self):
        expected = {"check_auth"}
        assert expected.issubset(set(ALL_ACTIONS))

    def test_expected_query_actions_present(self):
        expected = {"list_purchases"}
        assert expected.issubset(set(ALL_ACTIONS))

    def test_expected_install_actions_present(self):
        expected = {"download", "import"}
        assert expected.issubset(set(ALL_ACTIONS))


# =============================================================================
# Tool Validation (Python-side, no Unity)
# =============================================================================

class TestManageAssetStoreToolValidation:
    """Test action validation in the manage_asset_store tool function."""

    def test_unknown_action_returns_error(self):
        from services.tools.manage_asset_store import manage_asset_store

        ctx = MagicMock()
        ctx.get_state = AsyncMock(return_value=None)

        result = asyncio.run(manage_asset_store(ctx, action="invalid_action"))
        assert result["success"] is False
        assert "Unknown action" in result["message"]

    def test_unknown_action_lists_valid_actions(self):
        from services.tools.manage_asset_store import manage_asset_store

        ctx = MagicMock()
        ctx.get_state = AsyncMock(return_value=None)

        result = asyncio.run(manage_asset_store(ctx, action="bogus"))
        assert result["success"] is False
        assert "Valid actions" in result["message"]

    def test_unknown_action_does_not_call_unity(self):
        from services.tools.manage_asset_store import manage_asset_store

        ctx = MagicMock()
        ctx.get_state = AsyncMock(return_value=None)

        with patch(
            "services.tools.manage_asset_store._send_asset_store_command",
            new_callable=AsyncMock,
        ) as mock_send:
            asyncio.run(manage_asset_store(ctx, action="bogus"))
            mock_send.assert_not_called()

    def test_action_matching_is_case_insensitive(self):
        from services.tools.manage_asset_store import manage_asset_store

        ctx = MagicMock()
        ctx.get_state = AsyncMock(return_value=None)

        with patch(
            "services.tools.manage_asset_store._send_asset_store_command",
            new_callable=AsyncMock,
        ) as mock_send:
            mock_send.return_value = {"success": True, "message": "OK"}
            result = asyncio.run(manage_asset_store(ctx, action="CHECK_AUTH"))

        assert result["success"] is True
        sent_params = mock_send.call_args.args[1]
        assert sent_params["action"] == "check_auth"


# =============================================================================
# CLI Command Parameter Building
# =============================================================================

class TestAssetStoreQueryCLICommands:
    """Verify query CLI commands build correct parameter dicts."""

    def test_auth_builds_correct_params(self, cli_runner):
        result, mock_run = cli_runner(["auth"])
        assert result.exit_code == 0
        mock_run.assert_called_once()
        params = mock_run.call_args.args[1]
        assert params["action"] == "check_auth"

    def test_list_builds_correct_params(self, cli_runner):
        result, mock_run = cli_runner(["list"])
        assert result.exit_code == 0
        params = mock_run.call_args.args[1]
        assert params["action"] == "list_purchases"
        assert "page" not in params
        assert "page_size" not in params

    def test_list_with_pagination(self, cli_runner):
        result, mock_run = cli_runner(["list", "--page", "2", "--page-size", "10"])
        assert result.exit_code == 0
        params = mock_run.call_args.args[1]
        assert params["action"] == "list_purchases"
        assert params["page"] == 2
        assert params["page_size"] == 10



class TestAssetStoreInstallCLICommands:
    """Verify download/import CLI commands build correct parameter dicts."""

    def test_download_builds_correct_params(self, cli_runner):
        result, mock_run = cli_runner(["download", "12345"])
        assert result.exit_code == 0
        params = mock_run.call_args.args[1]
        assert params["action"] == "download"
        assert params["product_id"] == 12345

    def test_import_builds_correct_params(self, cli_runner):
        result, mock_run = cli_runner(["import", "12345"])
        assert result.exit_code == 0
        params = mock_run.call_args.args[1]
        assert params["action"] == "import"
        assert params["product_id"] == 12345

