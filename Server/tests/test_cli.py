"""Unit tests for Unity MCP CLI."""

import json
import pytest
from unittest.mock import patch, MagicMock, AsyncMock
from click.testing import CliRunner

from cli.main import cli
from cli.utils.config import CLIConfig, get_config, set_config
from cli.utils.output import format_output, format_as_json, format_as_text, format_as_table
from cli.utils.connection import (
    send_command,
    check_connection,
    list_unity_instances,
    UnityConnectionError,
)


# =============================================================================
# Fixtures
# =============================================================================

@pytest.fixture
def runner():
    """Create a CLI test runner."""
    return CliRunner()


@pytest.fixture
def mock_config():
    """Create a mock CLI configuration."""
    return CLIConfig(
        host="127.0.0.1",
        port=8080,
        timeout=30,
        format="text",
        unity_instance=None,
    )


@pytest.fixture
def mock_unity_response():
    """Standard successful Unity response."""
    return {
        "success": True,
        "message": "Operation successful",
        "data": {"test": "data"}
    }


@pytest.fixture
def mock_instances_response():
    """Mock Unity instances response."""
    return {
        "success": True,
        "instances": [
            {
                "session_id": "test-session-123",
                "project": "TestProject",
                "hash": "abc123def456",
                "unity_version": "2022.3.10f1",
                "connected_at": "2024-01-01T00:00:00Z",
            }
        ]
    }


@pytest.fixture
def mock_sessions_response():
    """Mock plugin sessions response (legacy format)."""
    return {
        "sessions": {
            "test-session-123": {
                "project": "TestProject",
                "hash": "abc123def456",
                "unity_version": "2022.3.10f1",
                "connected_at": "2024-01-01T00:00:00Z",
            }
        }
    }


# =============================================================================
# Config Tests
# =============================================================================

class TestConfig:
    """Tests for CLI configuration."""

    def test_default_config(self):
        """Test default configuration values."""
        config = CLIConfig()
        assert config.host == "127.0.0.1"
        assert config.port == 8080
        assert config.timeout == 30
        assert config.format == "text"
        assert config.unity_instance is None

    def test_config_from_env(self, monkeypatch):
        """Test configuration from environment variables."""
        monkeypatch.setenv("UNITY_MCP_HOST", "192.168.1.100")
        monkeypatch.setenv("UNITY_MCP_HTTP_PORT", "9090")
        monkeypatch.setenv("UNITY_MCP_TIMEOUT", "60")
        monkeypatch.setenv("UNITY_MCP_FORMAT", "json")
        monkeypatch.setenv("UNITY_MCP_INSTANCE", "MyProject")

        config = CLIConfig.from_env()
        assert config.host == "192.168.1.100"
        assert config.port == 9090
        assert config.timeout == 60
        assert config.format == "json"
        assert config.unity_instance == "MyProject"

    def test_set_and_get_config(self, mock_config):
        """Test setting and getting global config."""
        set_config(mock_config)
        retrieved = get_config()
        assert retrieved.host == mock_config.host
        assert retrieved.port == mock_config.port


# =============================================================================
# Output Formatting Tests
# =============================================================================

class TestOutputFormatting:
    """Tests for output formatting utilities."""

    def test_format_as_json(self):
        """Test JSON formatting."""
        data = {"key": "value", "number": 42}
        result = format_as_json(data)
        parsed = json.loads(result)
        assert parsed == data

    def test_format_as_json_with_complex_types(self):
        """Test JSON formatting with complex types."""
        from datetime import datetime
        data = {"timestamp": datetime(2024, 1, 1)}
        result = format_as_json(data)
        assert "2024" in result

    def test_format_as_text_success_response(self):
        """Test text formatting for success response."""
        data = {
            "success": True,
            "message": "OK",
            "data": {"name": "Player", "id": 123}
        }
        result = format_as_text(data)
        assert "name" in result
        assert "Player" in result

    def test_format_as_text_error_response(self):
        """Test text formatting for error response."""
        data = {"success": False, "error": "Something went wrong"}
        result = format_as_text(data)
        assert "Error" in result
        assert "Something went wrong" in result

    def test_format_as_text_list(self):
        """Test text formatting for lists."""
        data = [{"name": "Item1"}, {"name": "Item2"}]
        result = format_as_text(data)
        assert "2 items" in result

    def test_format_as_table(self):
        """Test table formatting."""
        data = [
            {"name": "Player", "id": 1},
            {"name": "Enemy", "id": 2},
        ]
        result = format_as_table(data)
        assert "name" in result
        assert "Player" in result
        assert "Enemy" in result

    def test_format_output_dispatch(self):
        """Test format_output dispatches correctly."""
        data = {"key": "value"}
        
        json_result = format_output(data, "json")
        assert json.loads(json_result) == data
        
        text_result = format_output(data, "text")
        assert "key" in text_result
        
        table_result = format_output(data, "table")
        assert "key" in table_result.lower() or "Key" in table_result


