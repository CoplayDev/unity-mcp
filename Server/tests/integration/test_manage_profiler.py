"""
Tests for the manage_profiler tool.

Validates parameter mapping, action routing, and response handling.
"""
import pytest

from .test_helpers import DummyContext
import services.tools.manage_profiler as profiler_mod


@pytest.mark.asyncio
async def test_manage_profiler_read_frames_default_params(monkeypatch):
    """Test read_frames with default parameters sends correct command."""
    captured = {}

    async def fake_send(cmd, params, **kwargs):
        captured["cmd"] = cmd
        captured["params"] = params
        return {
            "success": True,
            "message": "1 frames read",
            "data": {"frames": []},
        }

    monkeypatch.setattr(profiler_mod, "async_send_command_with_retry", fake_send)

    resp = await profiler_mod.manage_profiler(
        ctx=DummyContext(),
        action="read_frames",
    )

    assert resp.get("success") is True
    assert captured["cmd"] == "manage_profiler"
    assert captured["params"]["action"] == "read_frames"
    # No optional params should be sent when not provided
    assert "frameCount" not in captured["params"]
    assert "thread" not in captured["params"]
    assert "filter" not in captured["params"]
    assert "minMs" not in captured["params"]


@pytest.mark.asyncio
async def test_manage_profiler_read_frames_with_all_params(monkeypatch):
    """Test read_frames passes all optional parameters correctly."""
    captured = {}

    async def fake_send(cmd, params, **kwargs):
        captured["params"] = params
        return {
            "success": True,
            "message": "5 frames read",
            "data": {"frames": []},
        }

    monkeypatch.setattr(profiler_mod, "async_send_command_with_retry", fake_send)

    resp = await profiler_mod.manage_profiler(
        ctx=DummyContext(),
        action="read_frames",
        frame_count=5,
        thread=1,
        filter="Render",
        min_ms=0.5,
    )

    assert resp.get("success") is True
    p = captured["params"]
    assert p["action"] == "read_frames"
    assert p["frameCount"] == 5
    assert p["thread"] == 1
    assert p["filter"] == "Render"
    assert p["minMs"] == 0.5


@pytest.mark.asyncio
async def test_manage_profiler_read_frames_string_coercion(monkeypatch):
    """Test that string values for numeric params are coerced to proper types."""
    captured = {}

    async def fake_send(cmd, params, **kwargs):
        captured["params"] = params
        return {"success": True, "message": "ok", "data": None}

    monkeypatch.setattr(profiler_mod, "async_send_command_with_retry", fake_send)

    resp = await profiler_mod.manage_profiler(
        ctx=DummyContext(),
        action="read_frames",
        frame_count="3",
        thread="2",
        min_ms="0.01",
    )

    assert resp.get("success") is True
    p = captured["params"]
    assert p["frameCount"] == 3
    assert isinstance(p["frameCount"], int)
    assert p["thread"] == 2
    assert isinstance(p["thread"], int)
    assert p["minMs"] == 0.01
    assert isinstance(p["minMs"], float)


@pytest.mark.asyncio
async def test_manage_profiler_enable(monkeypatch):
    """Test enable action sends correct command."""
    captured = {}

    async def fake_send(cmd, params, **kwargs):
        captured["cmd"] = cmd
        captured["params"] = params
        return {"success": True, "message": "Profiler enabled."}

    monkeypatch.setattr(profiler_mod, "async_send_command_with_retry", fake_send)

    resp = await profiler_mod.manage_profiler(
        ctx=DummyContext(),
        action="enable",
    )

    assert resp.get("success") is True
    assert captured["cmd"] == "manage_profiler"
    assert captured["params"]["action"] == "enable"
    # No read_frames params should be present
    assert "frameCount" not in captured["params"]


@pytest.mark.asyncio
async def test_manage_profiler_disable(monkeypatch):
    """Test disable action sends correct command."""
    captured = {}

    async def fake_send(cmd, params, **kwargs):
        captured["params"] = params
        return {"success": True, "message": "Profiler disabled."}

    monkeypatch.setattr(profiler_mod, "async_send_command_with_retry", fake_send)

    resp = await profiler_mod.manage_profiler(
        ctx=DummyContext(),
        action="disable",
    )

    assert resp.get("success") is True
    assert captured["params"]["action"] == "disable"


@pytest.mark.asyncio
async def test_manage_profiler_status(monkeypatch):
    """Test status action returns profiler state data."""
    captured = {}

    async def fake_send(cmd, params, **kwargs):
        captured["params"] = params
        return {
            "success": True,
            "message": "Profiler status",
            "data": {
                "enabled": True,
                "firstFrame": 0,
                "lastFrame": 100,
                "frameCount": 101,
            },
        }

    monkeypatch.setattr(profiler_mod, "async_send_command_with_retry", fake_send)

    resp = await profiler_mod.manage_profiler(
        ctx=DummyContext(),
        action="status",
    )

    assert resp.get("success") is True
    assert resp["data"]["enabled"] is True
    assert resp["data"]["frameCount"] == 101


@pytest.mark.asyncio
async def test_manage_profiler_clear(monkeypatch):
    """Test clear action sends correct command."""
    captured = {}

    async def fake_send(cmd, params, **kwargs):
        captured["params"] = params
        return {"success": True, "message": "All profiler frames cleared."}

    monkeypatch.setattr(profiler_mod, "async_send_command_with_retry", fake_send)

    resp = await profiler_mod.manage_profiler(
        ctx=DummyContext(),
        action="clear",
    )

    assert resp.get("success") is True
    assert captured["params"]["action"] == "clear"


@pytest.mark.asyncio
async def test_manage_profiler_unity_error_passthrough(monkeypatch):
    """Test that Unity-side errors are passed through correctly."""

    async def fake_send(cmd, params, **kwargs):
        return {"success": False, "error": "Profiler is not enabled."}

    monkeypatch.setattr(profiler_mod, "async_send_command_with_retry", fake_send)

    resp = await profiler_mod.manage_profiler(
        ctx=DummyContext(),
        action="read_frames",
    )

    assert resp.get("success") is False


@pytest.mark.asyncio
async def test_manage_profiler_non_read_frames_ignores_extra_params(monkeypatch):
    """Test that non-read_frames actions don't send read_frames-specific params."""
    captured = {}

    async def fake_send(cmd, params, **kwargs):
        captured["params"] = params
        return {"success": True, "message": "ok"}

    monkeypatch.setattr(profiler_mod, "async_send_command_with_retry", fake_send)

    resp = await profiler_mod.manage_profiler(
        ctx=DummyContext(),
        action="enable",
        frame_count=5,
        thread=1,
        filter="test",
        min_ms=0.1,
    )

    assert resp.get("success") is True
    p = captured["params"]
    assert p == {"action": "enable"}
