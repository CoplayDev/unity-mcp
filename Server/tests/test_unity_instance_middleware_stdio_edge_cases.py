"""
Edge case tests for stdio tool toggle functionality.

Tests cover boundary conditions, error handling, and concurrent scenarios
that may not occur in normal operation but could cause subtle bugs.
"""
import asyncio
import json
import os
from datetime import datetime, timedelta, timezone
from types import SimpleNamespace
from unittest.mock import AsyncMock, Mock, patch

import pytest

from core.config import config
from transport.unity_instance_middleware import UnityInstanceMiddleware


# ---------------------------------------------------------------------------
# Test helpers
# ---------------------------------------------------------------------------

def _tool_registry() -> list[dict]:
    return [
        {"name": "manage_scene", "unity_target": "manage_scene"},
        {"name": "manage_script", "unity_target": "manage_script"},
        {"name": "manage_asset", "unity_target": "manage_asset"},
        {"name": "server_only_tool", "unity_target": None},
    ]


def _build_fastmcp_context(active_instance: str | None = None) -> Mock:
    state = {}
    if active_instance:
        state["unity_instance"] = active_instance

    ctx = Mock()
    ctx.client_id = "test-client"
    ctx.set_state = Mock(side_effect=lambda key, value: state.__setitem__(key, value))
    ctx.get_state = Mock(side_effect=lambda key: state.get(key))
    return ctx


def _write_status_file(path, payload: dict) -> None:
    path.write_text(json.dumps(payload), encoding="utf-8")


async def _filter_tool_names(middleware: UnityInstanceMiddleware, fastmcp_context: Mock) -> list[str]:
    middleware_ctx = SimpleNamespace(fastmcp_context=fastmcp_context)
    available_tools = [
        SimpleNamespace(name="manage_scene"),
        SimpleNamespace(name="manage_script"),
        SimpleNamespace(name="manage_asset"),
        SimpleNamespace(name="server_only_tool"),
    ]

    async def call_next(_ctx):
        return available_tools

    with patch.object(middleware, "_inject_unity_instance", new=AsyncMock()):
        with patch(
            "transport.unity_instance_middleware.get_registered_tools",
            return_value=_tool_registry(),
        ):
            filtered = await middleware.on_list_tools(middleware_ctx, call_next)

    return [tool.name for tool in filtered]


def _build_context(session_id: str, session_obj: object, method: str = "tools/list"):
    fastmcp_context = SimpleNamespace(
        request_context=object(),
        session_id=session_id,
        session=session_obj,
    )
    return SimpleNamespace(
        fastmcp_context=fastmcp_context,
        method=method,
    )


# ---------------------------------------------------------------------------
# Status file content edge cases
# ---------------------------------------------------------------------------

@pytest.mark.asyncio
async def test_empty_enabled_tools_list_passes_filter(monkeypatch, tmp_path):
    """Empty enabled_tools list should result in only server-only tools visible."""
    monkeypatch.setattr(config, "transport_mode", "stdio")
    monkeypatch.setenv("UNITY_MCP_STATUS_DIR", str(tmp_path))

    _write_status_file(
        tmp_path / "unity-mcp-status-abc123.json",
        {"project_hash": "abc123", "enabled_tools": []},
    )

    middleware = UnityInstanceMiddleware()
    names = await _filter_tool_names(middleware, _build_fastmcp_context("Project@abc123"))

    # Only server-only tool should be visible
    assert "server_only_tool" in names
    assert "manage_scene" not in names
    assert "manage_script" not in names
    assert "manage_asset" not in names


@pytest.mark.asyncio
async def test_enabled_tools_with_null_elements_filtered(monkeypatch, tmp_path):
    """Null elements in enabled_tools should be ignored."""
    monkeypatch.setattr(config, "transport_mode", "stdio")
    monkeypatch.setenv("UNITY_MCP_STATUS_DIR", str(tmp_path))

    _write_status_file(
        tmp_path / "unity-mcp-status-abc123.json",
        {"project_hash": "abc123", "enabled_tools": ["manage_scene", None, "manage_script", 123, ""]},
    )

    middleware = UnityInstanceMiddleware()
    names = await _filter_tool_names(middleware, _build_fastmcp_context("Project@abc123"))

    assert "manage_scene" in names
    assert "manage_script" in names
    assert "manage_asset" not in names