# =============================================================================
# Connection Tests
# =============================================================================

class TestConnection:
    """Tests for connection utilities."""

    @pytest.mark.asyncio
    async def test_check_connection_success(self):
        """Test successful connection check."""
        mock_response = MagicMock()
        mock_response.status_code = 200

        with patch("httpx.AsyncClient") as mock_client:
            mock_client.return_value.__aenter__.return_value.get = AsyncMock(
                return_value=mock_response
            )
            result = await check_connection()
            assert result is True

    @pytest.mark.asyncio
    async def test_check_connection_failure(self):
        """Test failed connection check."""
        with patch("httpx.AsyncClient") as mock_client:
            mock_client.return_value.__aenter__.return_value.get = AsyncMock(
                side_effect=Exception("Connection refused")
            )
            result = await check_connection()
            assert result is False

    @pytest.mark.asyncio
    async def test_send_command_success(self, mock_unity_response):
        """Test successful command sending."""
        mock_response = MagicMock()
        mock_response.status_code = 200
        mock_response.json.return_value = mock_unity_response

        with patch("httpx.AsyncClient") as mock_client:
            mock_client.return_value.__aenter__.return_value.post = AsyncMock(
                return_value=mock_response
            )
            mock_response.raise_for_status = MagicMock()

            result = await send_command("test_command", {"param": "value"})
            assert result == mock_unity_response

    @pytest.mark.asyncio
    async def test_send_command_connection_error(self):
        """Test command sending with connection error."""
        with patch("httpx.AsyncClient") as mock_client:
            mock_client.return_value.__aenter__.return_value.post = AsyncMock(
                side_effect=Exception("Connection refused")
            )

            with pytest.raises(UnityConnectionError):
                await send_command("test_command", {})

    @pytest.mark.asyncio
    async def test_list_instances_from_sessions(self, mock_sessions_response):
        """Test listing instances from /plugin/sessions endpoint."""
        mock_response = MagicMock()
        mock_response.status_code = 200
        mock_response.json.return_value = mock_sessions_response

        with patch("httpx.AsyncClient") as mock_client:
            # First call (api/instances) returns 404, second (plugin/sessions) succeeds
            mock_get = AsyncMock(return_value=mock_response)
            mock_client.return_value.__aenter__.return_value.get = mock_get

            result = await list_unity_instances()
            assert result["success"] is True
            assert len(result["instances"]) == 1
            assert result["instances"][0]["project"] == "TestProject"


# =============================================================================
# CLI Command Tests
# =============================================================================

class TestCLICommands:
    """Tests for CLI commands."""

    def test_cli_help(self, runner):
        """Test CLI help command."""
        result = runner.invoke(cli, ["--help"])
        assert result.exit_code == 0
        assert "Unity MCP Command Line Interface" in result.output

    def test_cli_version(self, runner):
        """Test CLI version command."""
        result = runner.invoke(cli, ["--version"])
        assert result.exit_code == 0

    def test_status_connected(self, runner, mock_instances_response):
        """Test status command when connected."""
        with patch("cli.main.run_check_connection", return_value=True):
            with patch("cli.main.run_list_instances", return_value=mock_instances_response):
                result = runner.invoke(cli, ["status"])
                assert result.exit_code == 0
                assert "Connected" in result.output

    def test_status_disconnected(self, runner):
        """Test status command when disconnected."""
        with patch("cli.main.run_check_connection", return_value=False):
            result = runner.invoke(cli, ["status"])
            assert result.exit_code == 1
            assert "Cannot connect" in result.output

    def test_instances_command(self, runner, mock_instances_response):
        """Test instances command."""
        with patch("cli.main.run_list_instances", return_value=mock_instances_response):
            result = runner.invoke(cli, ["instances"])
            assert result.exit_code == 0

    def test_raw_command(self, runner, mock_unity_response):
        """Test raw command."""
        with patch("cli.main.run_command", return_value=mock_unity_response):
            result = runner.invoke(cli, ["raw", "test_command", '{"param": "value"}'])
            assert result.exit_code == 0

    def test_raw_command_invalid_json(self, runner):
        """Test raw command with invalid JSON."""
        result = runner.invoke(cli, ["raw", "test_command", "invalid json"])
        assert result.exit_code == 1
        assert "Invalid JSON" in result.output


