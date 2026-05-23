"""
Tool for Unity DOTS SubScene management — listing, loading, unloading, status.
Actions: list_subscenes, load_subscene, unload_subscene, get_subscene_status, list_sections.
Requires com.unity.entities package in the Unity project.
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
        "Manage Unity DOTS SubScenes at runtime. "
        "Actions: list_subscenes (find all SubScene components in hierarchy), "
        "load_subscene (request async scene loading), "
        "unload_subscene (unload scene and destroy meta entities), "
        "get_subscene_status (streaming state, section counts, asset path), "
        "list_sections (inspect individual scene sections and their load state). "
        "Requires com.unity.entities package. Works best during Play mode."
    ),
    annotations=ToolAnnotations(
        title="Manage DOTS SubScene",
    ),
)
async def manage_dots_subscene(
    ctx: Context,
    action: Annotated[
        Literal[
            "list_subscenes", "load_subscene", "unload_subscene",
            "get_subscene_status", "list_sections"
        ],
        "Action to perform on DOTS SubScenes."
    ],
    # SubScene identification
    scene_name: Annotated[str, "SubScene name or GameObject name (for load/unload/status/sections)"] | None = None,
) -> dict[str, Any]:
    unity_instance = await get_unity_instance_from_context(ctx)

    params = {
        "action": action,
        "scene_name": scene_name,
    }
    params = {k: v for k, v in params.items() if v is not None}

    try:
        response = await send_with_unity_instance(
            async_send_command_with_retry, unity_instance, "manage_dots_subscene", params
        )

        if isinstance(response, dict) and response.get("success"):
            return {
                "success": True,
                "message": response.get("message", "SubScene operation successful."),
                "data": response.get("data"),
            }
        return response if isinstance(response, dict) else {"success": False, "message": str(response)}

    except Exception as e:
        return {"success": False, "message": f"Python error managing DOTS SubScene: {str(e)}"}
