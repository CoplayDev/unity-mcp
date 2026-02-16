import json
from types import SimpleNamespace
from unittest.mock import AsyncMock, Mock, patch

import pytest

from core.config import config
from transport.unity_instance_middleware import UnityInstanceMiddleware


def _tool_registry_for_visibility_tests() -> list[dict]:
    return [
        {"name": "manage_scene", "unity_target": "manage_scene"},
        {"name": "manage_script", "unity_target": "manage_script"},
        {"name": "manage_asset", "unity_target": "manage_asset"},
        {"name": "create_script", "unity_target": "manage_script"},
        {"name": "set_active_instance", "unity_target": None},
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
        SimpleNamespace(name="manage_asset"),
        SimpleNamespace(name="create_script"),
        SimpleNamespace(name="set_active_instance"),
        SimpleNamespace(name="custom_server_tool"),
    ]

    async def call_next(_ctx):
        return available_tools

    with patch.object(middleware, "_inject_unity_instance", new=AsyncMock()):
        with patch(
            "transport.unity_instance_middleware.get_registered_tools",
            return_value=_tool_registry_for_visibility_tests(),
        ):
            filtered = await middleware.on_list_tools(middleware_ctx, call_next)

    return [tool.name for tool in filtered]


@pytest.mark.asyncio
async def test_stdio_list_tools_filters_enabled_tools(monkeypatch, tmp_path):
    monkeypatch.setattr(config, "transport_mode", "stdio")
    monkeypatch.setenv("UNITY_MCP_STATUS_DIR", str(tmp_path))
    _write_status_file(
        tmp_path / "unity-mcp-status-abc123.json",
        {
            "project_hash": "abc123",
            "enabled_tools": ["manage_scene", "manage_script"],
        },
    )

    middleware = UnityInstanceMiddleware()
    names = await _filter_tool_names(middleware, _build_fastmcp_context("Project@abc123"))

    assert "manage_scene" in names
    assert "create_script" in names
    assert "set_active_instance" in names
    assert "custom_server_tool" in names
    assert "manage_asset" not in names


@pytest.mark.asyncio
async def test_stdio_list_tools_skips_filter_when_status_file_missing(monkeypatch, tmp_path):
    monkeypatch.setattr(config, "transport_mode", "stdio")
    monkeypatch.setenv("UNITY_MCP_STATUS_DIR", str(tmp_path))

    middleware = UnityInstanceMiddleware()
    names = await _filter_tool_names(middleware, _build_fastmcp_context("Project@abc123"))

    assert "manage_scene" in names
    assert "manage_asset" in names
    assert "create_script" in names
    assert "set_active_instance" in names
    assert "custom_server_tool" in names


@pytest.mark.asyncio
async def test_stdio_list_tools_skips_filter_when_status_file_is_invalid_json(monkeypatch, tmp_path):
    monkeypatch.setattr(config, "transport_mode", "stdio")
    monkeypatch.setenv("UNITY_MCP_STATUS_DIR", str(tmp_path))
    (tmp_path / "unity-mcp-status-abc123.json").write_text("{invalid", encoding="utf-8")

    middleware = UnityInstanceMiddleware()
    names = await _filter_tool_names(middleware, _build_fastmcp_context("Project@abc123"))

    assert "manage_scene" in names
    assert "manage_asset" in names
    assert "create_script" in names


@pytest.mark.asyncio
async def test_stdio_list_tools_prefers_active_instance_hash_when_multiple_files(monkeypatch, tmp_path):
    monkeypatch.setattr(config, "transport_mode", "stdio")
    monkeypatch.setenv("UNITY_MCP_STATUS_DIR", str(tmp_path))

    _write_status_file(
        tmp_path / "unity-mcp-status-first11.json",
        {
            "project_hash": "first11",
            "enabled_tools": ["manage_asset"],
        },
    )
    _write_status_file(
        tmp_path / "unity-mcp-status-second22.json",
        {
            "project_hash": "second22",
            "enabled_tools": ["manage_scene"],
        },
    )

    middleware = UnityInstanceMiddleware()
    names = await _filter_tool_names(middleware, _build_fastmcp_context("AnyName@first11"))

    assert "manage_asset" in names
    assert "manage_scene" not in names


@pytest.mark.asyncio
async def test_stdio_list_tools_uses_union_when_no_active_instance_and_multiple_hashes(monkeypatch, tmp_path):
    monkeypatch.setattr(config, "transport_mode", "stdio")
    monkeypatch.setenv("UNITY_MCP_STATUS_DIR", str(tmp_path))

    _write_status_file(
        tmp_path / "unity-mcp-status-first11.json",
        {
            "project_hash": "first11",
            "enabled_tools": ["manage_scene"],
        },
    )
    _write_status_file(
        tmp_path / "unity-mcp-status-second22.json",
        {
            "project_hash": "second22",
            "enabled_tools": ["manage_asset"],
        },
    )

    middleware = UnityInstanceMiddleware()
    names = await _filter_tool_names(middleware, _build_fastmcp_context(None))

    assert "manage_scene" in names
    assert "manage_asset" in names
    assert "set_active_instance" in names


@pytest.mark.asyncio
async def test_stdio_list_tools_skips_filter_when_enabled_tools_field_missing(monkeypatch, tmp_path):
    monkeypatch.setattr(config, "transport_mode", "stdio")
    monkeypatch.setenv("UNITY_MCP_STATUS_DIR", str(tmp_path))
    _write_status_file(
        tmp_path / "unity-mcp-status-abc123.json",
        {
            "project_hash": "abc123",
            "unity_port": 6400,
        },
    )

    middleware = UnityInstanceMiddleware()
    names = await _filter_tool_names(middleware, _build_fastmcp_context("Project@abc123"))

    assert "manage_scene" in names
    assert "manage_asset" in names
    assert "create_script" in names