@pytest.mark.asyncio
async def test_enabled_tools_is_object_not_list_skipped(monkeypatch, tmp_path):
    """If enabled_tools is an object instead of list, file should be skipped."""
    monkeypatch.setattr(config, "transport_mode", "stdio")
    monkeypatch.setenv("UNITY_MCP_STATUS_DIR", str(tmp_path))

    _write_status_file(
        tmp_path / "unity-mcp-status-abc123.json",
        {"project_hash": "abc123", "enabled_tools": {"manage_scene": True}},
    )

    middleware = UnityInstanceMiddleware()
    names = await _filter_tool_names(middleware, _build_fastmcp_context("Project@abc123"))

    # Should fall through without filtering
    assert "manage_scene" in names
    assert "manage_asset" in names


@pytest.mark.asyncio
async def test_project_hash_missing_uses_filename_hash(monkeypatch, tmp_path):
    """If project_hash is missing, should extract from filename."""
    monkeypatch.setattr(config, "transport_mode", "stdio")
    monkeypatch.setenv("UNITY_MCP_STATUS_DIR", str(tmp_path))

    _write_status_file(
        tmp_path / "unity-mcp-status-fromfilename.json",
        {"enabled_tools": ["manage_scene"]},  # No project_hash
    )

    middleware = UnityInstanceMiddleware()
    names = await _filter_tool_names(middleware, _build_fastmcp_context("Project@fromfilename"))

    assert "manage_scene" in names
    assert "manage_asset" not in names


@pytest.mark.asyncio
async def test_project_hash_empty_string_uses_filename_hash(monkeypatch, tmp_path):
    """If project_hash is empty string, should extract from filename."""
    monkeypatch.setattr(config, "transport_mode", "stdio")
    monkeypatch.setenv("UNITY_MCP_STATUS_DIR", str(tmp_path))

    _write_status_file(
        tmp_path / "unity-mcp-status-filename123.json",
        {"project_hash": "", "enabled_tools": ["manage_scene"]},
    )

    middleware = UnityInstanceMiddleware()
    names = await _filter_tool_names(middleware, _build_fastmcp_context("Project@filename123"))

    assert "manage_scene" in names


@pytest.mark.asyncio
async def test_no_project_hash_and_no_filename_hash_skipped(monkeypatch, tmp_path):
    """File without project_hash and no hash in filename should be skipped."""
    monkeypatch.setattr(config, "transport_mode", "stdio")
    monkeypatch.setenv("UNITY_MCP_STATUS_DIR", str(tmp_path))

    _write_status_file(
        tmp_path / "unity-mcp-status-.json",  # Empty hash suffix
        {"enabled_tools": ["manage_scene"]},
    )

    middleware = UnityInstanceMiddleware()
    names = await _filter_tool_names(middleware, _build_fastmcp_context("Project@anyhash"))

    # Should fall through without filtering since no valid payload
    assert "manage_scene" in names
    assert "manage_asset" in names


# ---------------------------------------------------------------------------
# Heartbeat and TTL edge cases
# ---------------------------------------------------------------------------

@pytest.mark.asyncio
async def test_heartbeat_with_z_suffix_parsed_correctly(monkeypatch, tmp_path):
    """Heartbeat with 'Z' suffix should be parsed correctly."""
    monkeypatch.setattr(config, "transport_mode", "stdio")
    monkeypatch.setenv("UNITY_MCP_STATUS_DIR", str(tmp_path))
    monkeypatch.setenv("UNITY_MCP_STDIO_STATUS_TTL_SECONDS", "60")

    # Recent heartbeat with Z suffix
    heartbeat = datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")

    _write_status_file(
        tmp_path / "unity-mcp-status-abc123.json",
        {"project_hash": "abc123", "enabled_tools": ["manage_scene"], "last_heartbeat": heartbeat},
    )

    middleware = UnityInstanceMiddleware()
    names = await _filter_tool_names(middleware, _build_fastmcp_context("Project@abc123"))

    assert "manage_scene" in names


