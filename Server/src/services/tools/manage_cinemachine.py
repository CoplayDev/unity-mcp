"""
Tool for Unity Cinemachine — virtual cameras, brain, blends.
Actions: list_vcams, get_vcam, set_vcam, get_brain, set_priority, list_blends.
Requires com.unity.cinemachine package.
"""
from typing import Annotated, Any, Literal

from fastmcp import Context
from mcp.types import ToolAnnotations

from services.registry import mcp_for_unity_tool
from services.tools import get_unity_instance_from_context
from transport.unity_transport import send_with_unity_instance
from transport.legacy.unity_connection import async_send_command_with_retry


@mcp_for_unity_tool(
    description=(
        "Unity Cinemachine virtual camera management. "
        "Actions: list_vcams (all CinemachineCamera components), "
        "get_vcam (priority, follow/look-at, body/aim settings), "
        "set_vcam (modify priority, follow target), "
        "get_brain (active camera, blend state, default blend), "
        "set_priority (change camera priority), "
        "list_blends (custom blend definitions). "
        "Requires com.unity.cinemachine package."
    ),
    annotations=ToolAnnotations(title="Manage Cinemachine"),
)
async def manage_cinemachine(
    ctx: Context,
    action: Annotated[
        Literal[
            "list_vcams", "get_vcam", "set_vcam",
            "get_brain", "set_priority", "list_blends"
        ],
        "Action to perform on Unity Cinemachine."
    ],
    target: Annotated[str, "GameObject name or instance ID"] | None = None,
    properties: Annotated[str, "JSON object of properties to set"] | None = None,
    priority: Annotated[int, "Camera priority value"] | None = None,
    page_size: Annotated[int, "Max results to return (default 50)"] | None = None,
    cursor: Annotated[int, "Pagination cursor (0-based offset)"] | None = None,
) -> dict[str, Any]:
    unity_instance = get_unity_instance_from_context(ctx)

    params = {"action": action, "target": target, "properties": properties,
              "priority": priority, "page_size": page_size, "cursor": cursor}
    params = {k: v for k, v in params.items() if v is not None}

    try:
        response = await send_with_unity_instance(
            async_send_command_with_retry, unity_instance, "manage_cinemachine", params)
        if isinstance(response, dict) and response.get("success"):
            return {"success": True, "message": response.get("message", "OK"), "data": response.get("data")}
        return response if isinstance(response, dict) else {"success": False, "message": str(response)}
    except Exception as e:
        return {"success": False, "message": f"Python error managing cinemachine: {str(e)}"}