# =============================================================================
# GameObject Command Tests
# =============================================================================

class TestGameObjectCommands:
    """Tests for GameObject CLI commands."""

    def test_gameobject_find(self, runner, mock_unity_response):
        """Test gameobject find command."""
        with patch("cli.commands.gameobject.run_command", return_value=mock_unity_response):
            result = runner.invoke(cli, ["gameobject", "find", "Player"])
            assert result.exit_code == 0

    def test_gameobject_find_with_options(self, runner, mock_unity_response):
        """Test gameobject find with options."""
        with patch("cli.commands.gameobject.run_command", return_value=mock_unity_response):
            result = runner.invoke(cli, [
                "gameobject", "find", "Enemy",
                "--method", "by_tag",
                "--include-inactive",
                "--limit", "100"
            ])
            assert result.exit_code == 0

    def test_gameobject_create(self, runner, mock_unity_response):
        """Test gameobject create command."""
        with patch("cli.commands.gameobject.run_command", return_value=mock_unity_response):
            result = runner.invoke(cli, ["gameobject", "create", "NewObject"])
            assert result.exit_code == 0

    def test_gameobject_create_with_primitive(self, runner, mock_unity_response):
        """Test gameobject create with primitive."""
        with patch("cli.commands.gameobject.run_command", return_value=mock_unity_response):
            result = runner.invoke(cli, [
                "gameobject", "create", "MyCube",
                "--primitive", "Cube",
                "--position", "0", "1", "0"
            ])
            assert result.exit_code == 0

    def test_gameobject_modify(self, runner, mock_unity_response):
        """Test gameobject modify command."""
        with patch("cli.commands.gameobject.run_command", return_value=mock_unity_response):
            result = runner.invoke(cli, [
                "gameobject", "modify", "Player",
                "--position", "0", "5", "0"
            ])
            assert result.exit_code == 0

    def test_gameobject_delete(self, runner, mock_unity_response):
        """Test gameobject delete command."""
        with patch("cli.commands.gameobject.run_command", return_value=mock_unity_response):
            result = runner.invoke(cli, ["gameobject", "delete", "OldObject", "--force"])
            assert result.exit_code == 0

    def test_gameobject_delete_confirmation(self, runner, mock_unity_response):
        """Test gameobject delete with confirmation prompt."""
        with patch("cli.commands.gameobject.run_command", return_value=mock_unity_response):
            result = runner.invoke(cli, ["gameobject", "delete", "OldObject"], input="y\n")
            assert result.exit_code == 0

    def test_gameobject_duplicate(self, runner, mock_unity_response):
        """Test gameobject duplicate command."""
        with patch("cli.commands.gameobject.run_command", return_value=mock_unity_response):
            result = runner.invoke(cli, [
                "gameobject", "duplicate", "Player",
                "--name", "Player2",
                "--offset", "5", "0", "0"
            ])
            assert result.exit_code == 0

    def test_gameobject_move(self, runner, mock_unity_response):
        """Test gameobject move command."""
        with patch("cli.commands.gameobject.run_command", return_value=mock_unity_response):
            result = runner.invoke(cli, [
                "gameobject", "move", "Chair",
                "--reference", "Table",
                "--direction", "right",
                "--distance", "2"
            ])
            assert result.exit_code == 0


# =============================================================================
# Component Command Tests
# =============================================================================