@pytest.mark.asyncio
async def test_heartbeat_without_timezone_treated_as_utc(monkeypatch, tmp_path):
    """Heartbeat without timezone should be treated as UTC."""
    monkeypatch.setattr(config, "transport_mode", "stdio")
    monkeypatch.setenv("UNITY_MCP_STATUS_DIR", str(tmp_path))
    monkeypatch.setenv("UNITY_MCP_STDIO_STATUS_TTL_SECONDS", "60")

    # Heartbeat without timezone
    heartbeat = datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%S")

    _write_status_file(
        tmp_path / "unity-mcp-status-abc123.json",
        {"project_hash": "abc123", "enabled_tools": ["manage_scene"], "last_heartbeat": heartbeat},
    )

    middleware = UnityInstanceMiddleware()
    names = await _filter_tool_names(middleware, _build_fastmcp_context("Project@abc123"))

    assert "manage_scene" in names


@pytest.mark.asyncio
async def test_heartbeat_invalid_format_falls_back_to_mtime(monkeypatch, tmp_path):
    """Invalid heartbeat format should fall back to file mtime."""
    monkeypatch.setattr(config, "transport_mode", "stdio")
    monkeypatch.setenv("UNITY_MCP_STATUS_DIR", str(tmp_path))
    monkeypatch.setenv("UNITY_MCP_STDIO_STATUS_TTL_SECONDS", "60")

    _write_status_file(
        tmp_path / "unity-mcp-status-abc123.json",
        {"project_hash": "abc123", "enabled_tools": ["manage_scene"], "last_heartbeat": "not-a-date"},
    )

    middleware = UnityInstanceMiddleware()
    names = await _filter_tool_names(middleware, _build_fastmcp_context("Project@abc123"))

    # File is fresh by mtime, should filter
    assert "manage_scene" in names
    assert "manage_asset" not in names


@pytest.mark.asyncio
async def test_heartbeat_exactly_at_ttl_boundary_is_stale(monkeypatch, tmp_path):
    """Heartbeat exactly at TTL boundary should be considered stale (>)."""
    monkeypatch.setattr(config, "transport_mode", "stdio")
    monkeypatch.setenv("UNITY_MCP_STATUS_DIR", str(tmp_path))
    monkeypatch.setenv("UNITY_MCP_STDIO_STATUS_TTL_SECONDS", "10")

    # Heartbeat exactly TTL seconds ago (boundary case: > not >=)
    boundary_heartbeat = (datetime.now(timezone.utc) - timedelta(seconds=10)).isoformat()
    slightly_fresh = (datetime.now(timezone.utc) - timedelta(seconds=9.9)).isoformat()

    _write_status_file(
        tmp_path / "unity-mcp-status-boundary.json",
        {"project_hash": "boundary", "enabled_tools": ["manage_scene"], "last_heartbeat": boundary_heartbeat},
    )
    _write_status_file(
        tmp_path / "unity-mcp-status-fresh.json",
        {"project_hash": "fresh", "enabled_tools": ["manage_asset"], "last_heartbeat": slightly_fresh},
    )

    middleware = UnityInstanceMiddleware()
    names = await _filter_tool_names(middleware, _build_fastmcp_context(None))

    # Boundary should be stale, fresh should be included
    assert "manage_asset" in names
    assert "manage_scene" not in names


