"""Tests for manage_build MCP tool."""

import asyncio
from types import SimpleNamespace
from unittest.mock import AsyncMock

import pytest

from services.tools.manage_build import ALL_ACTIONS, manage_build


@pytest.fixture
def mock_unity(monkeypatch):
    """Patch Unity transport layer and return captured call dict."""
    captured: dict[str, object] = {}

    async def fake_send(send_fn, unity_instance, tool_name, params):
        captured["unity_instance"] = unity_instance
        captured["tool_name"] = tool_name
        captured["params"] = params
        return {"success": True, "message": "ok"}

    monkeypatch.setattr(
        "services.tools.manage_build.get_unity_instance_from_context",
        AsyncMock(return_value="unity-instance-1"),
    )
    monkeypatch.setattr(
        "services.tools.manage_build.send_with_unity_instance",
        fake_send,
    )
    return captured


# ── action validation ───────────────────────────────────────────────

def test_all_actions_count():
    assert len(ALL_ACTIONS) == 8


def test_unknown_action_returns_error(mock_unity):
    result = asyncio.run(manage_build(SimpleNamespace(), action="nonexistent"))
    assert result["success"] is False
    assert "Unknown action" in result["message"]
    assert "tool_name" not in mock_unity


# ── build action ────────────────────────────────────────────────────

def test_build_forwards_params(mock_unity):
    result = asyncio.run(
        manage_build(
            SimpleNamespace(),
            action="build",
            target="windows64",
            development="true",
            output_path="Builds/Win/Game.exe",
            scripting_backend="il2cpp",
        )
    )
    assert result["success"] is True
    params = mock_unity["params"]
    assert params["action"] == "build"
    assert params["target"] == "windows64"
    assert params["development"] is True
    assert params["output_path"] == "Builds/Win/Game.exe"
    assert params["scripting_backend"] == "il2cpp"


def test_build_omits_none_params(mock_unity):
    asyncio.run(manage_build(SimpleNamespace(), action="build"))
    params = mock_unity["params"]
    assert params == {"action": "build"}


def test_build_with_options(mock_unity):
    asyncio.run(
        manage_build(
            SimpleNamespace(),
            action="build",
            options='["clean_build", "auto_run"]',
        )
    )
    params = mock_unity["params"]
    assert params["options"] == ["clean_build", "auto_run"]


def test_build_with_scenes(mock_unity):
    asyncio.run(
        manage_build(
            SimpleNamespace(),
            action="build",
            scenes='["Assets/Scenes/Main.unity", "Assets/Scenes/Level1.unity"]',
        )
    )
    params = mock_unity["params"]
    assert params["scenes"] == ["Assets/Scenes/Main.unity", "Assets/Scenes/Level1.unity"]


# ── status action ──────────────────────────────────────────────────

def test_status_forwards_job_id(mock_unity):
    asyncio.run(manage_build(SimpleNamespace(), action="status", job_id="build-abc123"))
    params = mock_unity["params"]
    assert params["action"] == "status"
    assert params["job_id"] == "build-abc123"


def test_status_without_job_id(mock_unity):
    asyncio.run(manage_build(SimpleNamespace(), action="status"))
    params = mock_unity["params"]
    assert params == {"action": "status"}


# ── platform action ────────────────────────────────────────────────

def test_platform_read(mock_unity):
    asyncio.run(manage_build(SimpleNamespace(), action="platform"))
    params = mock_unity["params"]
    assert params == {"action": "platform"}


def test_platform_switch(mock_unity):
    asyncio.run(
        manage_build(SimpleNamespace(), action="platform", target="android", subtarget="player")
    )
    params = mock_unity["params"]
    assert params["target"] == "android"
    assert params["subtarget"] == "player"


# ── settings action ────────────────────────────────────────────────

def test_settings_read(mock_unity):
    asyncio.run(
        manage_build(SimpleNamespace(), action="settings", property="product_name")
    )
    params = mock_unity["params"]
    assert params["action"] == "settings"
    assert params["property"] == "product_name"
    assert "value" not in params


def test_settings_write(mock_unity):
    asyncio.run(
        manage_build(
            SimpleNamespace(),
            action="settings",
            property="product_name",
            value="My Game",
        )
    )
    params = mock_unity["params"]
    assert params["property"] == "product_name"
    assert params["value"] == "My Game"


# ── scenes action ──────────────────────────────────────────────────

def test_scenes_read(mock_unity):
    asyncio.run(manage_build(SimpleNamespace(), action="scenes"))
    params = mock_unity["params"]
    assert params == {"action": "scenes"}


def test_scenes_write(mock_unity):
    scenes_json = '[{"path": "Assets/Scenes/Main.unity", "enabled": true}]'
    asyncio.run(manage_build(SimpleNamespace(), action="scenes", scenes=scenes_json))
    params = mock_unity["params"]
    assert params["scenes"] == [{"path": "Assets/Scenes/Main.unity", "enabled": True}]


# ── profiles action ────────────────────────────────────────────────

def test_profiles_list(mock_unity):
    asyncio.run(manage_build(SimpleNamespace(), action="profiles"))
    params = mock_unity["params"]
    assert params == {"action": "profiles"}


def test_profiles_activate(mock_unity):
    asyncio.run(
        manage_build(
            SimpleNamespace(),
            action="profiles",
            profile="Assets/Settings/Build Profiles/iOS.asset",
            activate="true",
        )
    )
    params = mock_unity["params"]
    assert params["profile"] == "Assets/Settings/Build Profiles/iOS.asset"
    assert params["activate"] is True


# ── batch action ────────────────────────────────────────────────────

def test_batch_with_targets(mock_unity):
    asyncio.run(
        manage_build(
            SimpleNamespace(),
            action="batch",
            targets='["windows64", "linux64", "webgl"]',
            development="true",
        )
    )
    params = mock_unity["params"]
    assert params["action"] == "batch"
    assert params["targets"] == ["windows64", "linux64", "webgl"]
    assert params["development"] is True


def test_batch_with_profiles(mock_unity):
    asyncio.run(
        manage_build(
            SimpleNamespace(),
            action="batch",
            profiles='["Assets/Profiles/A.asset", "Assets/Profiles/B.asset"]',
        )
    )
    params = mock_unity["params"]
    assert params["profiles"] == ["Assets/Profiles/A.asset", "Assets/Profiles/B.asset"]


# ── cancel action ──────────────────────────────────────────────────

def test_cancel_forwards_job_id(mock_unity):
    asyncio.run(manage_build(SimpleNamespace(), action="cancel", job_id="batch-xyz789"))
    params = mock_unity["params"]
    assert params["action"] == "cancel"
    assert params["job_id"] == "batch-xyz789"


# ── minimal param forwarding ────────────────────────────────────────

def test_batch_without_targets_sends_minimal_params(mock_unity):
    asyncio.run(manage_build(SimpleNamespace(), action="batch"))
    params = mock_unity["params"]
    assert params == {"action": "batch"}


def test_settings_without_property_sends_minimal_params(mock_unity):
    asyncio.run(manage_build(SimpleNamespace(), action="settings"))
    params = mock_unity["params"]
    assert params == {"action": "settings"}


def test_cancel_without_job_id_sends_minimal_params(mock_unity):
    asyncio.run(manage_build(SimpleNamespace(), action="cancel"))
    params = mock_unity["params"]
    assert params == {"action": "cancel"}


# ── transport ───────────────────────────────────────────────────────

def test_sends_to_correct_tool_name(mock_unity):
    asyncio.run(manage_build(SimpleNamespace(), action="status"))
    assert mock_unity["tool_name"] == "manage_build"
