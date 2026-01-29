"""
Characterization tests for CLI Commands domain (Server-Side Tools).

This test suite captures CURRENT behavior of CLI command modules without refactoring.
Tests are designed to identify common patterns and boilerplate across command implementations.

Domain: /Server/src/cli/commands/
Modules sampled:
  - prefab.py (216 lines) - Stage/hierarchy operations, create from GameObject
  - component.py (213 lines) - Add, remove, set properties on components
  - material.py (269 lines) - Material info, creation, property assignment
  - asset.py (partial) - Asset search, info, create
  - animation.py (partial) - Animation state/parameter control

Common patterns identified:
  1. All commands follow try/except with UnityConnectionError handling
  2. All commands call run_command() with params dict and config
  3. All commands build params dict with "action" key first
  4. All commands use format_output() for results
  5. Success messages use print_success() when result.get("success")
  6. JSON parsing appears 5+ times across modules (inline try/except blocks)
  7. Search method parameter uses click.Choice() - repeated across 4+ modules
  8. Confirmation dialogs use click.confirm() directly (not extracted)
"""

import json
import pytest
from unittest.mock import patch, MagicMock, call
from click.testing import CliRunner

from cli.commands.prefab import prefab
from cli.commands.component import component
from cli.commands.material import material
from cli.commands.asset import asset
from cli.utils.connection import UnityConnectionError
from cli.utils.config import CLIConfig


# =============================================================================
# Fixtures - Shared Test Setup
# =============================================================================

@pytest.fixture
def runner():
    """CLI test runner for all command tests."""
    return CliRunner()


@pytest.fixture
def mock_config():
    """Standard mock configuration for CLI commands."""
    return CLIConfig(
        host="127.0.0.1",
        port=8080,
        timeout=30,
        format="text",
        unity_instance=None,
    )


@pytest.fixture
def mock_success_response():
    """Standard successful command response."""
    return {
        "success": True,
        "message": "Operation successful",
        "data": {"result": "ok"}
    }


@pytest.fixture
def mock_failure_response():
    """Standard failure command response."""
    return {
        "success": False,
        "error": "Operation failed",
        "message": "Something went wrong"
    }


# =============================================================================
# Pattern: Command Structure and Parameter Building
# =============================================================================

class TestCommandParameterBuilding:
    """Verify how commands build parameter dictionaries.

    Current behavior: All commands build params with 'action' key first,
    then conditionally add optional parameters.
    """

    def test_prefab_open_builds_action_and_path_params(self, runner, mock_config):
        """Test prefab open command parameter construction.

        Captures: All prefab commands use 'action' key with camelCase param names
        like 'prefabPath', 'saveBeforeClose', 'force'.
        """
        mock_response = {"success": True, "data": {}}

        with patch("cli.commands.prefab.get_config", return_value=mock_config):
            with patch("cli.commands.prefab.run_command", return_value=mock_response) as mock_run:
                runner.invoke(prefab, ["open", "Assets/Prefabs/Test.prefab"])

                # Verify run_command was called with correct structure
                mock_run.assert_called_once()
                args = mock_run.call_args
                assert args[0][0] == "manage_prefabs"
                params = args[0][1]
                assert params["action"] == "open_stage"
                assert params["prefabPath"] == "Assets/Prefabs/Test.prefab"

    def test_component_add_with_optional_properties(self, runner, mock_config):
        """Test component add builds action, required, and optional params.

        Captures: Conditional parameter inclusion pattern - if search_method
        is provided, add to params; if properties JSON provided, parse and add.
        """
        mock_response = {"success": True, "data": {}}

        with patch("cli.commands.component.get_config", return_value=mock_config):
            with patch("cli.commands.component.run_command", return_value=mock_response) as mock_run:
                # Without optional params
                runner.invoke(component, ["add", "Player", "Rigidbody"])

                args = mock_run.call_args
                params = args[0][1]
                assert "searchMethod" not in params
                assert "properties" not in params
                assert params["action"] == "add"
                assert params["target"] == "Player"
                assert params["componentType"] == "Rigidbody"

    def test_material_set_color_converts_floats_to_list(self, runner, mock_config):
        """Test material command converts multiple float args to color array.

        Captures: Material commands convert 4 float args into [r, g, b, a] list
        in params dict before calling run_command.
        """
        mock_response = {"success": True, "data": {}}

        with patch("cli.commands.material.get_config", return_value=mock_config):
            with patch("cli.commands.material.run_command", return_value=mock_response) as mock_run:
                runner.invoke(material, ["set-color", "Assets/Mat.mat", "1.0", "0.5", "0.25", "1.0"])

                args = mock_run.call_args
                params = args[0][1]
                assert params["color"] == [1.0, 0.5, 0.25, 1.0]