@pytest.mark.asyncio
async def test_ttl_zero_uses_default(monkeypatch, tmp_path):
    """TTL of 0 should fall back to default."""
    monkeypatch.setattr(config, "transport_mode", "stdio")
    monkeypatch.setenv("UNITY_MCP_STATUS_DIR", str(tmp_path))
    monkeypatch.setenv("UNITY_MCP_STDIO_STATUS_TTL_SECONDS", "0")

    recent_heartbeat = datetime.now(timezone.utc).isoformat()

    _write_status_file(
        tmp_path / "unity-mcp-status-abc123.json",
        {"project_hash": "abc123", "enabled_tools": ["manage_scene"], "last_heartbeat": recent_heartbeat},
    )

    middleware = UnityInstanceMiddleware()
    # TTL=0 should use default of 15, so recent file should be fresh
    names = await _filter_tool_names(middleware, _build_fastmcp_context("Project@abc123"))

    assert "manage_scene" in names


@pytest.mark.asyncio
async def test_ttl_negative_uses_default(monkeypatch, tmp_path):
    """Negative TTL should fall back to default."""
    monkeypatch.setattr(config, "transport_mode", "stdio")
    monkeypatch.setenv("UNITY_MCP_STATUS_DIR", str(tmp_path))
    monkeypatch.setenv("UNITY_MCP_STDIO_STATUS_TTL_SECONDS", "-5")

    recent_heartbeat = datetime.now(timezone.utc).isoformat()

    _write_status_file(
        tmp_path / "unity-mcp-status-abc123.json",
        {"project_hash": "abc123", "enabled_tools": ["manage_scene"], "last_heartbeat": recent_heartbeat},
    )

    middleware = UnityInstanceMiddleware()
    names = await _filter_tool_names(middleware, _build_fastmcp_context("Project@abc123"))

    assert "manage_scene" in names


# ---------------------------------------------------------------------------
# Watch interval edge cases
# ---------------------------------------------------------------------------

@pytest.mark.asyncio
async def test_watch_interval_below_minimum_clamped(monkeypatch):
    """Watch interval below 0.2 should be clamped to 0.2."""
    monkeypatch.setattr(config, "transport_mode", "stdio")
    monkeypatch.setenv("UNITY_MCP_STDIO_TOOLS_WATCH_INTERVAL_SECONDS", "0.1")

    middleware = UnityInstanceMiddleware()
    assert middleware._get_stdio_tools_watch_interval_seconds() == 0.2


@pytest.mark.asyncio
async def test_watch_interval_negative_clamped_to_minimum(monkeypatch):
    """Negative watch interval should be clamped to minimum 0.2."""
    monkeypatch.setattr(config, "transport_mode", "stdio")
    monkeypatch.setenv("UNITY_MCP_STDIO_TOOLS_WATCH_INTERVAL_SECONDS", "-1")

    middleware = UnityInstanceMiddleware()
    # Negative values are parsed then clamped to minimum
    assert middleware._get_stdio_tools_watch_interval_seconds() == 0.2


@pytest.mark.asyncio
async def test_watch_interval_invalid_string_uses_default(monkeypatch):
    """Invalid watch interval string should use default."""
    monkeypatch.setattr(config, "transport_mode", "stdio")
    monkeypatch.setenv("UNITY_MCP_STDIO_TOOLS_WATCH_INTERVAL_SECONDS", "not-a-number")

    middleware = UnityInstanceMiddleware()
    assert middleware._get_stdio_tools_watch_interval_seconds() == 1.0


# ---------------------------------------------------------------------------
# Session tracking edge cases
# ---------------------------------------------------------------------------

@pytest.mark.asyncio
async def test_track_session_with_none_context_returns_false(monkeypatch):
    """None context should return False without error."""
    monkeypatch.setattr(config, "transport_mode", "stdio")
    middleware = UnityInstanceMiddleware()

    result = middleware._track_session_from_context(None)
    assert result is False


@pytest.mark.asyncio
async def test_track_session_with_none_request_context_returns_false(monkeypatch):
    """Context with None request_context should return False."""
    monkeypatch.setattr(config, "transport_mode", "stdio")
    middleware = UnityInstanceMiddleware()

    ctx = SimpleNamespace(request_context=None)
    result = middleware._track_session_from_context(ctx)
    assert result is False


