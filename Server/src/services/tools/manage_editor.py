from typing import Annotated, Any, Literal

from fastmcp import Context
from mcp.types import ToolAnnotations

from services.registry import mcp_for_unity_tool
from core.telemetry import is_telemetry_enabled, record_tool_usage
from services.tools import get_unity_instance_from_context
from transport.unity_transport import send_with_unity_instance
from transport.legacy.unity_connection import async_send_command_with_retry
from services.play_mode_startup import has_startup_options, run_play_startup

@mcp_for_unity_tool(
    description="Controls and queries the Unity editor's state and settings. Read-only actions: telemetry_status, telemetry_ping, get_play_mode_startup. Modifying actions: play, pause, stop, set_active_tool, add_tag, remove_tag, add_layer, remove_layer, deploy_package, restore_package, undo, redo. play can wait_until='scene_loaded' or 'first_frame', with wait_timeout (1-300 seconds; default 30). on_ready_code is a C# body executed once after first-frame startup using execute_code's compiler and safety checks; its result is returned as data.on_ready_result. Startup state survives domain reload without changing Enter Play Mode settings. After a timeout or disconnected reply, poll get_play_mode_startup with job_id instead of resubmitting code. Scene loading alone does not guarantee Start methods have run; use first_frame or on_ready_code for that. For prefab editing (open/save/close prefab stage), use manage_prefabs. deploy_package copies the configured MCPForUnity source folder into the project's installed package location (triggers recompile, no confirmation dialog). restore_package reverts to the pre-deployment backup. undo/redo perform Unity editor undo/redo and return the affected group name.",
    annotations=ToolAnnotations(
        title="Manage Editor",
        readOnlyHint=False,
        destructiveHint=True,
    ),
)
async def manage_editor(
    ctx: Context,
    action: Annotated[Literal["telemetry_status", "telemetry_ping", "play", "get_play_mode_startup", "pause", "stop", "set_active_tool", "add_tag", "remove_tag", "add_layer", "remove_layer", "deploy_package", "restore_package", "undo", "redo"], "Get and update the Unity Editor state. deploy_package copies the configured MCPForUnity source into the project's package location (triggers recompile). restore_package reverts the last deployment from backup. undo/redo perform editor undo/redo. For prefab editing (open/save/close prefab stage), use manage_prefabs."],
    tool_name: Annotated[str,
                         "Tool name when setting active tool"] | None = None,
    tag_name: Annotated[str,
                        "Tag name when adding and removing tags"] | None = None,
    layer_name: Annotated[str,
                          "Layer name when adding and removing layers"] | None = None,
    wait_until: Annotated[Literal["scene_loaded", "first_frame"], "Startup milestone to wait for on play. first_frame waits until Start and at least one frame have completed."] | None = None,
    on_ready_code: Annotated[str, "C# body to execute once after first-frame startup on play. Uses execute_code's default compiler and safety checks."] | None = None,
    wait_timeout: Annotated[float, "Maximum startup wait in seconds (1-300; default 30). A timeout does not stop Play Mode; poll the returned job_id."] | None = None,
    job_id: Annotated[str, "Startup job id returned by play; required for get_play_mode_startup."] | None = None,
) -> dict[str, Any]:
    # Get active instance from request state (injected by middleware)
    unity_instance = await get_unity_instance_from_context(ctx)

    try:
        if action != "play" and any(value is not None for value in (wait_until, on_ready_code, wait_timeout)):
            return {"success": False, "error": "Startup options are only supported by play."}
        if action == "get_play_mode_startup" and not (job_id and job_id.strip()):
            return {"success": False, "error": "job_id is required for get_play_mode_startup."}
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
            "waitUntil": wait_until,
            "onReadyCode": on_ready_code,
            "waitTimeout": wait_timeout,
            "jobId": job_id,
        }
        params = {k: v for k, v in params.items() if v is not None}

        # Send command using centralized retry helper with instance routing
        if action == "play" and has_startup_options(params):
            async def send_startup(wire_params: dict[str, Any], bound_instance: str | None) -> dict[str, Any]:
                return await send_with_unity_instance(async_send_command_with_retry,
                    bound_instance or unity_instance, "manage_editor", wire_params,
                    retry_on_reload=False, max_retries=0)
            response = await run_play_startup(send_startup, params)
        else:
            response = await send_with_unity_instance(async_send_command_with_retry, unity_instance, "manage_editor", params)

        # Preserve structured failure data; unwrap success into a friendlier shape
        if isinstance(response, dict) and response.get("success"):
            return {"success": True, "message": response.get("message", "Editor operation successful."), "data": response.get("data")}
        return response if isinstance(response, dict) else {"success": False, "message": str(response)}

    except Exception as e:
        return {"success": False, "message": f"Python error managing editor: {str(e)}"}
