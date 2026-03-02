"""
manage_tools – server-only meta-tool for dynamic tool group activation.

This tool lets the AI assistant (or user) discover available tool groups
and selectively enable / disable them for the current session. Activating
a group makes its tools appear in tool listings; deactivating hides them.

Works on all transports (stdio, HTTP, SSE) via FastMCP 3.x native
per-session visibility.
"""
from typing import Annotated, Any, Literal

from fastmcp import Context
from mcp.types import ToolAnnotations

from services.registry import (
    mcp_for_unity_tool,
    TOOL_GROUPS,
    DEFAULT_ENABLED_GROUPS,
    get_group_tool_names,
)


@mcp_for_unity_tool(
    unity_target=None,
    group=None,
    description=(
        "Manage which tool groups are visible in this session. "
        "Actions: list_groups (show all groups and their status), "
        "activate (enable a group), deactivate (disable a group), "
        "reset (restore defaults). "
        "Activating a group makes its tools appear; deactivating hides them."
    ),
    annotations=ToolAnnotations(
        title="Manage Tools",
        readOnlyHint=False,
    ),
)
async def manage_tools(
    ctx: Context,
    action: Annotated[
        Literal["list_groups", "activate", "deactivate", "reset"],
        "Action to perform."
    ],
    group: Annotated[
        str | None,
        "Group name (required for activate / deactivate). "
        "Valid groups: " + ", ".join(sorted(TOOL_GROUPS.keys()))
    ] = None,
) -> dict[str, Any]:
    if action == "list_groups":
        return _list_groups()

    if action == "activate":
        if not group:
            return {"error": "group is required for activate"}
        if group not in TOOL_GROUPS:
            return {"error": f"Unknown group '{group}'. Valid: {', '.join(sorted(TOOL_GROUPS))}"}
        tag = f"group:{group}"
        await ctx.info(f"Activating tool group: {group}")
        await ctx.enable_components(tags={tag}, components={"tool"})
        return {
            "activated": group,
            "tools": get_group_tool_names().get(group, []),
            "message": f"Group '{group}' is now visible. Its tools will appear in tool listings.",
        }

    if action == "deactivate":
        if not group:
            return {"error": "group is required for deactivate"}
        if group not in TOOL_GROUPS:
            return {"error": f"Unknown group '{group}'. Valid: {', '.join(sorted(TOOL_GROUPS))}"}
        tag = f"group:{group}"
        await ctx.info(f"Deactivating tool group: {group}")
        await ctx.disable_components(tags={tag}, components={"tool"})
        return {
            "deactivated": group,
            "tools": get_group_tool_names().get(group, []),
            "message": f"Group '{group}' is now hidden.",
        }

    if action == "reset":
        await ctx.info("Resetting tool visibility to defaults")
        await ctx.reset_visibility()
        return {
            "reset": True,
            "default_groups": sorted(DEFAULT_ENABLED_GROUPS),
            "message": "Tool visibility restored to server defaults.",
        }

    return {"error": f"Unknown action '{action}'"}


def _list_groups() -> dict[str, Any]:
    """Build the list_groups response with group metadata and tool names."""
    group_tools = get_group_tool_names()
    groups = []
    for name in sorted(TOOL_GROUPS.keys()):
        groups.append({
            "name": name,
            "description": TOOL_GROUPS[name],
            "default_enabled": name in DEFAULT_ENABLED_GROUPS,
            "tools": group_tools.get(name, []),
            "tool_count": len(group_tools.get(name, [])),
        })
    return {
        "groups": groups,
        "note": (
            "Use activate/deactivate to toggle groups for this session. "
            "Tools with group=None (server meta-tools) are always visible."
        ),
    }