@pytest.mark.asyncio
async def test_track_session_with_empty_session_id_returns_false(monkeypatch):
    """Empty session_id should return False."""
    monkeypatch.setattr(config, "transport_mode", "stdio")
    middleware = UnityInstanceMiddleware()

    ctx = SimpleNamespace(
        request_context=object(),
        session_id="",
        session=object(),
    )
    result = middleware._track_session_from_context(ctx)
    assert result is False


@pytest.mark.asyncio
async def test_track_session_with_none_session_id_returns_false(monkeypatch):
    """None session_id should return False."""
    monkeypatch.setattr(config, "transport_mode", "stdio")
    middleware = UnityInstanceMiddleware()

    ctx = SimpleNamespace(
        request_context=object(),
        session_id=None,
        session=object(),
    )
    result = middleware._track_session_from_context(ctx)
    assert result is False


@pytest.mark.asyncio
async def test_track_same_session_twice_returns_false_second_time(monkeypatch):
    """Tracking the same session twice should return False on second call."""
    monkeypatch.setattr(config, "transport_mode", "stdio")
    middleware = UnityInstanceMiddleware()

    session = object()
    ctx = SimpleNamespace(
        request_context=object(),
        session_id="session-1",
        session=session,
    )

    result1 = middleware._track_session_from_context(ctx)
    result2 = middleware._track_session_from_context(ctx)

    assert result1 is True
    assert result2 is False


@pytest.mark.asyncio
async def test_notify_with_no_tracked_sessions_returns_early(monkeypatch):
    """Notify with no tracked sessions should return immediately without error."""
    monkeypatch.setattr(config, "transport_mode", "stdio")
    middleware = UnityInstanceMiddleware()

    # Should not raise
    await middleware._notify_tool_list_changed_to_sessions("test")


@pytest.mark.asyncio
async def test_all_sessions_fail_during_notify_clears_all(monkeypatch):
    """If all sessions fail during notify, all should be cleared."""
    monkeypatch.setattr(config, "transport_mode", "stdio")
    middleware = UnityInstanceMiddleware()

    async def _raise():
        raise RuntimeError("connection lost")

    failing_session = SimpleNamespace(send_tool_list_changed=AsyncMock(side_effect=_raise))
    middleware._tracked_sessions["fail1"] = failing_session
    middleware._tracked_sessions["fail2"] = failing_session

    await middleware._notify_tool_list_changed_to_sessions("test")

    assert len(middleware._tracked_sessions) == 0


# ---------------------------------------------------------------------------
# Watcher lifecycle edge cases
# ---------------------------------------------------------------------------

@pytest.mark.asyncio
async def test_start_watcher_twice_only_creates_one_task(monkeypatch):
    """Starting watcher twice should only create one task."""
    monkeypatch.setattr(config, "transport_mode", "stdio")
    monkeypatch.setenv("UNITY_MCP_STDIO_TOOLS_WATCH_INTERVAL_SECONDS", "1.0")
    middleware = UnityInstanceMiddleware()

    await middleware.start_stdio_tools_watcher()
    first_task = middleware._stdio_tools_watch_task

    await middleware.start_stdio_tools_watcher()
    second_task = middleware._stdio_tools_watch_task

    assert first_task is second_task
    assert not first_task.done()

    await middleware.stop_stdio_tools_watcher()


@pytest.mark.asyncio
async def test_stop_watcher_when_not_started_is_safe(monkeypatch):
    """Stopping watcher when not started should not raise."""
    monkeypatch.setattr(config, "transport_mode", "stdio")
    middleware = UnityInstanceMiddleware()

    # Should not raise
    await middleware.stop_stdio_tools_watcher()
    assert middleware._stdio_tools_watch_task is None


