"""Tests for the manage_editor tool surface."""

import inspect

import pytest

import services.tools.manage_editor as manage_editor_mod
from services.registry import get_registered_tools
from .integration.test_helpers import DummyContext


def test_manage_editor_prefab_path_parameters_exist():
    """open_prefab_stage should expose prefab_path plus path alias parameters."""
    sig = inspect.signature(manage_editor_mod.manage_editor)
    assert "prefab_path" in sig.parameters
    assert "path" in sig.parameters
    assert sig.parameters["prefab_path"].default is None
    assert sig.parameters["path"].default is None


def test_manage_editor_description_mentions_open_prefab_stage():
    """The tool description should advertise the new prefab stage action."""
    editor_tool = next(
        (t for t in get_registered_tools() if t["name"] == "manage_editor"), None
    )
    assert editor_tool is not None
    desc = editor_tool.get("description") or editor_tool.get("kwargs", {}).get("description", "")
    assert "open_prefab_stage" in desc


@pytest.mark.asyncio
async def test_manage_editor_open_prefab_stage_forwards_prefab_path(monkeypatch):
    """prefab_path should map to Unity's prefabPath parameter."""
    captured = {}

    async def fake_send(cmd, params, **kwargs):
        captured["cmd"] = cmd
        captured["params"] = params
        return {"success": True, "data": {"openedPrefabPath": params["prefabPath"]}}

    monkeypatch.setattr(
        manage_editor_mod,
        "async_send_command_with_retry",
        fake_send,
    )

    resp = await manage_editor_mod.manage_editor(
        ctx=DummyContext(),
        action="open_prefab_stage",
        prefab_path="Assets/Prefabs/Test.prefab",
    )

    assert resp.get("success") is True
    assert captured["cmd"] == "manage_editor"
    assert captured["params"]["action"] == "open_prefab_stage"
    assert captured["params"]["prefabPath"] == "Assets/Prefabs/Test.prefab"
    assert "path" not in captured["params"]


@pytest.mark.asyncio
async def test_manage_editor_open_prefab_stage_accepts_path_alias(monkeypatch):
    """path should remain available as a compatibility alias."""
    captured = {}

    async def fake_send(cmd, params, **kwargs):
        captured["params"] = params
        return {"success": True}

    monkeypatch.setattr(
        manage_editor_mod,
        "async_send_command_with_retry",
        fake_send,
    )

    resp = await manage_editor_mod.manage_editor(
        ctx=DummyContext(),
        action="open_prefab_stage",
        path="Assets/Prefabs/Alias.prefab",
    )

    assert resp.get("success") is True
    assert captured["params"]["action"] == "open_prefab_stage"
    assert captured["params"]["path"] == "Assets/Prefabs/Alias.prefab"
    assert "prefabPath" not in captured["params"]


@pytest.mark.asyncio
async def test_manage_editor_open_prefab_stage_rejects_conflicting_path_inputs(monkeypatch):
    """Conflicting aliases should fail fast before sending a Unity command."""

    async def fake_send(cmd, params, **kwargs):  # pragma: no cover - should not be hit
        raise AssertionError("send should not be called for conflicting path inputs")

    monkeypatch.setattr(
        manage_editor_mod,
        "async_send_command_with_retry",
        fake_send,
    )

    resp = await manage_editor_mod.manage_editor(
        ctx=DummyContext(),
        action="open_prefab_stage",
        prefab_path="Assets/Prefabs/Primary.prefab",
        path="Assets/Prefabs/Alias.prefab",
    )

    assert resp.get("success") is False
    assert "Provide only one of prefab_path or path" in resp.get("message", "")
