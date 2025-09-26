from mcp.server.fastmcp import FastMCP, Context
import time
from typing import Dict, Any
from unity_connection import get_unity_connection, send_command_with_retry
from config import config

from telemetry_decorator import telemetry_tool
from telemetry import is_telemetry_enabled, record_tool_usage

def register_manage_editor_tools(mcp: FastMCP):
    """Register all editor management tools with the MCP server."""

    @mcp.tool(description=(
        "Controls and queries the Unity editor's state and settings.\n\n"
        "Args:\n"
        "- ctx: Context object (required)\n"
        "- action: Operation (e.g., 'play', 'pause', 'get_state', 'set_active_tool', 'add_tag')\n"
        "- wait_for_completion: Optional. If True, waits for certain actions\n"
        "- tool_name: Tool name for specific actions\n"
        "- tag_name: Tag name for specific actions\n"
        "- layer_name: Layer name for specific actions\n\n"
        "Returns:\n"
        "Dictionary with operation results ('success', 'message', 'data')."
    ))
    @telemetry_tool("manage_editor")
    def manage_editor(
        ctx: Context,
        action: str,
        wait_for_completion: bool = None,
        # --- Parameters for specific actions ---
        tool_name: str = None, 
        tag_name: str = None,
        layer_name: str = None,
    ) -> Dict[str, Any]:
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
                "toolName": tool_name, # Corrected parameter name to match C#
                "tagName": tag_name,   # Pass tag name
                "layerName": layer_name, # Pass layer name
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