class TestComponentCommands:
    """Tests for Component CLI commands."""

    def test_component_add(self, runner, mock_unity_response):
        """Test component add command."""
        with patch("cli.commands.component.run_command", return_value=mock_unity_response):
            result = runner.invoke(cli, ["component", "add", "Player", "Rigidbody"])
            assert result.exit_code == 0

    def test_component_remove(self, runner, mock_unity_response):
        """Test component remove command."""
        with patch("cli.commands.component.run_command", return_value=mock_unity_response):
            result = runner.invoke(cli, ["component", "remove", "Player", "Rigidbody", "--force"])
            assert result.exit_code == 0

    def test_component_set(self, runner, mock_unity_response):
        """Test component set command."""
        with patch("cli.commands.component.run_command", return_value=mock_unity_response):
            result = runner.invoke(cli, ["component", "set", "Player", "Rigidbody", "mass", "5.0"])
            assert result.exit_code == 0

    def test_component_modify(self, runner, mock_unity_response):
        """Test component modify command."""
        with patch("cli.commands.component.run_command", return_value=mock_unity_response):
            result = runner.invoke(cli, [
                "component", "modify", "Player", "Rigidbody",
                "--properties", '{"mass": 5.0, "useGravity": false}'
            ])
            assert result.exit_code == 0


# =============================================================================
# Scene Command Tests
# =============================================================================

class TestSceneCommands:
    """Tests for Scene CLI commands."""

    def test_scene_hierarchy(self, runner, mock_unity_response):
        """Test scene hierarchy command."""
        with patch("cli.commands.scene.run_command", return_value=mock_unity_response):
            result = runner.invoke(cli, ["scene", "hierarchy"])
            assert result.exit_code == 0

    def test_scene_hierarchy_with_options(self, runner, mock_unity_response):
        """Test scene hierarchy with options."""
        with patch("cli.commands.scene.run_command", return_value=mock_unity_response):
            result = runner.invoke(cli, [
                "scene", "hierarchy",
                "--max-depth", "5",
                "--include-transform"
            ])
            assert result.exit_code == 0

    def test_scene_active(self, runner, mock_unity_response):
        """Test scene active command."""
        with patch("cli.commands.scene.run_command", return_value=mock_unity_response):
            result = runner.invoke(cli, ["scene", "active"])
            assert result.exit_code == 0

    def test_scene_load(self, runner, mock_unity_response):
        """Test scene load command."""
        with patch("cli.commands.scene.run_command", return_value=mock_unity_response):
            result = runner.invoke(cli, ["scene", "load", "Assets/Scenes/Main.unity"])
            assert result.exit_code == 0

    def test_scene_save(self, runner, mock_unity_response):
        """Test scene save command."""
        with patch("cli.commands.scene.run_command", return_value=mock_unity_response):
            result = runner.invoke(cli, ["scene", "save"])
            assert result.exit_code == 0

    def test_scene_create(self, runner, mock_unity_response):
        """Test scene create command."""
        with patch("cli.commands.scene.run_command", return_value=mock_unity_response):
            result = runner.invoke(cli, ["scene", "create", "NewLevel"])
            assert result.exit_code == 0

    def test_scene_screenshot(self, runner, mock_unity_response):
        """Test scene screenshot command."""
        with patch("cli.commands.scene.run_command", return_value=mock_unity_response):
            result = runner.invoke(cli, ["scene", "screenshot", "--filename", "test"])
            assert result.exit_code == 0


# =============================================================================
# Asset Command Tests
# =============================================================================

class TestAssetCommands:
    """Tests for Asset CLI commands."""

    def test_asset_search(self, runner, mock_unity_response):
        """Test asset search command."""
        with patch("cli.commands.asset.run_command", return_value=mock_unity_response):
            result = runner.invoke(cli, ["asset", "search", "*.prefab"])
            assert result.exit_code == 0

    def test_asset_info(self, runner, mock_unity_response):
        """Test asset info command."""
        with patch("cli.commands.asset.run_command", return_value=mock_unity_response):
            result = runner.invoke(cli, ["asset", "info", "Assets/Materials/Red.mat"])
            assert result.exit_code == 0

    def test_asset_create(self, runner, mock_unity_response):
        """Test asset create command."""
        with patch("cli.commands.asset.run_command", return_value=mock_unity_response):
            result = runner.invoke(cli, ["asset", "create", "Assets/Materials/New.mat", "Material"])
            assert result.exit_code == 0

    def test_asset_delete(self, runner, mock_unity_response):
        """Test asset delete command."""
        with patch("cli.commands.asset.run_command", return_value=mock_unity_response):
            result = runner.invoke(cli, ["asset", "delete", "Assets/Old.mat", "--force"])
            assert result.exit_code == 0

    def test_asset_duplicate(self, runner, mock_unity_response):
        """Test asset duplicate command."""
        with patch("cli.commands.asset.run_command", return_value=mock_unity_response):
            result = runner.invoke(cli, [
                "asset", "duplicate",
                "Assets/Materials/Red.mat",
                "Assets/Materials/RedCopy.mat"
            ])
            assert result.exit_code == 0

    def test_asset_move(self, runner, mock_unity_response):
        """Test asset move command."""
        with patch("cli.commands.asset.run_command", return_value=mock_unity_response):
            result = runner.invoke(cli, [
                "asset", "move",
                "Assets/Old/Mat.mat",
                "Assets/New/Mat.mat"
            ])
            assert result.exit_code == 0

    def test_asset_mkdir(self, runner, mock_unity_response):
        """Test asset mkdir command."""
        with patch("cli.commands.asset.run_command", return_value=mock_unity_response):
            result = runner.invoke(cli, ["asset", "mkdir", "Assets/NewFolder"])
            assert result.exit_code == 0