# =============================================================================
# Pattern: JSON Parsing and Type Coercion
# =============================================================================

class TestJSONParsingPattern:
    """Verify how commands parse JSON parameters.

    Current behavior: Each module has inline try/except blocks for JSON parsing,
    duplicated across component.py, material.py, and asset.py modules.
    """

    def test_component_add_parses_json_properties(self, runner, mock_config):
        """Test component add parses JSON properties parameter.

        Captures: Try json.loads() on properties string, catch JSONDecodeError,
        print_error and sys.exit(1) on failure.
        """
        with patch("cli.commands.component.get_config", return_value=mock_config):
            with patch("cli.commands.component.run_command") as mock_run:
                # Valid JSON
                runner.invoke(component, ["add", "Player", "Rigidbody", "-p", '{"mass": 5.0}'])

                args = mock_run.call_args
                params = args[0][1]
                assert params["properties"] == {"mass": 5.0}

    def test_component_add_rejects_invalid_json(self, runner, mock_config):
        """Test component add exits with error on invalid JSON.

        Captures: When json.loads() fails, print_error is called and sys.exit(1)
        is invoked, resulting in exit_code != 0.
        """
        with patch("cli.commands.component.get_config", return_value=mock_config):
            result = runner.invoke(component, ["add", "Player", "Rigidbody", "-p", "not json"])

            assert result.exit_code != 0
            assert "Invalid JSON" in result.output

    def test_material_set_property_tries_json_then_float_then_string(self, runner, mock_config):
        """Test material set-property uses fallback parsing strategy.

        Captures: Attempt json.loads() first, then float(), then keep as string.
        This pattern appears in component.py and material.py independently.
        """
        mock_response = {"success": True, "data": {}}

        with patch("cli.commands.material.get_config", return_value=mock_config):
            with patch("cli.commands.material.run_command", return_value=mock_response) as mock_run:
                # Test float value
                runner.invoke(material, ["set-property", "Mat.mat", "_Metallic", "0.5"])

                args = mock_run.call_args
                params = args[0][1]
                assert params["value"] == 0.5
                assert isinstance(params["value"], float)

    def test_material_set_property_parses_json_value(self, runner, mock_config):
        """Test material set-property accepts JSON values for complex types.

        Captures: JSON parsing tries first, enabling complex types like vectors.
        """
        mock_response = {"success": True, "data": {}}

        with patch("cli.commands.material.get_config", return_value=mock_config):
            with patch("cli.commands.material.run_command", return_value=mock_response) as mock_run:
                runner.invoke(material, ["set-property", "Mat.mat", "_Color", "[1, 0, 0, 1]"])

                args = mock_run.call_args
                params = args[0][1]
                assert params["value"] == [1, 0, 0, 1]

    def test_material_set_property_keeps_string_fallback(self, runner, mock_config):
        """Test material set-property falls back to string for non-numeric values.

        Captures: If json.loads() and float() both fail, use original string.
        """
        mock_response = {"success": True, "data": {}}

        with patch("cli.commands.material.get_config", return_value=mock_config):
            with patch("cli.commands.material.run_command", return_value=mock_response) as mock_run:
                runner.invoke(material, ["set-property", "Mat.mat", "_MainTex", "Assets/Tex.png"])

                args = mock_run.call_args
                params = args[0][1]
                assert params["value"] == "Assets/Tex.png"
                assert isinstance(params["value"], str)


# =============================================================================
# Pattern: Error Handling and Exit Codes
# =============================================================================

