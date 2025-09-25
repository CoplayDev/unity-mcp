from mcp.server.fastmcp import FastMCP, Context
from typing import Dict, Any
from unity_connection import get_unity_connection, send_command_with_retry
from config import config
import time
import os
import base64

from telemetry_decorator import telemetry_tool

def register_manage_shader_tools(mcp: FastMCP):
    """Register all shader script management tools with the MCP server."""

    @mcp.tool()
    @telemetry_tool("manage_shader")
    def manage_shader(
        ctx: Any,
        action: str,
        name: str,
        path: str,
        contents: str,
    ) -> Dict[str, Any]:
        """Manages shader scripts in Unity (create, read, update, delete).

        Args:
            action: Operation ('create', 'read', 'update', 'delete').
            name: Shader name (no .cs extension).
            path: Asset path (default: "Assets/").
            contents: Shader code for 'create'/'update'.

        Returns:
            Dictionary with results ('success', 'message', 'data').
        """
        try:
            # Prepare parameters for Unity
            params = {
                "action": action,
                "name": name,
                "path": path,
            }
            
            # Base64 encode the contents if they exist to avoid JSON escaping issues
            if contents is not None:
                if action in ['create', 'update']:
                    # Encode content for safer transmission
                    params["encodedContents"] = base64.b64encode(contents.encode('utf-8')).decode('utf-8')
                    params["contentsEncoded"] = True
                else:
                    params["contents"] = contents
            
            # Remove None values so they don't get sent as null
            params = {k: v for k, v in params.items() if v is not None}

            # Send command via centralized retry helper
            response = send_command_with_retry("manage_shader", params)
            
            # Process response from Unity
            if isinstance(response, dict) and response.get("success"):
                # If the response contains base64 encoded content, decode it
                if response.get("data", {}).get("contentsEncoded"):
                    decoded_contents = base64.b64decode(response["data"]["encodedContents"]).decode('utf-8')
                    response["data"]["contents"] = decoded_contents
                    del response["data"]["encodedContents"]
                    del response["data"]["contentsEncoded"]
                
                return {"success": True, "message": response.get("message", "Operation successful."), "data": response.get("data")}
            return response if isinstance(response, dict) else {"success": False, "message": str(response)}

        except Exception as e:
            # Handle Python-side errors (e.g., connection issues)
            return {"success": False, "message": f"Python error managing shader: {str(e)}"}