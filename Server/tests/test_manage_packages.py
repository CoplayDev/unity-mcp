"""Tests for manage_packages tool and CLI commands."""

import asyncio
import pytest
from unittest.mock import patch, MagicMock, AsyncMock
from click.testing import CliRunner

from cli.commands.packages import packages
from cli.utils.config import CLIConfig
from services.tools.manage_packages import ALL_ACTIONS


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
    """Invoke a packages CLI command with run_command mocked out.

    Usage::

        def test_something(cli_runner):
            result, mock_run = cli_runner(["list"])
            assert result.exit_code == 0
            params = mock_run.call_args.args[1]
            assert params["action"] == "list_packages"
    """
    def _invoke(args):
        with patch("cli.commands.packages.get_config", return_value=mock_config):
            with patch("cli.commands.packages.run_command", return_value=mock_success) as mock_run:
                result = runner.invoke(packages, args)
                return result, mock_run
    return _invoke


# =============================================================================
# Action Lists
# =============================================================================

class TestActionLists:
    """Verify action list completeness and consistency."""

    def test_all_actions_is_not_empty(self):
        """ALL_ACTIONS must contain at least one entry."""
        assert len(ALL_ACTIONS) > 0

    def test_no_duplicate_actions(self):
        """ALL_ACTIONS must not contain duplicate entries."""
        assert len(ALL_ACTIONS) == len(set(ALL_ACTIONS))

    def test_expected_query_actions_present(self):
        """Query actions must all be present in ALL_ACTIONS."""
        expected = {"list_packages", "search_packages", "get_package_info", "ping", "status"}
        assert expected.issubset(set(ALL_ACTIONS))

    def test_expected_install_remove_actions_present(self):
        """Install/remove actions must all be present in ALL_ACTIONS."""
        expected = {"add_package", "remove_package", "embed_package", "resolve_packages"}
        assert expected.issubset(set(ALL_ACTIONS))

    def test_expected_registry_actions_present(self):
        """Registry actions must all be present in ALL_ACTIONS."""
        expected = {"add_registry", "remove_registry", "list_registries"}
        assert expected.issubset(set(ALL_ACTIONS))


# =============================================================================
# Tool Validation (Python-side, no Unity)
# =============================================================================

class TestManagePackagesToolValidation:
    """Test action validation in the manage_packages tool function."""

    def test_unknown_action_returns_error(self):
        """An unrecognised action must return success=False with an error message."""
        from services.tools.manage_packages import manage_packages

        ctx = MagicMock()
        ctx.get_state = AsyncMock(return_value=None)

        result = asyncio.run(manage_packages(ctx, action="invalid_action"))
        assert result["success"] is False
        assert "Unknown action" in result["message"]

    def test_unknown_action_lists_valid_actions(self):
        """The error message for an unknown action must list valid actions."""
        from services.tools.manage_packages import manage_packages

        ctx = MagicMock()
        ctx.get_state = AsyncMock(return_value=None)

        result = asyncio.run(manage_packages(ctx, action="bogus"))
        assert result["success"] is False
        assert "Valid actions" in result["message"]

    def test_unknown_action_does_not_call_unity(self):
        """An unknown action must be rejected before any Unity call is made."""
        from services.tools.manage_packages import manage_packages

        ctx = MagicMock()
        ctx.get_state = AsyncMock(return_value=None)

        with patch(
            "services.tools.manage_packages._send_packages_command",
            new_callable=AsyncMock,
        ) as mock_send:
            asyncio.run(manage_packages(ctx, action="bogus"))
            mock_send.assert_not_called()

    def test_action_matching_is_case_insensitive(self):
        """Actions must be accepted regardless of capitalisation and normalised to lowercase."""
        from services.tools.manage_packages import manage_packages

        ctx = MagicMock()
        ctx.get_state = AsyncMock(return_value=None)

        with patch(
            "services.tools.manage_packages._send_packages_command",
            new_callable=AsyncMock,
        ) as mock_send:
            mock_send.return_value = {"success": True, "message": "OK"}
            result = asyncio.run(manage_packages(ctx, action="LIST_PACKAGES"))

        assert result["success"] is True
        sent_params = mock_send.call_args.args[1]
        assert sent_params["action"] == "list_packages"


# =============================================================================
# CLI Command Parameter Building
# =============================================================================