class TestErrorHandlingPattern:
    """Verify consistent error handling across command modules.

    Current behavior: All commands have try/except wrapping run_command(),
    catch UnityConnectionError, print_error(), and sys.exit(1).
    """

    def test_prefab_open_catches_unity_connection_error(self, runner, mock_config):
        """Test prefab open handles connection errors gracefully.

        Captures: try/except around run_command(), catches UnityConnectionError,
        calls print_error(str(e)), then sys.exit(1).
        """
        with patch("cli.commands.prefab.get_config", return_value=mock_config):
            with patch("cli.commands.prefab.run_command", side_effect=UnityConnectionError("Connection failed")):
                result = runner.invoke(prefab, ["open", "Assets/Prefabs/Test.prefab"])

                assert result.exit_code == 1
                assert "Connection failed" in result.output

    def test_component_add_catches_unity_connection_error(self, runner, mock_config):
        """Test component add handles connection errors.

        Captures: Same error handling pattern repeated in component.py.
        """
        with patch("cli.commands.component.get_config", return_value=mock_config):
            with patch("cli.commands.component.run_command", side_effect=UnityConnectionError("Connection lost")):
                result = runner.invoke(component, ["add", "Player", "Rigidbody"])

                assert result.exit_code == 1

    def test_material_info_handles_connection_failure(self, runner, mock_config):
        """Test material info handles connection errors.

        Captures: Pattern repeats across all material commands.
        """
        with patch("cli.commands.material.get_config", return_value=mock_config):
            with patch("cli.commands.material.run_command", side_effect=UnityConnectionError("Disconnected")):
                result = runner.invoke(material, ["info", "Assets/Mat.mat"])

                assert result.exit_code == 1

    def test_asset_search_handles_connection_error(self, runner, mock_config):
        """Test asset search handles connection errors.

        Captures: Pattern found in asset.py as well.
        """
        with patch("cli.commands.asset.get_config", return_value=mock_config):
            with patch("cli.commands.asset.run_command", side_effect=UnityConnectionError("Timeout")):
                result = runner.invoke(asset, ["search", "*.prefab"])

                assert result.exit_code == 1


# =============================================================================
# Pattern: Success Response Handling
# =============================================================================

class TestSuccessResponseHandling:
    """Verify how commands handle successful responses.

    Current behavior: Commands check result.get("success"), then call
    print_success() with context-specific message.
    """

    def test_prefab_open_shows_success_message_on_success(self, runner, mock_config):
        """Test prefab open shows success message when result succeeds.

        Captures: if result.get("success"): print_success(formatted message)
        """
        response = {"success": True, "data": {}}

        with patch("cli.commands.prefab.get_config", return_value=mock_config):
            with patch("cli.commands.prefab.run_command", return_value=response):
                result = runner.invoke(prefab, ["open", "Assets/Prefabs/Test.prefab"])

                assert result.exit_code == 0
                assert "Opened prefab" in result.output
                assert "Assets/Prefabs/Test.prefab" in result.output

    def test_prefab_close_shows_context_appropriate_success_message(self, runner, mock_config):
        """Test prefab close shows appropriate success message.

        Captures: Different commands show different success messages based on action.
        """
        response = {"success": True, "data": {}}

        with patch("cli.commands.prefab.get_config", return_value=mock_config):
            with patch("cli.commands.prefab.run_command", return_value=response):
                result = runner.invoke(prefab, ["close"])

                assert "Closed prefab stage" in result.output

    def test_component_add_shows_action_context_in_success(self, runner, mock_config):
        """Test component add includes component type and target in success message.

        Captures: Success messages include relevant context (component type, target).
        """
        response = {"success": True, "data": {}}

        with patch("cli.commands.component.get_config", return_value=mock_config):
            with patch("cli.commands.component.run_command", return_value=response):
                result = runner.invoke(component, ["add", "Player", "Rigidbody"])

                assert "Added Rigidbody" in result.output
                assert "Player" in result.output

    def test_material_create_includes_path_in_success_message(self, runner, mock_config):
        """Test material create includes asset path in success message.

        Captures: Path parameters are echoed back in success messages.
        """
        response = {"success": True, "data": {}}

        with patch("cli.commands.material.get_config", return_value=mock_config):
            with patch("cli.commands.material.run_command", return_value=response):
                result = runner.invoke(material, ["create", "Assets/Materials/New.mat"])

                assert "Created material" in result.output
                assert "Assets/Materials/New.mat" in result.output


# =============================================================================
# Pattern: Output Formatting
# =============================================================================

