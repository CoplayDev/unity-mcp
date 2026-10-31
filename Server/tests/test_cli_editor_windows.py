from unittest.mock import Mock

import pytest
from click.testing import CliRunner

from cli.commands.editor import editor
from cli.utils.config import CLIConfig, set_config


@pytest.fixture
def transport(monkeypatch):
    """Use an isolated CLI configuration and mock transport without connecting to Unity."""
    config = CLIConfig(format="json", unity_instance="isolated-test@hash")
    set_config(config)
    send = Mock(return_value={"success": True, "data": {"path": "Library/McpEditorScreenshots/test.png"}})
    monkeypatch.setattr("cli.commands.editor.run_command", send)
    return send, config


def test_list_routes_through_cli_configuration(transport):
    """The window-list command retains configured instance routing."""
    send, config = transport
    result = CliRunner().invoke(editor, ["windows"])
    assert result.exit_code == 0, result.output
    send.assert_called_once_with("manage_editor_windows", {"action": "list"}, config)


@pytest.mark.parametrize("selector,params", [
    ([], {}), (["--window-id", "42"], {"window_id": 42}),
    (["--window-title", "Inspector"], {"window_title": "Inspector"}),
    (["--window-type", "UnityEditor.ConsoleWindow"], {"window_type": "UnityEditor.ConsoleWindow"}),
])
def test_capture_saves_file_without_printing_base64(transport, selector, params):
    """CLI captures request file-only output and preserve explicit selector and focus options."""
    send, config = transport
    result = CliRunner().invoke(editor, ["screenshot", *selector, "--no-focus", "--no-restore-focus"])
    assert result.exit_code == 0, result.output
    send.assert_called_once_with("manage_editor_windows", {
        "action": "screenshot", **params, "focus": False, "restore_focus": False,
        "include_image": False, "save_file": True,
    }, config)
    assert "Library/McpEditorScreenshots/test.png" in result.output


@pytest.mark.parametrize("args", [
    ["--window-id", "invalid"], ["--window-id", "42", "--window-title", "Inspector"],
    ["--output-folder", "../private"],
])
def test_invalid_cli_capture_does_not_connect(transport, args):
    """Invalid selectors and unsupported output paths fail before connection."""
    result = CliRunner().invoke(editor, ["screenshot", *args])
    assert result.exit_code == 2
    transport[0].assert_not_called()
