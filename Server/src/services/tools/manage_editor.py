from typing import Annotated, Any, Literal

from fastmcp import Context
from mcp.types import ToolAnnotations

from services.registry import mcp_for_unity_tool
from core.telemetry import is_telemetry_enabled, record_tool_usage
from services.tools import get_unity_instance_from_context
from transport.unity_transport import send_with_unity_instance
from transport.legacy.unity_connection import async_send_command_with_retry

@mcp_for_unity_tool(
    description="Controls and queries the Unity editor's state and settings. Read-only actions: telemetry_status, telemetry_ping. Modifying actions: play, pause, stop, set_active_tool, add_tag, remove_tag, add_layer, remove_layer, deploy_package, restore_package, undo, redo, capture_screenshot. For prefab editing (open/save/close prefab stage), use manage_prefabs. deploy_package copies the configured MCPForUnity source folder into the project's installed package location (triggers recompile, no confirmation dialog). restore_package reverts to the pre-deployment backup. undo/redo perform Unity editor undo/redo and return the affected group name. capture_screenshot takes a PNG of the Game View (Edit or Play mode) and returns the path and optional base64 data.",
    annotations=ToolAnnotations(
        title="Manage Editor",
    ),
)
async def manage_editor(
    ctx: Context,
    action: Annotated[Literal["telemetry_status", "telemetry_ping", "play", "pause", "stop", "set_active_tool", "add_tag", "remove_tag", "add_layer", "remove_layer", "deploy_package", "restore_package", "undo", "redo", "capture_screenshot"], "Get and update the Unity Editor state. deploy_package copies the configured MCPForUnity source into the project's package location. restore_package reverts. undo/redo perform editor undo/redo. capture_screenshot takes a PNG screenshot of the Game View (supports Edit/Play mode)."],
    tool_name: Annotated[str,
                         "Tool name when setting active tool"] | None = None,
    tag_name: Annotated[str,
                        "Tag name when adding and removing tags"] | None = None,
    layer_name: Annotated[str,
                          "Layer name when adding and removing layers"] | None = None,
    file_name: Annotated[str, "Optional file name for screenshot (default: timestamped)."] | None = None,
    include_image: Annotated[bool, "Whether to include base64 image data in response (default: false)."] | None = None,
    max_resolution: Annotated[int, "Max edge resolution for base64 image (default: 640)."] | None = None,
    refresh: Annotated[bool, "Whether to perform an AssetDatabase.Refresh before and after (default: false). Recommended for UI verification."] | None = None,
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

        # Prepare parameters, removing None values
        params = {
            "action": action,
            "toolName": tool_name,
            "tagName": tag_name,
            "layerName": layer_name,
            "fileName": file_name,
            "includeImage": include_image,
            "maxResolution": max_resolution,
            "refresh": refresh,
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
