"""
Tests for the reserialize_assets tool.

Validates parameter handling for single path, multiple paths, and error cases.
"""
import pytest

from .test_helpers import DummyContext
import services.tools.reserialize_assets as reserialize_mod


@pytest.mark.asyncio
async def test_reserialize_assets_single_path(monkeypatch):
    """Test reserialization with a single path parameter."""
    captured = {}

    async def fake_send(cmd, params, **kwargs):
        captured["cmd"] = cmd
        captured["params"] = params
        return {
            "success": True,
            "message": "Reserialized 1 asset(s).",
            "data": {"paths": ["Assets/Prefabs/Player.prefab"]},
        }

    monkeypatch.setattr(reserialize_mod, "async_send_command_with_retry", fake_send)

    resp = await reserialize_mod.reserialize_assets(
        ctx=DummyContext(),
        path="Assets/Prefabs/Player.prefab",
    )

    assert resp.get("success") is True
    assert captured["cmd"] == "reserialize_assets"
    assert captured["params"]["path"] == "Assets/Prefabs/Player.prefab"
    assert "paths" not in captured["params"]


@pytest.mark.asyncio
async def test_reserialize_assets_multiple_paths(monkeypatch):
    """Test reserialization with an array of paths."""
    captured = {}

    async def fake_send(cmd, params, **kwargs):
        captured["params"] = params
        return {
            "success": True,
            "message": "Reserialized 3 asset(s).",
            "data": {
                "paths": [
                    "Assets/Prefabs/A.prefab",
                    "Assets/Prefabs/B.prefab",
                    "Assets/Materials/C.mat",
                ]
            },
        }

    monkeypatch.setattr(reserialize_mod, "async_send_command_with_retry", fake_send)

    resp = await reserialize_mod.reserialize_assets(
        ctx=DummyContext(),
        paths=[
            "Assets/Prefabs/A.prefab",
            "Assets/Prefabs/B.prefab",
            "Assets/Materials/C.mat",
        ],
    )

    assert resp.get("success") is True
    assert captured["params"]["paths"] == [
        "Assets/Prefabs/A.prefab",
        "Assets/Prefabs/B.prefab",
        "Assets/Materials/C.mat",
    ]
    assert "path" not in captured["params"]


@pytest.mark.asyncio
async def test_reserialize_assets_paths_preferred_over_path(monkeypatch):
    """Test that paths array takes priority when both path and paths are provided."""
    captured = {}

    async def fake_send(cmd, params, **kwargs):
        captured["params"] = params
        return {"success": True, "message": "ok", "data": None}

    monkeypatch.setattr(reserialize_mod, "async_send_command_with_retry", fake_send)

    resp = await reserialize_mod.reserialize_assets(
        ctx=DummyContext(),
        path="Assets/single.prefab",
        paths=["Assets/multi1.prefab", "Assets/multi2.prefab"],
    )

    assert resp.get("success") is True
    assert "paths" in captured["params"]
    assert "path" not in captured["params"]


@pytest.mark.asyncio
async def test_reserialize_assets_no_params_returns_error(monkeypatch):
    """Test that providing neither path nor paths returns an error without calling Unity."""
    send_called = False

    async def fake_send(cmd, params, **kwargs):
        nonlocal send_called
        send_called = True
        return {"success": True}

    monkeypatch.setattr(reserialize_mod, "async_send_command_with_retry", fake_send)

    resp = await reserialize_mod.reserialize_assets(
        ctx=DummyContext(),
    )

    assert resp.get("success") is False
    assert "path" in resp.get("message", "").lower()
    assert send_called is False, "Should not call Unity when no paths provided"


@pytest.mark.asyncio
async def test_reserialize_assets_unity_error_passthrough(monkeypatch):
    """Test that Unity-side errors are passed through correctly."""

    async def fake_send(cmd, params, **kwargs):
        return {"success": False, "error": "Asset not found"}

    monkeypatch.setattr(reserialize_mod, "async_send_command_with_retry", fake_send)

    resp = await reserialize_mod.reserialize_assets(
        ctx=DummyContext(),
        path="Assets/NonExistent.prefab",
    )

    assert resp.get("success") is False