# =============================================================================
# Editor Command Tests
# =============================================================================

class TestEditorCommands:
    """Tests for Editor CLI commands."""

    def test_editor_play(self, runner, mock_unity_response):
        """Test editor play command."""
        with patch("cli.commands.editor.run_command", return_value=mock_unity_response):
            result = runner.invoke(cli, ["editor", "play"])
            assert result.exit_code == 0

    def test_editor_pause(self, runner, mock_unity_response):
        """Test editor pause command."""
        with patch("cli.commands.editor.run_command", return_value=mock_unity_response):
            result = runner.invoke(cli, ["editor", "pause"])
            assert result.exit_code == 0

    def test_editor_stop(self, runner, mock_unity_response):
        """Test editor stop command."""
        with patch("cli.commands.editor.run_command", return_value=mock_unity_response):
            result = runner.invoke(cli, ["editor", "stop"])
            assert result.exit_code == 0

    def test_editor_console(self, runner, mock_unity_response):
        """Test editor console command."""
        with patch("cli.commands.editor.run_command", return_value=mock_unity_response):
            result = runner.invoke(cli, ["editor", "console"])
            assert result.exit_code == 0

    def test_editor_console_clear(self, runner, mock_unity_response):
        """Test editor console clear command."""
        with patch("cli.commands.editor.run_command", return_value=mock_unity_response):
            result = runner.invoke(cli, ["editor", "console", "--clear"])
            assert result.exit_code == 0

    def test_editor_add_tag(self, runner, mock_unity_response):
        """Test editor add-tag command."""
        with patch("cli.commands.editor.run_command", return_value=mock_unity_response):
            result = runner.invoke(cli, ["editor", "add-tag", "Enemy"])
            assert result.exit_code == 0

    def test_editor_add_layer(self, runner, mock_unity_response):
        """Test editor add-layer command."""
        with patch("cli.commands.editor.run_command", return_value=mock_unity_response):
            result = runner.invoke(cli, ["editor", "add-layer", "Interactable"])
            assert result.exit_code == 0

    def test_editor_menu(self, runner, mock_unity_response):
        """Test editor menu command."""
        with patch("cli.commands.editor.run_command", return_value=mock_unity_response):
            result = runner.invoke(cli, ["editor", "menu", "File/Save"])
            assert result.exit_code == 0

    def test_editor_tests(self, runner, mock_unity_response):
        """Test editor tests command."""
        with patch("cli.commands.editor.run_command", return_value=mock_unity_response):
            result = runner.invoke(cli, ["editor", "tests", "--mode", "EditMode"])
            assert result.exit_code == 0


# =============================================================================
# Prefab Command Tests
# =============================================================================

class TestPrefabCommands:
    """Tests for Prefab CLI commands."""

    def test_prefab_open(self, runner, mock_unity_response):
        """Test prefab open command."""
        with patch("cli.commands.prefab.run_command", return_value=mock_unity_response):
            result = runner.invoke(cli, ["prefab", "open", "Assets/Prefabs/Player.prefab"])
            assert result.exit_code == 0

    def test_prefab_close(self, runner, mock_unity_response):
        """Test prefab close command."""
        with patch("cli.commands.prefab.run_command", return_value=mock_unity_response):
            result = runner.invoke(cli, ["prefab", "close"])
            assert result.exit_code == 0

    def test_prefab_save(self, runner, mock_unity_response):
        """Test prefab save command."""
        with patch("cli.commands.prefab.run_command", return_value=mock_unity_response):
            result = runner.invoke(cli, ["prefab", "save"])
            assert result.exit_code == 0

    def test_prefab_create(self, runner, mock_unity_response):
        """Test prefab create command."""
        with patch("cli.commands.prefab.run_command", return_value=mock_unity_response):
            result = runner.invoke(cli, [
                "prefab", "create", "Player", "Assets/Prefabs/Player.prefab"
            ])
            assert result.exit_code == 0