class TestPackagesQueryCLICommands:
    """Verify query CLI commands build correct parameter dicts."""

    def test_list_builds_correct_params(self, cli_runner):
        """packages list must send action=list_packages."""
        result, mock_run = cli_runner(["list"])
        assert result.exit_code == 0
        mock_run.assert_called_once()
        params = mock_run.call_args.args[1]
        assert params["action"] == "list_packages"

    def test_search_builds_correct_params(self, cli_runner):
        """packages search <query> must send action=search_packages and the query."""
        result, mock_run = cli_runner(["search", "input"])
        assert result.exit_code == 0
        params = mock_run.call_args.args[1]
        assert params["action"] == "search_packages"
        assert params["query"] == "input"

    def test_info_builds_correct_params(self, cli_runner):
        """packages info <package> must send action=get_package_info and the package name."""
        result, mock_run = cli_runner(["info", "com.unity.inputsystem"])
        assert result.exit_code == 0
        params = mock_run.call_args.args[1]
        assert params["action"] == "get_package_info"
        assert params["package"] == "com.unity.inputsystem"

    def test_ping_builds_correct_params(self, cli_runner):
        """packages ping must send action=ping."""
        result, mock_run = cli_runner(["ping"])
        assert result.exit_code == 0
        params = mock_run.call_args.args[1]
        assert params["action"] == "ping"

    def test_status_without_job_id(self, cli_runner):
        """packages status with no args must send action=status and omit job_id."""
        result, mock_run = cli_runner(["status"])
        assert result.exit_code == 0
        params = mock_run.call_args.args[1]
        assert params["action"] == "status"
        assert "job_id" not in params

    def test_status_with_job_id(self, cli_runner):
        """packages status <job_id> must include the job_id in params."""
        result, mock_run = cli_runner(["status", "abc123"])
        assert result.exit_code == 0
        params = mock_run.call_args.args[1]
        assert params["action"] == "status"
        assert params["job_id"] == "abc123"


class TestPackagesInstallRemoveCLICommands:
    """Verify install/remove CLI commands build correct parameter dicts."""

    def test_add_builds_correct_params(self, cli_runner):
        """packages add <package> must send action=add_package and the package name."""
        result, mock_run = cli_runner(["add", "com.unity.inputsystem"])
        assert result.exit_code == 0
        params = mock_run.call_args.args[1]
        assert params["action"] == "add_package"
        assert params["package"] == "com.unity.inputsystem"

    def test_add_with_version_builds_correct_params(self, cli_runner):
        """packages add <package@version> must preserve the version specifier."""
        result, mock_run = cli_runner(["add", "com.unity.inputsystem@1.8.0"])
        assert result.exit_code == 0
        params = mock_run.call_args.args[1]
        assert params["action"] == "add_package"
        assert params["package"] == "com.unity.inputsystem@1.8.0"

    def test_remove_builds_correct_params(self, cli_runner):
        """packages remove <package> must send action=remove_package without force."""
        result, mock_run = cli_runner(["remove", "com.unity.inputsystem"])
        assert result.exit_code == 0
        params = mock_run.call_args.args[1]
        assert params["action"] == "remove_package"
        assert params["package"] == "com.unity.inputsystem"
        assert "force" not in params

    def test_remove_with_force_builds_correct_params(self, cli_runner):
        """packages remove --force must include force=True in params."""
        result, mock_run = cli_runner(["remove", "com.unity.inputsystem", "--force"])
        assert result.exit_code == 0
        params = mock_run.call_args.args[1]
        assert params["action"] == "remove_package"
        assert params["force"] is True

    def test_embed_builds_correct_params(self, cli_runner):
        """packages embed <package> must send action=embed_package and the package name."""
        result, mock_run = cli_runner(["embed", "com.unity.timeline"])
        assert result.exit_code == 0
        params = mock_run.call_args.args[1]
        assert params["action"] == "embed_package"
        assert params["package"] == "com.unity.timeline"

    def test_resolve_builds_correct_params(self, cli_runner):
        """packages resolve must send action=resolve_packages."""
        result, mock_run = cli_runner(["resolve"])
        assert result.exit_code == 0
        params = mock_run.call_args.args[1]
        assert params["action"] == "resolve_packages"


class TestRegistryCLICommands:
    """Verify registry CLI commands build correct parameter dicts."""

    def test_list_registries_builds_correct_params(self, cli_runner):
        """packages list-registries must send action=list_registries."""
        result, mock_run = cli_runner(["list-registries"])
        assert result.exit_code == 0
        params = mock_run.call_args.args[1]
        assert params["action"] == "list_registries"

    def test_add_registry_builds_correct_params(self, cli_runner):
        """packages add-registry must send name, url, and all scopes."""
        result, mock_run = cli_runner([
            "add-registry", "OpenUPM",
            "--url", "https://package.openupm.com",
            "--scope", "com.cysharp",
            "--scope", "com.neuecc",
        ])
        assert result.exit_code == 0
        params = mock_run.call_args.args[1]
        assert params["action"] == "add_registry"
        assert params["name"] == "OpenUPM"
        assert params["url"] == "https://package.openupm.com"
        assert params["scopes"] == ["com.cysharp", "com.neuecc"]

    def test_add_registry_with_single_scope(self, cli_runner):
        """packages add-registry with one --scope must produce a single-element scopes list."""
        result, mock_run = cli_runner([
            "add-registry", "MyReg",
            "--url", "https://registry.example.com",
            "--scope", "com.example",
        ])
        assert result.exit_code == 0
        params = mock_run.call_args.args[1]
        assert params["scopes"] == ["com.example"]

    def test_remove_registry_builds_correct_params(self, cli_runner):
        """packages remove-registry <name> must send action=remove_registry and the name."""
        result, mock_run = cli_runner(["remove-registry", "OpenUPM"])
        assert result.exit_code == 0
        params = mock_run.call_args.args[1]
        assert params["action"] == "remove_registry"
        assert params["name"] == "OpenUPM"