@pytest.mark.asyncio
async def test_stop_watcher_clears_tracked_sessions(monkeypatch):
    """Stopping watcher should clear all tracked sessions."""
    monkeypatch.setattr(config, "transport_mode", "stdio")
    middleware = UnityInstanceMiddleware()
    middleware._tracked_sessions["s1"] = object()
    middleware._tracked_sessions["s2"] = object()

    await middleware.stop_stdio_tools_watcher()

    assert len(middleware._tracked_sessions) == 0


@pytest.mark.asyncio
async def test_watcher_continues_after_iteration_error(monkeypatch):
    """Watcher should continue after an iteration error."""
    monkeypatch.setattr(config, "transport_mode", "stdio")
    monkeypatch.setenv("UNITY_MCP_STDIO_TOOLS_WATCH_INTERVAL_SECONDS", "0.2")
    middleware = UnityInstanceMiddleware()
    middleware._notify_tool_list_changed_to_sessions = AsyncMock(return_value=None)

    call_count = {"value": 0}
    error_on_call = 1

    def _fake_signature():
        call_count["value"] += 1
        if call_count["value"] == error_on_call:
            # First call is initial state, second call is first iteration
            pass
        if call_count["value"] == 2:
            raise RuntimeError("simulated error")
        if call_count["value"] >= 4:
            # Stop after a few successful iterations
            raise asyncio.CancelledError()
        return (("hash1", ("tool1",)),)

    monkeypatch.setattr(middleware, "_build_stdio_tools_state_signature", _fake_signature)

    await middleware.start_stdio_tools_watcher()
    try:
        await asyncio.sleep(0.5)
    except asyncio.CancelledError:
        pass
    finally:
        await middleware.stop_stdio_tools_watcher()

    # Should have continued despite error on call 2
    assert call_count["value"] >= 3


# ---------------------------------------------------------------------------
# Active instance resolution edge cases
# ---------------------------------------------------------------------------

@pytest.mark.asyncio
async def test_active_instance_without_at_symbol_uses_full_string(monkeypatch, tmp_path):
    """Active instance without @ should be treated as bare hash."""
    monkeypatch.setattr(config, "transport_mode", "stdio")
    monkeypatch.setenv("UNITY_MCP_STATUS_DIR", str(tmp_path))

    _write_status_file(
        tmp_path / "unity-mcp-status-abc123.json",
        {"project_hash": "abc123", "enabled_tools": ["manage_scene"]},
    )

    middleware = UnityInstanceMiddleware()
    # Bare hash without @
    names = await _filter_tool_names(middleware, _build_fastmcp_context("abc123"))

    assert "manage_scene" in names
    assert "manage_asset" not in names


@pytest.mark.asyncio
async def test_active_instance_is_none_passes_all_tools(monkeypatch, tmp_path):
    """None active instance should pass all tools when no status files."""
    monkeypatch.setattr(config, "transport_mode", "stdio")
    monkeypatch.setenv("UNITY_MCP_STATUS_DIR", str(tmp_path))

    middleware = UnityInstanceMiddleware()
    names = await _filter_tool_names(middleware, _build_fastmcp_context(None))

    # All tools should pass
    assert "manage_scene" in names
    assert "manage_script" in names
    assert "manage_asset" in names
    assert "server_only_tool" in names


# ---------------------------------------------------------------------------
# Signature building edge cases
# ---------------------------------------------------------------------------

@pytest.mark.asyncio
async def test_signature_empty_payloads_returns_empty(monkeypatch, tmp_path):
    """Empty payloads should return empty signature."""
    monkeypatch.setattr(config, "transport_mode", "stdio")
    monkeypatch.setenv("UNITY_MCP_STATUS_DIR", str(tmp_path))

    middleware = UnityInstanceMiddleware()
    signature = middleware._build_stdio_tools_state_signature()

    assert signature == ()


