"""
Tool for Unity DOTS ECS debugging, entity inspection, and performance monitoring.
Actions: list_worlds, query_entities, get_entity, list_systems, get_system,
         performance_snapshot, toggle_system, list_component_types,
         create_entity, destroy_entity, set_component, add_component,
         remove_component, query_count, inspect_bdp_tree.
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
        "get_entity (inspect entity components — use component_types to filter specific components), "
        "list_systems (list systems with enabled status), get_system (system details, queries, ordering), "
        "performance_snapshot (chunk utilization, archetype stats, entity counts), "
        "toggle_system (enable/disable a system for debugging), "
        "list_component_types (discover all registered ECS types with optional filter), "
        "create_entity (create debug entity with components), "
        "destroy_entity (destroy entity by index/version), "
        "set_component (modify a component field value at runtime), "
        "add_component (add a component to an existing entity), "
        "remove_component (remove a component from an entity), "
        "query_count (fast entity count without fetching data), "
        "inspect_bdp_tree (show BDP behavior tree state — active branch, running task, task statuses). "
        "Requires com.unity.entities package. Most actions work in Edit mode; "
        "performance_snapshot and inspect_bdp_tree are most useful during Play mode."
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
            "performance_snapshot", "toggle_system",
            "list_component_types", "create_entity", "destroy_entity",
            "set_component", "add_component", "remove_component", "query_count",
            "inspect_bdp_tree"
        ],
        "Action to perform on DOTS ECS data."
    ],
    # Entity query params
    component_types: Annotated[
        str,
        "Comma-separated component type names for query_entities/create_entity/get_entity filter (e.g. 'Health,NavigationTarget')"
    ] | None = None,
    # Entity get/destroy params
    entity_index: Annotated[int, "Entity index for get_entity/destroy_entity"] | None = None,
    entity_version: Annotated[int, "Entity version for get_entity/destroy_entity (default 1)"] | None = None,
    # System params
    system_name: Annotated[
        str,
        "System name (short or full) for get_system/toggle_system"
    ] | None = None,
    enabled: Annotated[
        bool | str,
        "Enable/disable for toggle_system (true/false)"
    ] | None = None,
    # Component manipulation params
    component_name: Annotated[
        str,
        "Component type name for set_component/add_component/remove_component"
    ] | None = None,
    field_name: Annotated[str, "Field name to modify (for set_component)"] | None = None,
    field_value: Annotated[str, "New field value as string (for set_component)"] | None = None,
    # Filtering
    world: Annotated[
        str,
        "Target world name (defaults to DefaultGameObjectInjectionWorld)"
    ] | None = None,
    group: Annotated[str, "Filter systems by group name (for list_systems)"] | None = None,
    filter: Annotated[str, "Name filter for list_component_types"] | None = None,
    category: Annotated[str, "Category filter for list_component_types (e.g. 'BufferData', 'ComponentData')"] | None = None,
    # Paging
    page_size: Annotated[int, "Max entities/types to return (default 20, max 200)"] | None = None,
    limit: Annotated[int, "Max archetypes in performance_snapshot (default 20)"] | None = None,
) -> dict[str, Any]:
    unity_instance = get_unity_instance_from_context(ctx)

    params = {
        "action": action,
        "component_types": component_types,
        "entity_index": entity_index,
        "entity_version": entity_version,
        "component_name": component_name,
        "field_name": field_name,
        "field_value": field_value,
        "system_name": system_name,
        "enabled": enabled,
        "world": world,
        "group": group,
        "filter": filter,
        "category": category,
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
