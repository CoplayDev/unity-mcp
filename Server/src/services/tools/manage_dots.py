"""
Tool for Unity DOTS ECS debugging, entity inspection, and performance monitoring.
Actions: list_worlds, query_entities, get_entity, list_systems, get_system,
         performance_snapshot, toggle_system.
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
        "Debug and monitor Unity DOTS ECS at runtime. "
        "Actions: list_worlds (show all ECS Worlds), query_entities (find entities by component types), "
        "get_entity (inspect single entity's components and field values), "
        "list_systems (list systems with enabled status), get_system (system details and queries), "
        "performance_snapshot (chunk utilization, archetype stats, entity counts), "
        "toggle_system (enable/disable a system for debugging). "
        "Requires com.unity.entities package. Most actions work in Edit mode; "
        "performance_snapshot is most useful during Play mode."
    ),
    annotations=ToolAnnotations(
        title="Manage DOTS",
    ),
)
async def manage_dots(
    ctx: Context,
    action: Annotated[
        Literal[
            "list_worlds", "query_entities", "get_entity",
            "list_systems", "get_system",
            "performance_snapshot", "toggle_system"
        ],
        "Action to perform on DOTS ECS data."
    ],
    # Entity query params
    component_types: Annotated[
        str,
        "Comma-separated component type names for query_entities (e.g. 'LocalTransform,Velocity')"
    ] | None = None,
    # Entity get params
    entity_index: Annotated[int, "Entity index for get_entity"] | None = None,
    entity_version: Annotated[int, "Entity version for get_entity (default 1)"] | None = None,
    # System params
    system_name: Annotated[
        str,
        "System name (short or full) for get_system/toggle_system"
    ] | None = None,
    enabled: Annotated[
        bool | str,
        "Enable/disable for toggle_system (true/false)"
    ] | None = None,
    # Filtering
    world: Annotated[
        str,
        "Target world name (defaults to DefaultGameObjectInjectionWorld)"
    ] | None = None,
    group: Annotated[str, "Filter systems by group name (for list_systems)"] | None = None,
    # Paging
    page_size: Annotated[int, "Max entities/archetypes to return (default 20, max 100)"] | None = None,
    limit: Annotated[int, "Max archetypes in performance_snapshot (default 20)"] | None = None,
) -> dict[str, Any]:
    unity_instance = get_unity_instance_from_context(ctx)

    params = {
        "action": action,
        "component_types": component_types,
        "entity_index": entity_index,
        "entity_version": entity_version,
        "system_name": system_name,
        "enabled": enabled,
        "world": world,
        "group": group,
        "page_size": page_size,
        "limit": limit,
    }
    params = {k: v for k, v in params.items() if v is not None}

    try:
        response = await send_with_unity_instance(
            async_send_command_with_retry, unity_instance, "manage_dots", params
        )

        if isinstance(response, dict) and response.get("success"):
            return {
                "success": True,
                "message": response.get("message", "DOTS operation successful."),
                "data": response.get("data"),
            }
        return response if isinstance(response, dict) else {"success": False, "message": str(response)}

    except Exception as e:
        return {"success": False, "message": f"Python error managing DOTS: {str(e)}"}
