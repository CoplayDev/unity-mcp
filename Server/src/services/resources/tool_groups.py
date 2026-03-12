"""
tool_groups resource – exposes available tool groups and their metadata.

URI: mcpforunity://tool-groups
"""
from typing import Any

from fastmcp import Context

from services.registry import (
    mcp_for_unity_resource,
    TOOL_GROUPS,
    DEFAULT_ENABLED_GROUPS,
    get_group_tool_names,
)


@mcp_for_unity_resource(
    uri="mcpforunity://tool-groups",
    name="tool_groups",
    description=(
        "Available tool groups and their tools. "
        "Use manage_tools to activate/deactivate groups per session.\n\n"
        "URI: mcpforunity://tool-groups"
    ),
)
async def get_tool_groups(ctx: Context) -> dict[str, Any]:
    group_tools = get_group_tool_names()
    groups = []
    for name in sorted(TOOL_GROUPS.keys()):
        tools = group_tools.get(name, [])
        groups.append({
            "name": name,
            "description": TOOL_GROUPS[name],
            "default_enabled": name in DEFAULT_ENABLED_GROUPS,
            "tools": tools,
            "tool_count": len(tools),
        })
    return {
        "groups": groups,
        "total_groups": len(groups),
        "default_enabled": sorted(DEFAULT_ENABLED_GROUPS),
        "usage": "Call manage_tools(action='activate', group='<name>') to enable a group.",
    }
