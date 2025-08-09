from mcp.server.fastmcp import FastMCP, Context
from typing import Dict, Any
from unity_connection import get_unity_connection

def register_manage_editor_tools(mcp: FastMCP):
    """Register all editor management tools with the MCP server."""

    @mcp.tool()
    def manage_editor(
        ctx: Context,
        action: str,
        wait_for_completion: bool = None,
        # --- Parameters for specific actions ---
        tool_name: str = None, 
        tag_name: str = None,
        layer_name: str = None,
    ) -> Dict[str, Any]:
        """Controls and queries the Unity editor's state and settings.

        Args:
            action: Operation (e.g., 'play', 'pause', 'get_state', 'set_active_tool', 'add_tag').
            wait_for_completion: Optional. If True, waits for certain actions.
            Action-specific arguments (e.g., tool_name, tag_name, layer_name).

        Returns:
            Dictionary with operation results ('success', 'message', 'data').
        """
        try:
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
            
            # Send command to Unity
            response = get_unity_connection().send_command("manage_editor", params)

            # Process response
            if response.get("success"):
                return {"success": True, "message": response.get("message", "Editor operation successful."), "data": response.get("data")}
            else:
                return {"success": False, "message": response.get("error", "An unknown error occurred during editor management.")}

        except Exception as e:
            return {"success": False, "message": f"Python error managing editor: {str(e)}"}