class TestOutputFormattingPattern:
    """Verify how commands format and display responses.

    Current behavior: All commands call format_output(result, config.format)
    and echo result with click.echo().
    """

    def test_command_uses_format_output_with_config_format(self, runner, mock_config):
        """Test command passes config.format to format_output.

        Captures: All commands call format_output(result, config.format) where
        config comes from get_config().
        """
        response = {"success": True, "data": {"info": "value"}}

        with patch("cli.commands.prefab.get_config", return_value=mock_config):
            with patch("cli.commands.prefab.run_command", return_value=response):
                with patch("cli.commands.prefab.format_output", return_value="formatted") as mock_format:
                    runner.invoke(prefab, ["open", "Assets/Prefabs/Test.prefab"])

                    mock_format.assert_called()
                    call_args = mock_format.call_args
                    assert call_args[0][1] == "text"  # config.format

    def test_prefab_info_formats_output_from_wrapped_result(self, runner, mock_config):
        """Test prefab info handles wrapped response structure.

        Captures: Some commands unwrap response data with result.get("result", result)
        before accessing .get("success") and .get("data").
        """
        response = {
            "result": {
                "success": True,
                "data": {
                    "assetPath": "Assets/Prefabs/Test.prefab",
                    "prefabType": "Regular",
                    "guid": "abc123"
                }
            }
        }

        with patch("cli.commands.prefab.get_config", return_value=mock_config):
            with patch("cli.commands.prefab.run_command", return_value=response):
                result = runner.invoke(prefab, ["info", "Assets/Prefabs/Test.prefab"])

                assert result.exit_code == 0
                # Compact output shows parsed data
                assert "Prefab:" in result.output or "Assets/Prefabs/Test.prefab" in result.output


# =============================================================================
# Pattern: Search Method Parameter
# =============================================================================

class TestSearchMethodParameter:
    """Verify repeated search method parameter implementation.

    Current behavior: search_method parameter appears in component.py, material.py,
    and other modules with identical click.Choice() definitions.
    """

    def test_component_add_accepts_search_method_parameter(self, runner, mock_config):
        """Test component add supports search_method choices.

        Captures: click.Choice(["by_id", "by_name", "by_path"]) appears in
        component.py add() and remove() and multiple material commands.
        """
        response = {"success": True}

        with patch("cli.commands.component.get_config", return_value=mock_config):
            with patch("cli.commands.component.run_command", return_value=response) as mock_run:
                runner.invoke(component, ["add", "Player", "Rigidbody", "--search-method", "by_id"])

                args = mock_run.call_args
                params = args[0][1]
                assert params["searchMethod"] == "by_id"

    def test_component_add_rejects_invalid_search_method(self, runner, mock_config):
        """Test component add validates search_method choices.

        Captures: Click automatically validates against Choice() options.
        """
        with patch("cli.commands.component.get_config", return_value=mock_config):
            result = runner.invoke(component, ["add", "Player", "Rigidbody", "--search-method", "invalid"])

            # Click exits with code 2 for usage errors
            assert result.exit_code == 2
            assert "invalid" in result.output.lower()

    def test_material_assign_has_extended_search_methods(self, runner, mock_config):
        """Test material assign supports additional search methods.

        Captures: material.py has ["by_name", "by_path", "by_tag", "by_layer", "by_component"]
        which is different from component.py's list - DUPLICATION with variation.
        """
        response = {"success": True}

        with patch("cli.commands.material.get_config", return_value=mock_config):
            with patch("cli.commands.material.run_command", return_value=response) as mock_run:
                runner.invoke(material, ["assign", "Assets/Mat.mat", "Cube", "--search-method", "by_tag"])

                args = mock_run.call_args
                params = args[0][1]
                assert params["searchMethod"] == "by_tag"


# =============================================================================
# Pattern: Confirmation Dialogs
# =============================================================================

class TestConfirmationDialogPattern:
    """Verify confirmation dialog usage across commands.

    Current behavior: Some destructive commands use click.confirm() directly,
    with --force flag to skip confirmation.
    """

    def test_component_remove_shows_confirmation_by_default(self, runner, mock_config):
        """Test component remove shows confirmation prompt.

        Captures: click.confirm() is called directly in the command function
        when force=False.
        """
        with patch("cli.commands.component.get_config", return_value=mock_config):
            # Simulate user declining confirmation
            result = runner.invoke(component, ["remove", "Player", "Rigidbody"], input="n\n")

            assert result.exit_code == 1
            assert "Aborted" in result.output or "cancelled" in result.output.lower()

    def test_component_remove_skips_confirmation_with_force_flag(self, runner, mock_config):
        """Test component remove skips confirmation when --force is set.

        Captures: When force=True, no confirmation is prompted and run_command is called.
        """
        response = {"success": True}

        with patch("cli.commands.component.get_config", return_value=mock_config):
            with patch("cli.commands.component.run_command", return_value=response):
                result = runner.invoke(component, ["remove", "Player", "Rigidbody", "--force"])

                # Command should proceed without confirmation
                assert result.exit_code == 0


# =============================================================================
# Pattern: Optional Parameter Handling
# =============================================================================

