from typing import Annotated, Any, Literal

from fastmcp import Context
from registry import mcp_for_unity_tool
from telemetry import is_telemetry_enabled, record_tool_usage
from unity_connection import send_command_with_retry


@mcp_for_unity_tool(
    description="Controls and queries the Unity editor's state and settings. Tip: pass booleans as true/false; if your client only sends strings, 'true'/'false' are accepted."
)
def manage_editor(
    ctx: Context,
    action: Annotated[Literal["telemetry_status", "telemetry_ping", "play", "pause", "stop", "get_state", "get_project_root", "get_windows",
                              "get_active_tool", "get_selection", "get_prefab_stage", "set_active_tool", "add_tag", "remove_tag", "get_tags", "add_layer", "remove_layer", "get_layers"], "Get and update the Unity Editor state."],
    wait_for_completion: Annotated[bool | str,
                                   "Optional. If True, waits for certain actions (accepts true/false or 'true'/'false')"] | None = None,
    tool_name: Annotated[str,
                         "Tool name when setting active tool"] | None = None,
    tag_name: Annotated[str,
                        "Tag name when adding and removing tags"] | None = None,
    layer_name: Annotated[str,
                          "Layer name when adding and removing layers"] | None = None,
) -> dict[str, Any]:
    ctx.info(f"Processing manage_editor: {action}")

    # Coerce boolean parameters defensively to tolerate 'true'/'false' strings
    def _coerce_bool(value, default=None):
        if value is None:
            return default
        if isinstance(value, bool):
            return value
        if isinstance(value, str):
            v = value.strip().lower()
            if v in ("true", "1", "yes", "on"):  # common truthy strings
                return True
            if v in ("false", "0", "no", "off"):
                return False
        return bool(value)

    wait_for_completion = _coerce_bool(wait_for_completion)

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
            "waitForCompletion": wait_for_completion,
            "toolName": tool_name,  # Corrected parameter name to match C#
            "tagName": tag_name,   # Pass tag name
            "layerName": layer_name,  # Pass layer name
            # Add other parameters based on the action being performed
            # "width": width,
            # "height": height,
            # etc.
        }
        params = {k: v for k, v in params.items() if v is not None}

        # Send command using centralized retry helper
        response = send_command_with_retry("manage_editor", params)

        # Preserve structured failure data; unwrap success into a friendlier shape
        if isinstance(response, dict) and response.get("success"):
            return {"success": True, "message": response.get("message", "Editor operation successful."), "data": response.get("data")}
        return response if isinstance(response, dict) else {"success": False, "message": str(response)}

    except Exception as e:
        return {"success": False, "message": f"Python error managing editor: {str(e)}"}
