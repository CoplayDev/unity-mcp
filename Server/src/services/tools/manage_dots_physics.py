"""
Tool for Unity DOTS Physics debugging — raycasting, overlap queries, body inspection.
Actions: get_physics_world, raycast, overlap_aabb, list_colliders, get_body.
Requires com.unity.physics package in the Unity project.
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
        "Debug Unity DOTS Physics at runtime. "
        "Actions: get_physics_world (body/joint counts), "
        "raycast (cast ray, get hit entities with position/normal), "
        "overlap_aabb (find bodies in axis-aligned bounding box), "
        "list_colliders (list entities with PhysicsCollider), "
        "get_body (inspect physics body — position, velocity, collider type). "
        "Requires com.unity.physics package. Works best during Play mode."
    ),
    annotations=ToolAnnotations(
        title="Manage DOTS Physics",
    ),
)
async def manage_dots_physics(
    ctx: Context,
    action: Annotated[
        Literal[
            "get_physics_world", "raycast", "overlap_aabb",
            "list_colliders", "get_body"
        ],
        "Action to perform on DOTS Physics data."
    ],
    # Raycast params
    origin: Annotated[str, "Ray origin as 'x,y,z' (for raycast)"] | None = None,
    direction: Annotated[str, "Ray direction as 'x,y,z' (for raycast)"] | None = None,
    max_distance: Annotated[float, "Max ray distance (default 100)"] | None = None,
    # AABB params
    min: Annotated[str, "AABB min corner as 'x,y,z' (for overlap_aabb)"] | None = None,
    max: Annotated[str, "AABB max corner as 'x,y,z' (for overlap_aabb)"] | None = None,
    # Body params
    body_index: Annotated[int, "Physics body index (for get_body)"] | None = None,
    # Common
    world: Annotated[str, "Target world name"] | None = None,
    page_size: Annotated[int, "Max results to return (default 20)"] | None = None,
) -> dict[str, Any]:
    unity_instance = get_unity_instance_from_context(ctx)

    params = {
        "action": action,
        "origin": origin,
        "direction": direction,
        "max_distance": max_distance,
        "min": min,
        "max": max,
        "body_index": body_index,
        "world": world,
        "page_size": page_size,
    }
    params = {k: v for k, v in params.items() if v is not None}

    try:
        response = await send_with_unity_instance(
            async_send_command_with_retry, unity_instance, "manage_dots_physics", params
        )

        if isinstance(response, dict) and response.get("success"):
            return {
                "success": True,
                "message": response.get("message", "Physics operation successful."),
                "data": response.get("data"),
            }
        return response if isinstance(response, dict) else {"success": False, "message": str(response)}

    except Exception as e:
        return {"success": False, "message": f"Python error managing DOTS Physics: {str(e)}"}