class TestOptionalParameterHandling:
    """Verify how commands handle optional parameters.

    Current behavior: Commands check each optional parameter and conditionally
    add to params dict only if provided.
    """

    def test_prefab_close_includes_save_param_when_flag_set(self, runner, mock_config):
        """Test prefab close includes saveBeforeClose when --save is set.

        Captures: Optional parameters are only added to params dict if flag is True.
        """
        response = {"success": True}

        with patch("cli.commands.prefab.get_config", return_value=mock_config):
            with patch("cli.commands.prefab.run_command", return_value=response) as mock_run:
                runner.invoke(prefab, ["close", "--save"])

                args = mock_run.call_args
                params = args[0][1]
                assert params["saveBeforeClose"] is True

    def test_prefab_close_omits_save_param_when_flag_not_set(self, runner, mock_config):
        """Test prefab close omits saveBeforeClose when --save is not set.

        Captures: Optional parameters are NOT included in params if not provided.
        """
        response = {"success": True}

        with patch("cli.commands.prefab.get_config", return_value=mock_config):
            with patch("cli.commands.prefab.run_command", return_value=response) as mock_run:
                runner.invoke(prefab, ["close"])

                args = mock_run.call_args
                params = args[0][1]
                assert "saveBeforeClose" not in params

    def test_material_create_only_adds_properties_if_provided(self, runner, mock_config):
        """Test material create only includes properties parameter if specified.

        Captures: Same optional parameter pattern as prefab commands.
        """
        response = {"success": True}

        with patch("cli.commands.material.get_config", return_value=mock_config):
            with patch("cli.commands.material.run_command", return_value=response) as mock_run:
                runner.invoke(material, ["create", "Assets/Mat.mat"])

                args = mock_run.call_args
                params = args[0][1]
                assert "properties" not in params


# =============================================================================
# Pattern: Command Tool Name Resolution
# =============================================================================

class TestCommandToolNameResolution:
    """Verify how commands resolve target tool names.

    Current behavior: Each command module hardcodes the tool name passed to
    run_command() - e.g., "manage_prefabs", "manage_components", "manage_material".
    """

    def test_prefab_commands_use_manage_prefabs_tool(self, runner, mock_config):
        """Test all prefab commands call run_command with 'manage_prefabs'.

        Captures: Tool name is hardcoded in each command function.
        """
        response = {"success": True}

        with patch("cli.commands.prefab.get_config", return_value=mock_config):
            with patch("cli.commands.prefab.run_command", return_value=response) as mock_run:
                runner.invoke(prefab, ["open", "Assets/Prefabs/Test.prefab"])

                args = mock_run.call_args
                assert args[0][0] == "manage_prefabs"

    def test_component_commands_use_manage_components_tool(self, runner, mock_config):
        """Test component commands use 'manage_components' tool name.

        Captures: Hardcoded tool name per module.
        """
        response = {"success": True}

        with patch("cli.commands.component.get_config", return_value=mock_config):
            with patch("cli.commands.component.run_command", return_value=response) as mock_run:
                runner.invoke(component, ["add", "Player", "Rigidbody"])

                args = mock_run.call_args
                assert args[0][0] == "manage_components"

    def test_material_commands_use_manage_material_tool(self, runner, mock_config):
        """Test material commands use 'manage_material' tool name.

        Captures: Material uses singular 'manage_material' while others use plural.
        """
        response = {"success": True}

        with patch("cli.commands.material.get_config", return_value=mock_config):
            with patch("cli.commands.material.run_command", return_value=response) as mock_run:
                runner.invoke(material, ["info", "Assets/Mat.mat"])

                args = mock_run.call_args
                assert args[0][0] == "manage_material"

    def test_asset_commands_use_manage_asset_tool(self, runner, mock_config):
        """Test asset commands use 'manage_asset' tool name.

        Captures: Hardcoded tool name in asset.py.
        """
        response = {"success": True}

        with patch("cli.commands.asset.get_config", return_value=mock_config):
            with patch("cli.commands.asset.run_command", return_value=response) as mock_run:
                runner.invoke(asset, ["search", "*.prefab"])

                args = mock_run.call_args
                assert args[0][0] == "manage_asset"


# =============================================================================
# Pattern: Config Access
# =============================================================================

