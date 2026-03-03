"""
manage_tools - server-only meta-tool for dynamic tool group activation.

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
        "sync (refresh visibility from Unity Editor's toggle states), "
        "reset (restore defaults). "
        "Activating a group makes its tools appear; deactivating hides them. "
        "Use sync after toggling tools in the Unity Editor GUI."
    ),
    annotations=ToolAnnotations(
        title="Manage Tools",
        readOnlyHint=False,
    ),
)
async def manage_tools(
    ctx: Context,
    action: Annotated[
        Literal["list_groups", "activate", "deactivate", "sync", "reset"],
        "Action to perform."
    ],
    group: Annotated[
        str | None,
        "Group name (required for activate / deactivate). "
        "Valid groups: " + ", ".join(sorted(TOOL_GROUPS.keys()))
    ] = None,
) -> dict[str, Any]:
    if action == "list_groups":
        return await _list_groups(ctx)

    if action in ("activate", "deactivate"):
        if not group:
            return {"error": f"group is required for {action}"}
        group = group.strip().lower()
        if group not in TOOL_GROUPS:
            return {"error": f"Unknown group '{group}'. Valid: {', '.join(sorted(TOOL_GROUPS))}"}

    if action == "activate":
        tag = f"group:{group}"
        await ctx.info(f"Activating tool group: {group}")
        await ctx.enable_components(tags={tag}, components={"tool"})
        return {
            "activated": group,
            "tools": get_group_tool_names().get(group, []),
            "message": f"Group '{group}' is now visible. Its tools will appear in tool listings.",
        }

    if action == "deactivate":
        tag = f"group:{group}"
        await ctx.info(f"Deactivating tool group: {group}")
        await ctx.disable_components(tags={tag}, components={"tool"})
        return {
            "deactivated": group,
            "tools": get_group_tool_names().get(group, []),
            "message": f"Group '{group}' is now hidden.",
        }

    if action == "sync":
        await ctx.info("Syncing tool visibility from Unity Editor...")
        from services.tools import sync_tool_visibility_from_unity
        result = await sync_tool_visibility_from_unity(notify=True)
        if result.get("error"):
            msg = result["error"]
            if result.get("unsupported"):
                msg = (
                    "The connected Unity Editor does not support tool state syncing yet. "
                    "Update the MCPForUnity package to the latest version, then try again. "
                    "In the meantime, use activate/deactivate actions to toggle groups manually."
                )
            else:
                msg = f"Failed to sync tool visibility from Unity. Is Unity running? ({msg})"
            return {"error": msg}
        return {
            "synced": True,
            "enabled_groups": result.get("enabled_groups", []),
            "disabled_groups": result.get("disabled_groups", []),
            "enabled_tool_count": result.get("enabled_tool_count", 0),
            "total_tool_count": result.get("total_tool_count", 0),
            "message": (
                "Tool visibility synced from Unity Editor. "
                f"Enabled groups: {', '.join(result.get('enabled_groups', []))}. "
                f"Disabled groups: {', '.join(result.get('disabled_groups', []) or ['none'])}."
            ),
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


async def _list_groups(ctx: Context) -> dict[str, Any]:
    """Build the list_groups response with group metadata and tool names."""
    group_tools = get_group_tool_names()

    # Determine current session-enabled state for each group.
    # Session rules accumulate; the last rule whose tags include "group:<name>" wins.
    session_enabled: dict[str, bool] = {}
    try:
        rules = await ctx._get_visibility_rules()
        for rule in rules:
            tags = rule.get("tags") or []
            enabled = rule.get("enabled", True)
            for tag in tags:
                if isinstance(tag, str) and tag.startswith("group:"):
                    group_name = tag[len("group:"):]
                    session_enabled[group_name] = enabled
    except Exception:
        pass  # No active session or unsupported – fall back to defaults

    groups = []
    for name in sorted(TOOL_GROUPS.keys()):
        if name in session_enabled:
            currently_enabled = session_enabled[name]
        else:
            currently_enabled = name in DEFAULT_ENABLED_GROUPS
        groups.append({
            "name": name,
            "description": TOOL_GROUPS[name],
            "enabled": currently_enabled,
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
