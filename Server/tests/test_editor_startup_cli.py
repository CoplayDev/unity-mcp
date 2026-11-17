from types import SimpleNamespace
from unittest.mock import Mock

import pytest
from click.testing import CliRunner
from cli.commands import editor as module


@pytest.fixture
def command(monkeypatch):
    config = SimpleNamespace(format="json")
    send = Mock(return_value={"success": True, "data": {"status": "completed"}})
    monkeypatch.setattr(module, "get_config", lambda: config)
    monkeypatch.setattr(module, "run_command", send)
    return CliRunner(), send, config


def test_default_play_keeps_existing_request(command):
    runner, send, config = command
    result = runner.invoke(module.editor, ["play"])
    assert result.exit_code == 0, result.output
    send.assert_called_once_with("manage_editor", {"action": "play"}, config)


@pytest.mark.parametrize("phase", ["scene_loaded", "first_frame"])
def test_cli_wait_forwards_milestone_and_http_timeout(command, phase):
    runner, send, config = command
    result = runner.invoke(module.editor, ["play", "--wait-until", phase, "--wait-timeout", "60"])
    assert result.exit_code == 0, result.output
    send.assert_called_once_with("manage_editor", {"action": "play", "waitUntil": phase, "waitTimeout": 60}, config, timeout=65)


def test_ready_code_uses_default_wait_budget(command):
    runner, send, config = command
    result = runner.invoke(module.editor, ["play", "--on-ready-code", "return 42;"])
    assert result.exit_code == 0, result.output
    send.assert_called_once_with("manage_editor", {"action": "play", "onReadyCode": "return 42;", "waitTimeout": 30}, config, timeout=35)


@pytest.mark.parametrize("options", [["--wait-until", "bad"], ["--wait-until", "first_frame", "--wait-timeout", "0"], ["--wait-timeout", "10"]])
def test_invalid_cli_options_never_send(command, options):
    runner, send, _ = command
    result = runner.invoke(module.editor, ["play", *options])
    assert result.exit_code == 2, result.output
    send.assert_not_called()


def test_cli_status_polls_existing_job(command):
    runner, send, config = command
    result = runner.invoke(module.editor, ["play-status", "startup-job"])
    assert result.exit_code == 0, result.output
    send.assert_called_once_with("manage_editor", {"action": "get_play_mode_startup", "jobId": "startup-job"}, config)