# =============================================================================
# Material Command Tests
# =============================================================================

class TestMaterialCommands:
    """Tests for Material CLI commands."""

    def test_material_info(self, runner, mock_unity_response):
        """Test material info command."""
        with patch("cli.commands.material.run_command", return_value=mock_unity_response):
            result = runner.invoke(cli, ["material", "info", "Assets/Materials/Red.mat"])
            assert result.exit_code == 0

    def test_material_create(self, runner, mock_unity_response):
        """Test material create command."""
        with patch("cli.commands.material.run_command", return_value=mock_unity_response):
            result = runner.invoke(cli, ["material", "create", "Assets/Materials/New.mat"])
            assert result.exit_code == 0

    def test_material_set_color(self, runner, mock_unity_response):
        """Test material set-color command."""
        with patch("cli.commands.material.run_command", return_value=mock_unity_response):
            result = runner.invoke(cli, [
                "material", "set-color", "Assets/Materials/Red.mat",
                "1", "0", "0"
            ])
            assert result.exit_code == 0

    def test_material_set_property(self, runner, mock_unity_response):
        """Test material set-property command."""
        with patch("cli.commands.material.run_command", return_value=mock_unity_response):
            result = runner.invoke(cli, [
                "material", "set-property", "Assets/Materials/Mat.mat",
                "_Metallic", "0.5"
            ])
            assert result.exit_code == 0

    def test_material_assign(self, runner, mock_unity_response):
        """Test material assign command."""
        with patch("cli.commands.material.run_command", return_value=mock_unity_response):
            result = runner.invoke(cli, [
                "material", "assign", "Assets/Materials/Red.mat", "Cube"
            ])
            assert result.exit_code == 0


# =============================================================================
# Script Command Tests
# =============================================================================

class TestScriptCommands:
    """Tests for Script CLI commands."""

    def test_script_create(self, runner, mock_unity_response):
        """Test script create command."""
        with patch("cli.commands.script.run_command", return_value=mock_unity_response):
            result = runner.invoke(cli, ["script", "create", "PlayerController"])
            assert result.exit_code == 0

    def test_script_create_with_options(self, runner, mock_unity_response):
        """Test script create with options."""
        with patch("cli.commands.script.run_command", return_value=mock_unity_response):
            result = runner.invoke(cli, [
                "script", "create", "EnemyData",
                "--type", "ScriptableObject",
                "--namespace", "MyGame"
            ])
            assert result.exit_code == 0

    def test_script_read(self, runner):
        """Test script read command."""
        mock_response = {
            "success": True,
            "data": {"content": "using UnityEngine;\n\npublic class Test {}"}
        }
        with patch("cli.commands.script.run_command", return_value=mock_response):
            result = runner.invoke(cli, ["script", "read", "Assets/Scripts/Test.cs"])
            assert result.exit_code == 0

    def test_script_delete(self, runner, mock_unity_response):
        """Test script delete command."""
        with patch("cli.commands.script.run_command", return_value=mock_unity_response):
            result = runner.invoke(cli, ["script", "delete", "Assets/Scripts/Old.cs", "--force"])
            assert result.exit_code == 0


# =============================================================================
# Global Options Tests
# =============================================================================

