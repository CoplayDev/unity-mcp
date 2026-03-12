"""
Tool for Unity 2D physics — raycasting, rigidbodies, colliders, physics settings.
Actions: raycast, raycast_all, overlap_circle, overlap_box,
         list_rigidbodies, get_rigidbody, list_colliders, get_physics2d_settings.
Uses built-in UnityEngine.Physics2D — no package dependency.
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
        "Unity 2D physics operations. "
        "Actions: raycast (2D raycast), "
        "raycast_all (all 2D hits), "
        "overlap_circle (entities in circle), "
        "overlap_box (entities in box), "
        "list_rigidbodies (all Rigidbody2D), "
        "get_rigidbody (body type, mass, velocity, gravity scale), "
        "list_colliders (all Collider2D), "
        "get_physics2d_settings (gravity, collision matrix). "
        "Uses built-in UnityEngine.Physics2D — no package dependency."
    ),
    annotations=ToolAnnotations(title="Manage Physics 2D"),
)
async def manage_physics2d(
    ctx: Context,
    action: Annotated[
        Literal[
            "raycast", "raycast_all", "overlap_circle", "overlap_box",
            "list_rigidbodies", "get_rigidbody", "list_colliders",
            "get_physics2d_settings"
        ],
        "Action to perform on Unity 2D physics."
    ],
    origin: Annotated[str, "Ray origin as 'x,y'"] | None = None,
    direction: Annotated[str, "Ray direction as 'x,y'"] | None = None,
    max_distance: Annotated[float, "Max ray distance (default 100)"] | None = None,
    layer_mask: Annotated[int, "Layer mask for filtering (default -1 = all)"] | None = None,
    center: Annotated[str, "Overlap center as 'x,y'"] | None = None,
    radius: Annotated[float, "Circle radius (for overlap_circle)"] | None = None,
    size: Annotated[str, "Box size as 'x,y' (for overlap_box)"] | None = None,
    angle: Annotated[float, "Box rotation angle (for overlap_box)"] | None = None,
    target: Annotated[str, "GameObject name or instance ID"] | None = None,
    page_size: Annotated[int, "Max results to return (default 50)"] | None = None,
    cursor: Annotated[int, "Pagination cursor (0-based offset)"] | None = None,
) -> dict[str, Any]:
    unity_instance = get_unity_instance_from_context(ctx)

    params = {"action": action, "origin": origin, "direction": direction,
              "max_distance": max_distance, "layer_mask": layer_mask,
              "center": center, "radius": radius, "size": size, "angle": angle,
              "target": target, "page_size": page_size, "cursor": cursor}
    params = {k: v for k, v in params.items() if v is not None}

    try:
        response = await send_with_unity_instance(
            async_send_command_with_retry, unity_instance, "manage_physics2d", params)
        if isinstance(response, dict) and response.get("success"):
            return {"success": True, "message": response.get("message", "OK"), "data": response.get("data")}
        return response if isinstance(response, dict) else {"success": False, "message": str(response)}
    except Exception as e:
        return {"success": False, "message": f"Python error managing physics 2D: {str(e)}"}
