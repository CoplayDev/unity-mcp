"""
Tool for Unity AI Navigation — NavMesh baking, agents, obstacles, path queries.
Actions: list_surfaces, bake, clear, list_agents, get_agent, set_agent_destination,
         list_obstacles, sample_position, calculate_path.
Requires com.unity.ai.navigation package.
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
        "Unity AI Navigation management. "
        "Actions: list_surfaces (all NavMeshSurface components), "
        "bake (bake NavMesh for a surface), "
        "clear (clear baked NavMesh), "
        "list_agents (all NavMeshAgent components), "
        "get_agent (speed, radius, destination, path status), "
        "set_agent_destination (set agent destination — Play mode), "
        "list_obstacles (all NavMeshObstacle components), "
        "sample_position (nearest point on NavMesh), "
        "calculate_path (path between two points). "
        "Requires com.unity.ai.navigation package."
    ),
    annotations=ToolAnnotations(title="Manage Navigation"),
)
async def manage_navigation(
    ctx: Context,
    action: Annotated[
        Literal[
            "list_surfaces", "bake", "clear",
            "list_agents", "get_agent", "set_agent_destination",
            "list_obstacles", "sample_position", "calculate_path"
        ],
        "Action to perform on Unity AI Navigation."
    ],
    # Target
    target: Annotated[str, "GameObject name or instance ID"] | None = None,
    # Position params
    position: Annotated[str, "Position as 'x,y,z'"] | None = None,
    start: Annotated[str, "Start position as 'x,y,z' (for calculate_path)"] | None = None,
    end: Annotated[str, "End position as 'x,y,z' (for calculate_path)"] | None = None,
    max_distance: Annotated[float, "Max sample distance (for sample_position)"] | None = None,
    area_mask: Annotated[int, "NavMesh area mask (default -1 = all)"] | None = None,
    # Pagination
    page_size: Annotated[int, "Max results to return (default 50)"] | None = None,
    cursor: Annotated[int, "Pagination cursor (0-based offset)"] | None = None,
) -> dict[str, Any]:
    unity_instance = get_unity_instance_from_context(ctx)

    params = {
        "action": action,
        "target": target,
        "position": position,
        "start": start,
        "end": end,
        "max_distance": max_distance,
        "area_mask": area_mask,
        "page_size": page_size,
        "cursor": cursor,
    }
    params = {k: v for k, v in params.items() if v is not None}

    try:
        response = await send_with_unity_instance(
            async_send_command_with_retry, unity_instance, "manage_navigation", params
        )

        if isinstance(response, dict) and response.get("success"):
            return {
                "success": True,
                "message": response.get("message", "Navigation operation successful."),
                "data": response.get("data"),
            }
        return response if isinstance(response, dict) else {"success": False, "message": str(response)}

    except Exception as e:
        return {"success": False, "message": f"Python error managing navigation: {str(e)}"}
