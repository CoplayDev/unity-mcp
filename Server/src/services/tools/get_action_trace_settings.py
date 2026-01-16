"""
Defines the get_action_trace_settings tool for retrieving ActionTrace system configuration.

This tool provides access to the current ActionTrace settings, allowing AI agents to:
- Check recording state and capacity
- View event filtering configuration
- Understand semantic analysis settings
- Monitor system health and statistics

Unity implementation: MCPForUnity/Editor/Tools/ActionTraceSettingsTool.cs
"""
from typing import Annotated, Any

from fastmcp import Context
from mcp.types import ToolAnnotations

from services.registry import mcp_for_unity_tool
from services.tools import get_unity_instance_from_context
from transport.unity_transport import send_with_unity_instance
from transport.legacy.unity_connection import async_send_command_with_retry


@mcp_for_unity_tool(
    description="Get ActionTrace system settings and configuration. Shows recording state, capacity, filtering rules, and other configuration details.",
    annotations=ToolAnnotations(
        title="Get Action Trace Settings",
    ),
)
async def get_action_trace_settings(
    ctx: Context,
) -> dict[str, Any]:
    # Get active instance from request state
    unity_instance = get_unity_instance_from_context(ctx)

    try:
        # Send command to Unity (no parameters needed)
        response = await send_with_unity_instance(
            async_send_command_with_retry,
            unity_instance,
            "get_action_trace_settings",
            {},
        )

        # Preserve structured failure data; unwrap success into a friendlier shape
        if isinstance(response, dict) and response.get("success"):
            return {
                "success": True,
                "message": response.get("message", "Retrieved ActionTrace settings."),
                "data": response.get("data")
            }
        return response if isinstance(response, dict) else {"success": False, "message": str(response)}

    except Exception as e:
        return {"success": False, "message": f"Python error getting action trace settings: {str(e)}"}