@pytest.mark.asyncio
async def test_signature_deduplicates_same_project_hash(monkeypatch, tmp_path):
    """Multiple files with same project_hash should be deduplicated."""
    monkeypatch.setattr(config, "transport_mode", "stdio")
    monkeypatch.setenv("UNITY_MCP_STATUS_DIR", str(tmp_path))

    _write_status_file(
        tmp_path / "unity-mcp-status-abc111.json",
        {"project_hash": "samehash", "enabled_tools": ["manage_scene"]},
    )
    _write_status_file(
        tmp_path / "unity-mcp-status-abc222.json",
        {"project_hash": "samehash", "enabled_tools": ["manage_asset"]},
    )

    middleware = UnityInstanceMiddleware()
    signature = middleware._build_stdio_tools_state_signature()

    # Only one entry for samehash (first by filename sort order)
    assert len(signature) == 1
    assert signature[0][0] == "samehash"


@pytest.mark.asyncio
async def test_signature_enabled_tools_as_set_converted_to_tuple(monkeypatch, tmp_path):
    """enabled_tools as set should be converted to sorted tuple."""
    monkeypatch.setattr(config, "transport_mode", "stdio")
    monkeypatch.setenv("UNITY_MCP_STATUS_DIR", str(tmp_path))

    _write_status_file(
        tmp_path / "unity-mcp-status-abc123.json",
        {"project_hash": "abc123", "enabled_tools": ["z_tool", "a_tool", "m_tool"]},
    )

    middleware = UnityInstanceMiddleware()
    signature = middleware._build_stdio_tools_state_signature()

    # Should be sorted
    assert signature[0][1] == ("a_tool", "m_tool", "z_tool")


# ---------------------------------------------------------------------------
# Status directory edge cases
# ---------------------------------------------------------------------------

@pytest.mark.asyncio
async def test_status_dir_does_not_exist_returns_empty(monkeypatch, tmp_path):
    """Non-existent status directory should return empty payloads."""
    monkeypatch.setattr(config, "transport_mode", "stdio")
    non_existent = tmp_path / "does-not-exist"
    monkeypatch.setenv("UNITY_MCP_STATUS_DIR", str(non_existent))

    middleware = UnityInstanceMiddleware()
    payloads = middleware._list_stdio_status_payloads()

    assert payloads == []


@pytest.mark.asyncio
async def test_status_dir_custom_path_expanded(monkeypatch, tmp_path):
    """Custom status dir path with ~ should be expanded."""
    monkeypatch.setattr(config, "transport_mode", "stdio")
    # Use tmp_path as a substitute for home directory expansion
    monkeypatch.setenv("UNITY_MCP_STATUS_DIR", str(tmp_path))

    _write_status_file(
        tmp_path / "unity-mcp-status-abc123.json",
        {"project_hash": "abc123", "enabled_tools": ["manage_scene"]},
    )

    middleware = UnityInstanceMiddleware()
    payloads = middleware._list_stdio_status_payloads()

    assert len(payloads) == 1
    assert payloads[0]["project_hash"] == "abc123"


# ---------------------------------------------------------------------------
# Concurrent operation tests
# ---------------------------------------------------------------------------

@pytest.mark.asyncio
async def test_concurrent_start_stop_watcher_safe(monkeypatch):
    """Concurrent start/stop operations should be safe."""
    monkeypatch.setattr(config, "transport_mode", "stdio")
    monkeypatch.setenv("UNITY_MCP_STDIO_TOOLS_WATCH_INTERVAL_SECONDS", "0.5")
    middleware = UnityInstanceMiddleware()

    # Start multiple times concurrently
    await asyncio.gather(
        middleware.start_stdio_tools_watcher(),
        middleware.start_stdio_tools_watcher(),
        middleware.start_stdio_tools_watcher(),
    )

    assert middleware._stdio_tools_watch_task is not None
    assert not middleware._stdio_tools_watch_task.done()

    # Stop multiple times concurrently
    await asyncio.gather(
        middleware.stop_stdio_tools_watcher(),
        middleware.stop_stdio_tools_watcher(),
    )

    assert middleware._stdio_tools_watch_task is None
