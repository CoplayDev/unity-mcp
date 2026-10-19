from typing import Annotated, Any, Literal

from fastmcp import Context
from mcp.types import ToolAnnotations
from pydantic import Field

from services.registry import mcp_for_unity_tool
from core.telemetry import is_telemetry_enabled, record_tool_usage
from services.tools import get_unity_instance_from_context
from transport.unity_transport import send_with_unity_instance
from transport.legacy.unity_connection import async_send_command_with_retry

@mcp_for_unity_tool(
    description="Controls and queries the Unity editor's state and settings. Read-only actions: telemetry_status, telemetry_ping, get_game_view_size. Modifying actions: play, pause, stop, set_active_tool, add_tag, remove_tag, add_layer, remove_layer, deploy_package, restore_package, undo, redo, set_game_view_size. Game View size actions require a graphical Editor and an open Game View (focused, or the only open Game View). Make it visible before setting its size so Unity can repaint. set_game_view_size requires integer width and height (10..8192); it reuses one MCP-owned preset per platform group in shared Editor preferences and waits for render-target size readback. It preserves other presets and does not change focus. Read requested_size, target_size, render_size and settled separately: GPU limits can reduce the actual size; a timeout can leave the new preset selected. For prefab editing (open/save/close prefab stage), use manage_prefabs. deploy_package copies the configured MCPForUnity source folder into the project's installed package location (triggers recompile, no confirmation dialog). restore_package reverts to the pre-deployment backup. undo/redo perform Unity editor undo/redo and return the affected group name.",
    annotations=ToolAnnotations(
        title="Manage Editor",
        readOnlyHint=False,
        destructiveHint=True,
    ),
)
async def manage_editor(
    ctx: Context,
    action: Annotated[Literal["telemetry_status", "telemetry_ping", "play", "pause", "stop", "set_active_tool", "add_tag", "remove_tag", "add_layer", "remove_layer", "deploy_package", "restore_package", "undo", "redo", "get_game_view_size", "set_game_view_size"], "Get and update the Unity Editor state. deploy_package copies the configured MCPForUnity source into the project's package location (triggers recompile). restore_package reverts the last deployment from backup. undo/redo perform editor undo/redo. For prefab editing (open/save/close prefab stage), use manage_prefabs."],
    tool_name: Annotated[str,
                         "Tool name when setting active tool"] | None = None,
    tag_name: Annotated[str,
                        "Tag name when adding and removing tags"] | None = None,
    layer_name: Annotated[str,
                          "Layer name when adding and removing layers"] | None = None,
    width: Annotated[int, Field(strict=True, ge=10, le=8192),
                     "Fixed Game View width in pixels (10..8192), required for set_game_view_size"] | None = None,
    height: Annotated[int, Field(strict=True, ge=10, le=8192),
                      "Fixed Game View height in pixels (10..8192), required for set_game_view_size"] | None = None,
) -> dict[str, Any]:
    # Get active instance from request state (injected by middleware)
    unity_instance = await get_unity_instance_from_context(ctx)

    try:
        # Diagnostics: quick telemetry checks
        if action == "telemetry_status":
            return {"success": True, "telemetry_enabled": is_telemetry_enabled()}

        if action == "telemetry_ping":
            record_tool_usage("diagnostic_ping", True, 1.0, None)
            return {"success": True, "message": "telemetry ping queued"}

        if action == "set_game_view_size":
            for name, value in (("width", width), ("height", height)):
                if type(value) is not int or not 10 <= value <= 8192:
                    return {"success": False, "message": f"{name} must be an integer between 10 and 8192."}
        elif width is not None or height is not None:
            return {"success": False, "message": "width and height are only valid for set_game_view_size."}

        # Prepare parameters, removing None values
        params = {
            "action": action,
            "toolName": tool_name,
            "tagName": tag_name,
            "layerName": layer_name,
            "width": width,
            "height": height,
        }
        params = {k: v for k, v in params.items() if v is not None}

        # Send command using centralized retry helper with instance routing
        response = await send_with_unity_instance(async_send_command_with_retry, unity_instance, "manage_editor", params)

        # Preserve structured failure data; unwrap success into a friendlier shape
        if isinstance(response, dict) and response.get("success"):
            return {"success": True, "message": response.get("message", "Editor operation successful."), "data": response.get("data")}
        return response if isinstance(response, dict) else {"success": False, "message": str(response)}

    except Exception as e:
        return {"success": False, "message": f"Python error managing editor: {str(e)}"}
