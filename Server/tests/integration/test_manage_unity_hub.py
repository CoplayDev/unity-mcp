"""
Tests for the manage_unity_hub tool and its underlying service.

Covers MCP-tool dispatch, the confirmation gate for state-changing actions,
service-layer parsing, and Unity Hub auto-detection (UNITY_HUB_PATH override
and hub-not-found error).

Tests do NOT require a real Unity Hub installation - the subprocess and
detection layers are mocked.
"""
import pytest

import services.tools.manage_unity_hub as manage_hub_mod
import services.unity_hub as unity_hub_mod


@pytest.mark.asyncio
async def test_list_installed_editors_invokes_hub_and_parses_output(monkeypatch):
    """list_installed_editors calls Hub with the right args and returns parsed editors."""
    captured = {}

    async def fake_run(args, timeout=30, hub_path=None):
        captured["args"] = args
        captured["timeout"] = timeout
        return {
            "success": True,
            "hub_path": "/Applications/Unity Hub.app/Contents/MacOS/Unity Hub",
            "raw_output": (
                "6000.3.10f1 (Apple silicon) installed at /Applications/Unity/Hub/Editor/6000.3.10f1\n"
                "2022.3.0f1 , installed at /Applications/Unity/Hub/Editor/2022.3.0f1"
            ),
            "stderr": None,
        }

    monkeypatch.setattr(manage_hub_mod, "run_hub_command", fake_run)

    resp = await manage_hub_mod.manage_unity_hub(action="list_installed_editors")

    assert resp["success"] is True
    assert resp["action"] == "list_installed_editors"
    assert captured["args"] == ["editors", "--installed"]
    assert resp["data"] == [
        {"version": "6000.3.10f1", "path": "/Applications/Unity/Hub/Editor/6000.3.10f1"},
        {"version": "2022.3.0f1", "path": "/Applications/Unity/Hub/Editor/2022.3.0f1"},
    ]


def test_parse_installed_editors_handles_realistic_output():
    """parse_installed_editors handles both 'installed at' and comma-separated formats."""
    raw = (
        "6000.3.10f1 (Apple silicon) installed at /Applications/Unity/Hub/Editor/6000.3.10f1\n"
        "2022.3.0f1 , installed at /Applications/Unity/Hub/Editor/2022.3.0f1\n"
        "\n"  # blank line should be skipped
    )

    result = unity_hub_mod.parse_installed_editors(raw)

    assert result == [
        {"version": "6000.3.10f1", "path": "/Applications/Unity/Hub/Editor/6000.3.10f1"},
        {"version": "2022.3.0f1", "path": "/Applications/Unity/Hub/Editor/2022.3.0f1"},
    ]


@pytest.mark.asyncio
async def test_set_install_path_confirm_false_does_not_invoke_hub(monkeypatch):
    """State-changing action without confirm=True must not invoke the Hub subprocess."""
    invoked = {"called": False}

    async def fake_run(*args, **kwargs):
        invoked["called"] = True
        return {"success": True, "hub_path": "/dummy", "raw_output": ""}

    monkeypatch.setattr(manage_hub_mod, "run_hub_command", fake_run)
    monkeypatch.setattr(
        manage_hub_mod, "detect_hub_path", lambda: "/dummy/Unity Hub"
    )

    resp = await manage_hub_mod.manage_unity_hub(
        action="set_install_path",
        path="/Applications/Unity/Hub/Editor",
    )

    assert resp["success"] is False
    assert resp.get("confirmation_required") is True
    assert resp.get("hint")
    assert invoked["called"] is False


@pytest.mark.asyncio
async def test_install_editor_confirm_true_invokes_hub_with_install_timeout(monkeypatch):
    """install_editor with confirm=True invokes Hub install with version arg and install timeout."""
    captured = {}

    async def fake_run(args, timeout=30, hub_path=None):
        captured["args"] = args
        captured["timeout"] = timeout
        return {
            "success": True,
            "hub_path": "/dummy/Unity Hub",
            "raw_output": "Installation started",
            "stderr": None,
        }

    monkeypatch.setattr(manage_hub_mod, "run_hub_command", fake_run)
    monkeypatch.setattr(
        manage_hub_mod, "detect_hub_path", lambda: "/dummy/Unity Hub"
    )

    resp = await manage_hub_mod.manage_unity_hub(
        action="install_editor",
        version="6000.3.10f1",
        confirm=True,
    )

    assert resp["success"] is True
    assert resp["action"] == "install_editor"
    assert captured["args"] == ["install", "--version", "6000.3.10f1"]
    assert captured["timeout"] == unity_hub_mod._INSTALL_TIMEOUT


@pytest.mark.asyncio
async def test_install_modules_confirm_true_passes_each_module(monkeypatch):
    """install_modules with confirm=True forwards each module via repeated --module flags."""
    captured = {}

    async def fake_run(args, timeout=30, hub_path=None):
        captured["args"] = args
        captured["timeout"] = timeout
        return {
            "success": True,
            "hub_path": "/dummy/Unity Hub",
            "raw_output": "Modules installation started",
            "stderr": None,
        }

    monkeypatch.setattr(manage_hub_mod, "run_hub_command", fake_run)
    monkeypatch.setattr(
        manage_hub_mod, "detect_hub_path", lambda: "/dummy/Unity Hub"
    )

    resp = await manage_hub_mod.manage_unity_hub(
        action="install_modules",
        version="6000.3.10f1",
        modules=["android", "ios"],
        confirm=True,
    )

    assert resp["success"] is True
    assert resp["action"] == "install_modules"
    assert captured["args"] == [
        "install-modules",
        "--version", "6000.3.10f1",
        "--module", "android",
        "--module", "ios",
    ]
    assert captured["timeout"] == unity_hub_mod._INSTALL_TIMEOUT


def test_detect_hub_path_uses_unity_hub_path_env_override(monkeypatch, tmp_path):
    """UNITY_HUB_PATH env var takes precedence when it points at an existing file."""
    fake_hub = tmp_path / "Unity Hub"
    fake_hub.write_text("#!/bin/sh\nexit 0\n")

    monkeypatch.setenv("UNITY_HUB_PATH", str(fake_hub))

    assert unity_hub_mod.detect_hub_path() == str(fake_hub)


@pytest.mark.asyncio
async def test_run_hub_command_returns_hub_not_found_when_detection_fails(monkeypatch):
    """run_hub_command surfaces a structured hub_not_found error when detection returns None."""
    monkeypatch.setattr(unity_hub_mod, "detect_hub_path", lambda: None)

    result = await unity_hub_mod.run_hub_command(["editors", "--installed"])

    assert result["success"] is False
    assert result["error"]["type"] == "hub_not_found"
    assert "UNITY_HUB_PATH" in result["error"]["message"]