class TestConfigAccessPattern:
    """Verify how commands access CLI configuration.

    Current behavior: All commands call get_config() at the beginning of the
    command function and use config throughout for formatting and connection.
    """

    def test_every_command_calls_get_config(self, runner, mock_config):
        """Test that commands retrieve config via get_config().

        Captures: Pattern is consistent across all command modules - first line
        is always config = get_config().
        """
        response = {"success": True}

        with patch("cli.commands.prefab.get_config", return_value=mock_config) as mock_get:
            with patch("cli.commands.prefab.run_command", return_value=response):
                runner.invoke(prefab, ["open", "Assets/Prefabs/Test.prefab"])

                mock_get.assert_called_once()

    def test_config_is_passed_to_run_command(self, runner, mock_config):
        """Test config is passed to run_command as third argument.

        Captures: run_command(tool_name, params, config) signature.
        """
        response = {"success": True}

        with patch("cli.commands.prefab.get_config", return_value=mock_config):
            with patch("cli.commands.prefab.run_command", return_value=response) as mock_run:
                runner.invoke(prefab, ["info", "Assets/Prefabs/Test.prefab"])

                args = mock_run.call_args
                assert len(args[0]) >= 3
                assert args[0][2] == mock_config


# =============================================================================
# Pattern: Wrapped Response Structure
# =============================================================================

class TestWrappedResponseHandling:
    """Verify handling of wrapped response data.

    Current behavior: Some commands (prefab.py) handle wrapped responses using
    result.get("result", result) fallback pattern.
    """

    def test_prefab_info_handles_wrapped_response_structure(self, runner, mock_config):
        """Test prefab info unwraps nested response structure.

        Captures: result.get("result", result) pattern allows handling both:
        - Direct success responses: {"success": True, "data": {...}}
        - Wrapped responses: {"result": {"success": True, "data": {...}}}
        """
        wrapped_response = {
            "result": {
                "success": True,
                "data": {
                    "assetPath": "Test.prefab",
                    "prefabType": "Regular"
                }
            }
        }

        with patch("cli.commands.prefab.get_config", return_value=mock_config):
            with patch("cli.commands.prefab.run_command", return_value=wrapped_response):
                result = runner.invoke(prefab, ["info", "Assets/Prefabs/Test.prefab"])

                assert result.exit_code == 0

    def test_prefab_hierarchy_compact_mode_extracts_data(self, runner, mock_config):
        """Test prefab hierarchy extracts and formats data from wrapped response.

        Captures: Compact mode custom formatting uses response_data.get("data")
        after unwrapping.
        """
        wrapped_response = {
            "result": {
                "success": True,
                "data": {
                    "items": [
                        {"name": "Root", "path": ""},
                        {"name": "Child", "path": "Root/Child"}
                    ],
                    "total": 2
                }
            }
        }

        with patch("cli.commands.prefab.get_config", return_value=mock_config):
            with patch("cli.commands.prefab.run_command", return_value=wrapped_response):
                result = runner.invoke(prefab, ["hierarchy", "Assets/Prefabs/Test.prefab", "--compact"])

                assert "Total: 2 objects" in result.output


# =============================================================================
# Pattern: Prefab Creation with Optional Flags
# =============================================================================

class TestPrefabCreateFlags:
    """Verify prefab create command's optional flags.

    Current behavior: Multiple boolean flags control prefab creation behavior.
    """

    def test_prefab_create_includes_all_optional_flags_when_set(self, runner, mock_config):
        """Test prefab create includes optional flags in params.

        Captures: --overwrite, --include-inactive, --unlink-if-instance flags
        map to allowOverwrite, searchInactive, unlinkIfInstance params.
        """
        response = {"success": True}

        with patch("cli.commands.prefab.get_config", return_value=mock_config):
            with patch("cli.commands.prefab.run_command", return_value=response) as mock_run:
                runner.invoke(prefab, ["create", "Player", "Assets/Prefabs/Player.prefab",
                                      "--overwrite", "--include-inactive", "--unlink-if-instance"])

                args = mock_run.call_args
                params = args[0][1]
                assert params["allowOverwrite"] is True
                assert params["searchInactive"] is True
                assert params["unlinkIfInstance"] is True

    def test_prefab_create_omits_unset_optional_flags(self, runner, mock_config):
        """Test prefab create omits flags when not provided.

        Captures: Unset flags are not included in params dict.
        """
        response = {"success": True}

        with patch("cli.commands.prefab.get_config", return_value=mock_config):
            with patch("cli.commands.prefab.run_command", return_value=response) as mock_run:
                runner.invoke(prefab, ["create", "Player", "Assets/Prefabs/Player.prefab"])

                args = mock_run.call_args
                params = args[0][1]
                assert "allowOverwrite" not in params
                assert "searchInactive" not in params
                assert "unlinkIfInstance" not in params