class TestGlobalOptions:
    """Tests for global CLI options."""

    def test_custom_host(self, runner, mock_unity_response):
        """Test custom host option."""
        with patch("cli.main.run_check_connection", return_value=True):
            with patch("cli.main.run_list_instances", return_value={"instances": []}):
                result = runner.invoke(cli, ["--host", "192.168.1.100", "status"])
                assert result.exit_code == 0

    def test_custom_port(self, runner, mock_unity_response):
        """Test custom port option."""
        with patch("cli.main.run_check_connection", return_value=True):
            with patch("cli.main.run_list_instances", return_value={"instances": []}):
                result = runner.invoke(cli, ["--port", "9090", "status"])
                assert result.exit_code == 0

    def test_json_format(self, runner, mock_unity_response):
        """Test JSON output format."""
        with patch("cli.commands.scene.run_command", return_value=mock_unity_response):
            result = runner.invoke(cli, ["--format", "json", "scene", "active"])
            assert result.exit_code == 0

    def test_table_format(self, runner, mock_unity_response):
        """Test table output format."""
        with patch("cli.commands.scene.run_command", return_value=mock_unity_response):
            result = runner.invoke(cli, ["--format", "table", "scene", "active"])
            assert result.exit_code == 0

    def test_timeout_option(self, runner, mock_unity_response):
        """Test timeout option."""
        with patch("cli.main.run_check_connection", return_value=True):
            with patch("cli.main.run_list_instances", return_value={"instances": []}):
                result = runner.invoke(cli, ["--timeout", "60", "status"])
                assert result.exit_code == 0


# =============================================================================
# Error Handling Tests
# =============================================================================

class TestErrorHandling:
    """Tests for error handling."""

    def test_connection_error_handling(self, runner):
        """Test connection error is handled gracefully."""
        with patch("cli.commands.scene.run_command", side_effect=UnityConnectionError("Connection failed")):
            result = runner.invoke(cli, ["scene", "hierarchy"])
            assert result.exit_code == 1
            assert "Connection failed" in result.output or "Error" in result.output

    def test_invalid_json_params(self, runner):
        """Test invalid JSON parameters are handled."""
        result = runner.invoke(cli, [
            "component", "modify", "Player", "Rigidbody",
            "--properties", "not valid json"
        ])
        assert result.exit_code == 1
        assert "Invalid JSON" in result.output

    def test_missing_required_argument(self, runner):
        """Test missing required argument."""
        result = runner.invoke(cli, ["gameobject", "find"])
        assert result.exit_code != 0
        assert "Missing argument" in result.output


# =============================================================================
# Integration-style Tests (with mocked responses)
# =============================================================================

class TestIntegration:
    """Integration-style tests with realistic response data."""

    def test_full_gameobject_workflow(self, runner):
        """Test a full GameObject workflow."""
        create_response = {
            "success": True,
            "message": "GameObject created",
            "data": {"instanceID": -12345, "name": "TestObject"}
        }
        modify_response = {
            "success": True,
            "message": "GameObject modified"
        }
        delete_response = {
            "success": True,
            "message": "GameObject deleted"
        }

        # Create
        with patch("cli.commands.gameobject.run_command", return_value=create_response):
            result = runner.invoke(cli, ["gameobject", "create", "TestObject", "--primitive", "Cube"])
            assert result.exit_code == 0
            assert "Created" in result.output

        # Modify
        with patch("cli.commands.gameobject.run_command", return_value=modify_response):
            result = runner.invoke(cli, ["gameobject", "modify", "TestObject", "--position", "0", "5", "0"])
            assert result.exit_code == 0

        # Delete
        with patch("cli.commands.gameobject.run_command", return_value=delete_response):
            result = runner.invoke(cli, ["gameobject", "delete", "TestObject", "--force"])
            assert result.exit_code == 0
            assert "Deleted" in result.output

    def test_scene_hierarchy_with_data(self, runner):
        """Test scene hierarchy with realistic data."""
        hierarchy_response = {
            "success": True,
            "data": {
                "nodes": [
                    {"name": "Main Camera", "instanceID": -100, "childCount": 0},
                    {"name": "Directional Light", "instanceID": -200, "childCount": 0},
                    {"name": "Player", "instanceID": -300, "childCount": 2},
                ]
            }
        }

        with patch("cli.commands.scene.run_command", return_value=hierarchy_response):
            result = runner.invoke(cli, ["scene", "hierarchy"])
            assert result.exit_code == 0

    def test_find_gameobjects_with_results(self, runner):
        """Test finding GameObjects with results."""
        find_response = {
            "success": True,
            "message": "Found 3 GameObjects",
            "data": {
                "instanceIDs": [-100, -200, -300],
                "count": 3,
                "hasMore": False
            }
        }

        with patch("cli.commands.gameobject.run_command", return_value=find_response):
            result = runner.invoke(cli, ["gameobject", "find", "Camera"])
            assert result.exit_code == 0


if __name__ == "__main__":
    pytest.main([__file__, "-v"])
