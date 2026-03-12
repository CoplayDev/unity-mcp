"""
Tool for classic Unity 3D physics — raycasting, rigidbodies, colliders, joints, physics settings.
Actions: raycast, raycast_all, overlap_sphere, overlap_box, list_rigidbodies, get_rigidbody,
         set_rigidbody, list_colliders, get_physics_settings, set_physics_settings.
Uses built-in UnityEngine.Physics — no package dependency.
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
        "Classic Unity 3D physics operations. "
        "Actions: raycast (cast ray, get first hit), "
        "raycast_all (all hits along ray), "
        "overlap_sphere (entities in sphere), "
        "overlap_box (entities in box), "
        "list_rigidbodies (all Rigidbody components), "
        "get_rigidbody (full rigidbody detail by name/ID), "
        "set_rigidbody (modify mass, drag, constraints), "
        "list_colliders (all colliders with type info), "
        "get_physics_settings (gravity, layer matrix, defaults), "
        "set_physics_settings (modify gravity, sleep thresholds). "
        "Uses built-in UnityEngine.Physics — no package dependency."
    ),
    annotations=ToolAnnotations(
        title="Manage Physics",
    ),
)
async def manage_physics(
    ctx: Context,
    action: Annotated[
        Literal[
            "raycast", "raycast_all", "overlap_sphere", "overlap_box",
            "list_rigidbodies", "get_rigidbody", "set_rigidbody",
            "list_colliders", "get_physics_settings", "set_physics_settings"
        ],
        "Action to perform on Unity 3D physics."
    ],
    # Raycast params
    origin: Annotated[str, "Ray origin as 'x,y,z'"] | None = None,
    direction: Annotated[str, "Ray direction as 'x,y,z'"] | None = None,
    max_distance: Annotated[float, "Max ray distance (default 100)"] | None = None,
    layer_mask: Annotated[int, "Layer mask for filtering (default -1 = all)"] | None = None,
    # Overlap params
    center: Annotated[str, "Overlap center as 'x,y,z'"] | None = None,
    radius: Annotated[float, "Sphere radius (for overlap_sphere)"] | None = None,
    half_extents: Annotated[str, "Box half extents as 'x,y,z' (for overlap_box)"] | None = None,
    # Target params
    target: Annotated[str, "GameObject name or instance ID"] | None = None,
    # Properties for set actions
    properties: Annotated[str, "JSON object of properties to set"] | None = None,
    # Pagination
    page_size: Annotated[int, "Max results to return (default 50)"] | None = None,
    cursor: Annotated[int, "Pagination cursor (0-based offset)"] | None = None,
) -> dict[str, Any]:
    unity_instance = get_unity_instance_from_context(ctx)

    params = {
        "action": action,
        "origin": origin,
        "direction": direction,
        "max_distance": max_distance,
        "layer_mask": layer_mask,
        "center": center,
        "radius": radius,
        "half_extents": half_extents,
        "target": target,
        "properties": properties,
        "page_size": page_size,
        "cursor": cursor,
    }
    params = {k: v for k, v in params.items() if v is not None}

    try:
        response = await send_with_unity_instance(
            async_send_command_with_retry, unity_instance, "manage_physics", params
        )

        if isinstance(response, dict) and response.get("success"):
            return {
                "success": True,
                "message": response.get("message", "Physics operation successful."),
                "data": response.get("data"),
            }
        return response if isinstance(response, dict) else {"success": False, "message": str(response)}

    except Exception as e:
        return {"success": False, "message": f"Python error managing physics: {str(e)}"}