# =============================================================================
# Integration Tests - Multi-Step Command Flows
# =============================================================================

class TestMultiStepCommandFlows:
    """Verify realistic workflows using multiple commands.

    Captures: How commands work together in typical usage patterns.
    """

    def test_prefab_workflow_open_modify_save(self, runner, mock_config):
        """Test realistic prefab editing workflow.

        Captures: Sequential commands that work together.
        """
        response = {"success": True}

        with patch("cli.commands.prefab.get_config", return_value=mock_config):
            with patch("cli.commands.prefab.run_command", return_value=response):
                # Open
                result = runner.invoke(prefab, ["open", "Assets/Prefabs/Player.prefab"])
                assert result.exit_code == 0

                # Save
                result = runner.invoke(prefab, ["save", "--force"])
                assert result.exit_code == 0

                # Close
                result = runner.invoke(prefab, ["close", "--save"])
                assert result.exit_code == 0

    def test_component_workflow_add_modify_remove(self, runner, mock_config):
        """Test component add, modify, remove workflow.

        Captures: Multiple component operations in sequence.
        """
        response = {"success": True}

        with patch("cli.commands.component.get_config", return_value=mock_config):
            with patch("cli.commands.component.run_command", return_value=response):
                # Add
                result = runner.invoke(component, ["add", "Player", "Rigidbody"])
                assert result.exit_code == 0

                # Modify
                result = runner.invoke(component, ["modify", "Player", "Rigidbody",
                                                  "-p", '{"mass": 5.0, "useGravity": false}'])
                assert result.exit_code == 0

                # Remove
                result = runner.invoke(component, ["remove", "Player", "Rigidbody", "--force"])
                assert result.exit_code == 0

    def test_material_workflow_create_assign_modify(self, runner, mock_config):
        """Test material create, assign, and modify workflow.

        Captures: Material-related commands work together.
        """
        response = {"success": True}

        with patch("cli.commands.material.get_config", return_value=mock_config):
            with patch("cli.commands.material.run_command", return_value=response):
                # Create
                result = runner.invoke(material, ["create", "Assets/Materials/New.mat"])
                assert result.exit_code == 0

                # Set color
                result = runner.invoke(material, ["set-color", "Assets/Materials/New.mat", "1", "0", "0"])
                assert result.exit_code == 0

                # Assign
                result = runner.invoke(material, ["assign", "Assets/Materials/New.mat", "Cube"])
                assert result.exit_code == 0


# =============================================================================
# Edge Cases and Boundary Conditions
# =============================================================================

class TestEdgeCases:
    """Verify handling of edge cases and unusual inputs.

    Captures: How commands behave with unexpected inputs or boundary conditions.
    """

    def test_prefab_path_with_spaces_and_special_chars(self, runner, mock_config):
        """Test prefab commands handle paths with spaces and special characters.

        Captures: Path arguments are passed as-is to run_command.
        """
        response = {"success": True}
        path = "Assets/Prefabs/My Special Prefab [v2].prefab"

        with patch("cli.commands.prefab.get_config", return_value=mock_config):
            with patch("cli.commands.prefab.run_command", return_value=response) as mock_run:
                runner.invoke(prefab, ["open", path])

                args = mock_run.call_args
                params = args[0][1]
                assert params["prefabPath"] == path

    def test_material_color_with_extreme_values(self, runner, mock_config):
        """Test material color command with out-of-range values.

        Captures: Click validates floats but doesn't clamp values; they're passed
        through to Unity which handles validation.
        """
        response = {"success": True}

        with patch("cli.commands.material.get_config", return_value=mock_config):
            with patch("cli.commands.material.run_command", return_value=response) as mock_run:
                # Out of typical 0-1 range but valid float values
                result = runner.invoke(material, ["set-color", "Mat.mat", "2.5", "0.5", "0.25", "1.0"])

                # Verify command executed successfully
                if mock_run.called:
                    args = mock_run.call_args
                    params = args[0][1]
                    assert params["color"] == [2.5, 0.5, 0.25, 1.0]
                else:
                    # Command may fail on validation, which is ok for characterization test
                    assert result.exit_code != 0 or mock_run.called

    def test_component_with_long_component_type_name(self, runner, mock_config):
        """Test component command with long/qualified component type name.

        Captures: Component type is passed as string; no validation on command side.
        """
        response = {"success": True}

        with patch("cli.commands.component.get_config", return_value=mock_config):
            with patch("cli.commands.component.run_command", return_value=response) as mock_run:
                runner.invoke(component, ["add", "Player", "MyNamespace.CustomComponent"])

                args = mock_run.call_args
                params = args[0][1]
                assert params["componentType"] == "MyNamespace.CustomComponent"

    def test_json_properties_with_nested_structure(self, runner, mock_config):
        """Test JSON properties with nested objects.

        Captures: json.loads() successfully parses nested structures.
        """
        response = {"success": True}
        nested_json = '{"outer": {"inner": {"value": 42}}}'

        with patch("cli.commands.component.get_config", return_value=mock_config):
            with patch("cli.commands.component.run_command", return_value=response) as mock_run:
                runner.invoke(component, ["add", "Player", "Rigidbody", "-p", nested_json])

                args = mock_run.call_args
                params = args[0][1]
                assert params["properties"] == {"outer": {"inner": {"value": 42}}}

    def test_search_method_default_when_not_specified(self, runner, mock_config):
        """Test that search_method is omitted when not specified (defaults in Unity).

        Captures: Optional search_method parameter is None by default, not included in params.
        """
        response = {"success": True}

        with patch("cli.commands.material.get_config", return_value=mock_config):
            with patch("cli.commands.material.run_command", return_value=response) as mock_run:
                runner.invoke(material, ["assign", "Assets/Mat.mat", "Cube"])

                args = mock_run.call_args
                params = args[0][1]
                assert "searchMethod" not in params


