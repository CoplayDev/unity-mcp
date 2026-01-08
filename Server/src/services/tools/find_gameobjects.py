"""
Tool for searching GameObjects in Unity scenes.
Returns only instance IDs with pagination support for efficient searches.
"""
from typing import Annotated, Any, Literal

from fastmcp import Context
from services.registry import mcp_for_unity_tool
from services.tools import get_unity_instance_from_context
from transport.unity_transport import send_with_unity_instance
from transport.legacy.unity_connection import async_send_command_with_retry
from services.tools.utils import coerce_bool, coerce_int
from services.tools.preflight import preflight


@mcp_for_unity_tool(
    description="Search for GameObjects in the scene. Returns instance IDs only (paginated) for efficient lookups. Use mcpforunity://scene/gameobject/{id} resource to get full GameObject data."
)
async def find_gameobjects(
    ctx: Context,
    search_term: Annotated[str, "The value to search for (name, tag, layer name, component type, or path)"],
    search_method: Annotated[
        Literal["by_name", "by_tag", "by_layer",
                "by_component", "by_path", "by_id"],
        "How to search for GameObjects"
    ] = "by_name",
    include_inactive: Annotated[bool | str,
                                "Include inactive GameObjects in search"] | None = None,
    page_size: Annotated[int | str,
                         "Number of results per page (default: 50, max: 500)"] | None = None,
    cursor: Annotated[int | str,
                      "Pagination cursor (offset for next page)"] | None = None,
) -> dict[str, Any]:
    """
    Search for GameObjects and return their instance IDs.

    This is a focused search tool optimized for finding GameObjects efficiently.
    It returns only instance IDs to minimize payload size.

    For detailed GameObject information, use the returned IDs with:
    - mcpforunity://scene/gameobject/{id} - Get full GameObject data
    - mcpforunity://scene/gameobject/{id}/components - Get all components
    - mcpforunity://scene/gameobject/{id}/component/{name} - Get specific component
    """
    unity_instance = get_unity_instance_from_context(ctx)

    # Validate required parameters before preflight I/O
    if not search_term:
        return {
            "success": False,
            "message": "Missing required parameter 'search_term'. Specify what to search for."
        }

    gate = await preflight(ctx, wait_for_no_compile=True, refresh_if_dirty=True)
    if gate is not None:
        return gate.model_dump()

    # Coerce parameters
    include_inactive = coerce_bool(include_inactive, default=False)
    page_size = coerce_int(page_size, default=50)
    cursor = coerce_int(cursor, default=0)

    try:
        params = {
            "searchMethod": search_method,
            "searchTerm": search_term,
            "includeInactive": include_inactive,
            "pageSize": page_size,
            "cursor": cursor,
        }
        params = {k: v for k, v in params.items() if v is not None}

        response = await send_with_unity_instance(
            async_send_command_with_retry,
            unity_instance,
            "find_gameobjects",
            params,
        )

        if isinstance(response, dict) and response.get("success"):
            return {
                "success": True,
                "message": response.get("message", "Search completed."),
                "data": response.get("data")
            }
        return response if isinstance(response, dict) else {"success": False, "message": str(response)}

    except Exception as e:
        return {"success": False, "message": f"Error searching GameObjects: {e!s}"}
