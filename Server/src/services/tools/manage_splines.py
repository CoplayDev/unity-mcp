"""
Tool for Unity Splines — inspect, add/remove knots, evaluate positions.
Actions: list_splines, get_spline, get_knot, add_knot, remove_knot, set_knot, evaluate.
Requires com.unity.splines package.
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
        "Unity Splines operations. "
        "Actions: list_splines (all SplineContainer components), "
        "get_spline (knot count, length, closed state), "
        "get_knot (knot position, rotation, tangents), "
        "add_knot (add knot at position), "
        "remove_knot (remove knot by index), "
        "set_knot (modify knot position/tangents), "
        "evaluate (position/tangent/up at t 0-1). "
        "Requires com.unity.splines package."
    ),
    annotations=ToolAnnotations(title="Manage Splines"),
)
async def manage_splines(
    ctx: Context,
    action: Annotated[
        Literal[
            "list_splines", "get_spline", "get_knot", "add_knot",
            "remove_knot", "set_knot", "evaluate"
        ],
        "Action to perform on Unity Splines."
    ],
    target: Annotated[str, "GameObject name or instance ID with SplineContainer"] | None = None,
    spline_index: Annotated[int, "Index of spline in container (default 0)"] | None = None,
    knot_index: Annotated[int, "Knot index within spline"] | None = None,
    position: Annotated[str, "Knot position as 'x,y,z'"] | None = None,
    rotation: Annotated[str, "Knot rotation as 'x,y,z,w' quaternion"] | None = None,
    t: Annotated[float, "Normalized position along spline (0-1) for evaluate"] | None = None,
    page_size: Annotated[int, "Max results to return (default 50)"] | None = None,
    cursor: Annotated[int, "Pagination cursor (0-based offset)"] | None = None,
) -> dict[str, Any]:
    unity_instance = get_unity_instance_from_context(ctx)

    params = {"action": action, "target": target, "spline_index": spline_index,
              "knot_index": knot_index, "position": position, "rotation": rotation,
              "t": t, "page_size": page_size, "cursor": cursor}
    params = {k: v for k, v in params.items() if v is not None}

    try:
        response = await send_with_unity_instance(
            async_send_command_with_retry, unity_instance, "manage_splines", params)
        if isinstance(response, dict) and response.get("success"):
            return {"success": True, "message": response.get("message", "OK"), "data": response.get("data")}
        return response if isinstance(response, dict) else {"success": False, "message": str(response)}
    except Exception as e:
        return {"success": False, "message": f"Python error managing splines: {str(e)}"}
