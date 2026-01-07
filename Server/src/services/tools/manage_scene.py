from typing import Annotated, Literal, Any

from fastmcp import Context
from mcp.types import ToolAnnotations

from services.registry import mcp_for_unity_tool
from services.tools import get_unity_instance_from_context
from services.tools.utils import coerce_int, coerce_bool
from transport.unity_transport import send_with_unity_instance
from transport.legacy.unity_connection import async_send_command_with_retry
from services.tools.preflight import preflight


@mcp_for_unity_tool(
    description="Performs CRUD operations on Unity scenes. Read-only actions: get_hierarchy, get_active, get_build_settings, screenshot. Modifying actions: create, load, save.",
    annotations=ToolAnnotations(
        title="Manage Scene",
        destructiveHint=True,
    ),
)
async def manage_scene(
    ctx: Context,
    action: Annotated[Literal[
        "create",
        "load",
        "save",
        "get_hierarchy",
        "get_active",
        "get_build_settings",
        "screenshot",
    ], "Perform CRUD operations on Unity scenes, and capture a screenshot."],
    name: Annotated[str, "Scene name."] | None = None,
    path: Annotated[str, "Scene path."] | None = None,
    build_index: Annotated[int | str,
                           "Unity build index (quote as string, e.g., '0')."] | None = None,
    screenshot_file_name: Annotated[str,
                                    "Screenshot file name (optional). Defaults to timestamp when omitted."] | None = None,
    screenshot_super_size: Annotated[int | str,
                                     "Screenshot supersize multiplier (integer ≥1). Optional."] | None = None,
    # --- get_hierarchy paging/safety ---
    parent: Annotated[str | int,
                      "Optional parent GameObject reference (name/path/instanceID) to list direct children."] | None = None,
    page_size: Annotated[int | str,
                         "Page size for get_hierarchy paging."] | None = None,
    cursor: Annotated[int | str,
                      "Opaque cursor for paging (offset)."] | None = None,
    max_nodes: Annotated[int | str,
                         "Hard cap on returned nodes per request (safety)."] | None = None,
    max_depth: Annotated[int | str,
                         "Accepted for forward-compatibility; current paging returns a single level."] | None = None,
    max_children_per_node: Annotated[int | str,
                                     "Child paging hint (safety)."] | None = None,
    include_transform: Annotated[bool | str,
                                 "If true, include local transform in node summaries."] | None = None,
) -> dict[str, Any]:
    # Get active instance from session state
    # Removed session_state import
    unity_instance = get_unity_instance_from_context(ctx)
    gate = await preflight(ctx, wait_for_no_compile=True, refresh_if_dirty=True)
    if gate is not None:
        return gate.model_dump()
    try:
        coerced_build_index = coerce_int(build_index, default=None)
        coerced_super_size = coerce_int(screenshot_super_size, default=None)
        coerced_page_size = coerce_int(page_size, default=None)
        coerced_cursor = coerce_int(cursor, default=None)
        coerced_max_nodes = coerce_int(max_nodes, default=None)
        coerced_max_depth = coerce_int(max_depth, default=None)
        coerced_max_children_per_node = coerce_int(
            max_children_per_node, default=None)
        coerced_include_transform = coerce_bool(
            include_transform, default=None)

        params: dict[str, Any] = {"action": action}
        if name:
            params["name"] = name
        if path:
            params["path"] = path
        if coerced_build_index is not None:
            params["buildIndex"] = coerced_build_index
        if screenshot_file_name:
            params["fileName"] = screenshot_file_name
        if coerced_super_size is not None:
            params["superSize"] = coerced_super_size

        # get_hierarchy paging/safety params (optional)
        if parent is not None:
            params["parent"] = parent
        if coerced_page_size is not None:
            params["pageSize"] = coerced_page_size
        if coerced_cursor is not None:
            params["cursor"] = coerced_cursor
        if coerced_max_nodes is not None:
            params["maxNodes"] = coerced_max_nodes
        if coerced_max_depth is not None:
            params["maxDepth"] = coerced_max_depth
        if coerced_max_children_per_node is not None:
            params["maxChildrenPerNode"] = coerced_max_children_per_node
        if coerced_include_transform is not None:
            params["includeTransform"] = coerced_include_transform

        # Use centralized retry helper with instance routing
        response = await send_with_unity_instance(async_send_command_with_retry, unity_instance, "manage_scene", params)

        # Preserve structured failure data; unwrap success into a friendlier shape
        if isinstance(response, dict) and response.get("success"):
            return {"success": True, "message": response.get("message", "Scene operation successful."), "data": response.get("data")}
        return response if isinstance(response, dict) else {"success": False, "message": str(response)}

    except Exception as e:
        return {"success": False, "message": f"Python error managing scene: {str(e)}"}
