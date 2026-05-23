"""
Tool for Unity DOTS Entities Graphics debugging — render stats, materials, meshes.
Actions: get_render_stats, list_rendered_entities, get_entity_rendering,
         list_registered_materials, list_registered_meshes.
Requires com.unity.entities.graphics package in the Unity project.
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
        "Debug Unity DOTS Entities Graphics rendering at runtime. "
        "Actions: get_render_stats (count rendered entities, LOD groups), "
        "list_rendered_entities (list entities with MaterialMeshInfo), "
        "get_entity_rendering (inspect render bounds, material, mesh, filter settings), "
        "list_registered_materials (unique materials from RenderMeshArrays), "
        "list_registered_meshes (unique meshes with vertex counts). "
        "Requires com.unity.entities.graphics package. Works best during Play mode."
    ),
    annotations=ToolAnnotations(
        title="Manage DOTS Graphics",
    ),
)
async def manage_dots_graphics(
    ctx: Context,
    action: Annotated[
        Literal[
            "get_render_stats", "list_rendered_entities", "get_entity_rendering",
            "list_registered_materials", "list_registered_meshes"
        ],
        "Action to perform on DOTS Graphics data."
    ],
    # Entity rendering detail
    entity_index: Annotated[int, "Entity index (for get_entity_rendering)"] | None = None,
    # Common
    world: Annotated[str, "Target world name"] | None = None,
    page_size: Annotated[int, "Max results to return (default 20)"] | None = None,
) -> dict[str, Any]:
    unity_instance = await get_unity_instance_from_context(ctx)

    params = {
        "action": action,
        "entity_index": entity_index,
        "world": world,
        "page_size": page_size,
    }
    params = {k: v for k, v in params.items() if v is not None}

    try:
        response = await send_with_unity_instance(
            async_send_command_with_retry, unity_instance, "manage_dots_graphics", params
        )

        if isinstance(response, dict) and response.get("success"):
            return {
                "success": True,
                "message": response.get("message", "Graphics operation successful."),
                "data": response.get("data"),
            }
        return response if isinstance(response, dict) else {"success": False, "message": str(response)}

    except Exception as e:
        return {"success": False, "message": f"Python error managing DOTS Graphics: {str(e)}"}
