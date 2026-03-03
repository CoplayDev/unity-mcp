import pytest

from services.registry import get_registered_tools, mcp_for_unity_tool
import services.registry.tool_registry as tool_registry_module


@pytest.fixture(autouse=True)
def restore_tool_registry_state():
    original_registry = list(tool_registry_module._tool_registry)
    try:
        yield
    finally:
        tool_registry_module._tool_registry[:] = original_registry


def test_tool_registry_defaults_unity_target_to_tool_name():
    @mcp_for_unity_tool()
    def _default_target_tool():
        return None

    registered_tools = get_registered_tools()
    tool_info = next(item for item in registered_tools if item["name"] == "_default_target_tool")
    assert tool_info["unity_target"] == "_default_target_tool"


def test_tool_registry_supports_server_only_and_alias_targets():
    @mcp_for_unity_tool(unity_target=None)
    def _server_only_tool():
        return None

    @mcp_for_unity_tool(unity_target="manage_script")
    def _manage_script_alias_tool():
        return None

    registered_tools = get_registered_tools()
    server_only = next(item for item in registered_tools if item["name"] == "_server_only_tool")
    alias_tool = next(item for item in registered_tools if item["name"] == "_manage_script_alias_tool")

    assert server_only["unity_target"] is None
    assert alias_tool["unity_target"] == "manage_script"


def test_tool_registry_does_not_leak_unity_target_into_tool_kwargs():
    @mcp_for_unity_tool(unity_target="manage_script", annotations={"title": "x"})
    def _non_leaking_target_tool():
        return None

    registered_tools = get_registered_tools()
    tool_info = next(item for item in registered_tools if item["name"] == "_non_leaking_target_tool")
    assert tool_info["unity_target"] == "manage_script"
    assert "unity_target" not in tool_info["kwargs"]
    assert tool_info["kwargs"]["annotations"] == {"title": "x"}


def test_tool_registry_rejects_invalid_unity_target_values():
    with pytest.raises(ValueError, match="Invalid unity_target"):
        @mcp_for_unity_tool(unity_target="")
        def _invalid_empty_target_tool():
            return None

    with pytest.raises(ValueError, match="Invalid unity_target"):
        @mcp_for_unity_tool(unity_target=123)  # type: ignore[arg-type]
        def _invalid_non_string_target_tool():
            return None
