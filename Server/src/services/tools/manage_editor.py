from typing import Annotated, Any, Literal

from fastmcp import Context
from mcp.types import ToolAnnotations

from core.telemetry import is_telemetry_enabled, record_tool_usage
from services.registry import mcp_for_unity_tool
from services.tools import get_unity_instance_from_context
from transport.legacy.unity_connection import async_send_command_with_retry
from transport.unity_transport import send_with_unity_instance

@mcp_for_unity_tool(
    description="Controls and queries the Unity editor's state and settings. Read-only actions: telemetry_status, telemetry_ping, wait_for_compilation. wait_for_compilation polls until compilation and domain reload finish; its timeout is clamped to 1-120 seconds (default 30). Modifying actions: play, pause, stop, set_active_tool, add_tag, remove_tag, add_layer, remove_layer, deploy_package, restore_package, undo, redo. For prefab editing (open/save/close prefab stage), use manage_prefabs. deploy_package copies the configured MCPForUnity source folder into the project's installed package location (triggers recompile, no confirmation dialog). restore_package reverts to the pre-deployment backup. undo/redo perform Unity editor undo/redo and return the affected group name.",
    annotations=ToolAnnotations(
        title="Manage Editor",
    ),
)
async def manage_editor(
    ctx: Context,
    action: Annotated[Literal["telemetry_status", "telemetry_ping", "wait_for_compilation", "play", "pause", "stop", "set_active_tool", "add_tag", "remove_tag", "add_layer", "remove_layer", "deploy_package", "restore_package", "undo", "redo"], "Get and update the Unity Editor state. deploy_package copies the configured MCPForUnity source into the project's package location (triggers recompile). restore_package reverts the last deployment from backup. undo/redo perform editor undo/redo. For prefab editing (open/save/close prefab stage), use manage_prefabs."],
    timeout: Annotated[int | float | None,
                       "Timeout in seconds for wait_for_compilation (default: 30, clamped to 1-120)."] = None,
    tool_name: Annotated[str,
                         "Tool name when setting active tool"] | None = None,
    tag_name: Annotated[str,
                        "Tag name when adding and removing tags"] | None = None,
    layer_name: Annotated[str,
                          "Layer name when adding and removing layers"] | None = None,
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

        if action == "wait_for_compilation":
            return await _wait_for_compilation(ctx, timeout)

        # Prepare parameters, removing None values
        params = {
            "action": action,
            "toolName": tool_name,
            "tagName": tag_name,
            "layerName": layer_name,
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


async def _wait_for_compilation(ctx: Context, timeout: int | float | None) -> dict[str, Any]:
    """Poll editor_state until compilation and domain reload finish.

    The timeout is clamped to the inclusive range [1.0, 120.0] seconds to
    keep waits bounded in the Unity editor.
    """
    from services.tools.refresh_unity import wait_for_editor_ready

    timeout_s = float(timeout) if timeout is not None else 30.0
    timeout_s = max(1.0, min(timeout_s, 120.0))
    ready, elapsed = await wait_for_editor_ready(ctx, timeout_s=timeout_s)

    if ready:
        return {
            "success": True,
            "message": "Compilation complete. Editor is ready.",
            "data": {
                "waited_seconds": round(elapsed, 2),
                "ready": True,
            },
        }

    return {
        "success": False,
        "message": f"Timed out after {timeout_s:.0f}s waiting for compilation to finish.",
        "data": {
            "waited_seconds": round(elapsed, 2),
            "ready": False,
            "timeout_seconds": timeout_s,
        },
    }