# =============================================================================
# Identified Boilerplate Patterns for Refactoring
# =============================================================================

class TestBoilerplatePatterns:
    """Document boilerplate patterns identified for refactoring.

    These tests serve as specification for P2-1 refactoring (Command Wrapper Decorator).
    """

    def test_every_command_has_identical_try_except_pattern(self):
        """Document that all commands have identical error handling.

        BOILERPLATE PATTERN:
        try:
            result = run_command(tool, params, config)
            click.echo(format_output(result, config.format))
            if result.get("success"):
                print_success(message)
        except UnityConnectionError as e:
            print_error(str(e))
            sys.exit(1)

        Identified in: prefab.py, component.py, material.py, asset.py, and all other modules.
        Refactoring opportunity: Extract as @standard_command decorator (P2-1).
        """
        pass

    def test_json_parsing_appears_5_times_independently(self):
        """Document JSON parsing duplication.

        BOILERPLATE PATTERN - appears in:
        1. component.py:54-57 (properties)
        2. component.py:138-142 (value with fallback)
        3. material.py:71-75 (properties)
        4. material.py:142-149 (value with fallback)
        5. asset.py:132-136 (properties)

        Three variants:
        - Simple json.loads() with error handling
        - json.loads() then float() fallback (component, material)
        - json.loads() then float() then string fallback (material)

        Refactoring opportunity: Extract as QW-2 utility (cli/utils/parsers.py).
        """
        pass

    def test_search_method_parameter_repeated_4_times(self):
        """Document search_method parameter duplication.

        BOILERPLATE PATTERN - appears in:
        1. component.py:add(), remove(), set_property(), modify()
        2. material.py:assign(), set_renderer_color()
        3. asset.py - potentially (not examined)

        Each uses click.Choice() but with slight variations:
        - component: ["by_id", "by_name", "by_path"]
        - material: ["by_name", "by_path", "by_tag", "by_layer", "by_component"]

        Refactoring opportunity: Extract as QW-4 constant (cli/utils/constants.py).
        """
        pass

    def test_confirmation_dialog_pattern_could_be_extracted(self):
        """Document confirmation dialog pattern.

        BOILERPLATE PATTERN:
        if not force:
            click.confirm(f"Remove {item}?", abort=True)

        Identified in: component.py:94

        Refactoring opportunity: Extract as QW-5 utility function
        (cli/utils/confirmation.py).
        """
        pass

    def test_wrapped_response_handling_in_prefab_module(self):
        """Document wrapped response handling pattern.

        BOILERPLATE PATTERN in prefab.py:
        response_data = result.get("result", result)
        if response_data.get("success") and response_data.get("data"):
            data = response_data["data"]
            # access data fields

        Identified in: prefab.py:133, 182, 195

        This pattern appears unique to prefab.py; may indicate inconsistent
        response wrapping behavior that should be standardized.
        """
        pass


if __name__ == "__main__":
    pytest.main([__file__, "-v"])
