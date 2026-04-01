"""
Tool for searching missing references and missing scripts in Unity scenes and assets.
"""
from typing import Annotated, Any, Literal

from fastmcp import Context
from pydantic import Field
from services.registry import mcp_for_unity_tool
from services.tools import get_unity_instance_from_context
from transport.unity_transport import send_with_unity_instance
from transport.legacy.unity_connection import async_send_command_with_retry
from services.tools.utils import coerce_bool, coerce_int
from services.tools.preflight import preflight


@mcp_for_unity_tool(
    description="Search for missing references, missing scripts, and broken prefab links in the active scene or across project assets. Returns detailed per-property issue reports with pagination. Use auto_repair to safely remove missing scripts with undo support."
)
async def search_missing_references(
    ctx: Context,
    scope: Annotated[
        Literal["scene", "project"],
        Field(
            default="scene",
            description="Scan scope: the active scene hierarchy or project assets."
        ),
    ] = "scene",
    include_missing_scripts: Annotated[
        bool | str | None,
        Field(
            default=None,
            description="Check for MonoBehaviours with missing scripts."
        ),
    ] = None,
    include_broken_references: Annotated[
        bool | str | None,
        Field(
            default=None,
            description="Check for broken object references in serialized properties."
        ),
    ] = None,
    include_broken_prefabs: Annotated[
        bool | str | None,
        Field(
            default=None,
            description="Check for prefab instances with missing prefab assets."
        ),
    ] = None,
    path_filter: Annotated[
        str | None,
        Field(
            default=None,
            description="Only scan assets under this path when scope='project'."
        ),
    ] = None,
    component_filter: Annotated[
        str | None,
        Field(
            default=None,
            description="Only report issues for this component type."
        ),
    ] = None,
    auto_repair: Annotated[
        bool | str | None,
        Field(
            default=None,
            description="Remove missing scripts with Undo support."
        ),
    ] = None,
    page_size: Annotated[
        int | str | None,
        Field(
            default=None,
            description="Number of issue results per page (default: 100, max: 500)."
        ),
    ] = None,
    cursor: Annotated[
        int | str | None,
        Field(
            default=None,
            description="Pagination cursor offset."
        ),
    ] = None,
) -> dict[str, Any]:
    """
    Search for missing references, missing scripts, and broken prefab links.

    Scans the active scene hierarchy or project assets (prefabs, ScriptableObjects,
    materials) for broken object references at the serialized property level.

    Returns paginated, per-property issue reports. Use auto_repair to safely
    remove missing scripts with undo support.
    """
    unity_instance = await get_unity_instance_from_context(ctx)

    coerced_include_missing_scripts = coerce_bool(include_missing_scripts, default=True)
    coerced_include_broken_references = coerce_bool(include_broken_references, default=True)
    coerced_include_broken_prefabs = coerce_bool(include_broken_prefabs, default=True)
    coerced_auto_repair = coerce_bool(auto_repair, default=None)
    coerced_page_size = coerce_int(page_size, default=100)
    coerced_cursor = coerce_int(cursor, default=0)

    if coerced_page_size < 1:
        coerced_page_size = 1
    if coerced_page_size > 500:
        coerced_page_size = 500
    if coerced_cursor < 0:
        coerced_cursor = 0

    gate = await preflight(ctx, wait_for_no_compile=True, refresh_if_dirty=True)
    if gate is not None:
        return gate.model_dump()

    try:
        params = {
            "scope": scope,
            "includeMissingScripts": coerced_include_missing_scripts,
            "includeBrokenReferences": coerced_include_broken_references,
            "includeBrokenPrefabs": coerced_include_broken_prefabs,
            "pathFilter": path_filter if scope == "project" else None,
            "componentFilter": component_filter,
            "autoRepair": coerced_auto_repair,
            "pageSize": coerced_page_size,
            "cursor": coerced_cursor,
        }
        params = {k: v for k, v in params.items() if v is not None}

        response = await send_with_unity_instance(
            async_send_command_with_retry,
            unity_instance,
            "search_missing_references",
            params,
        )

        if isinstance(response, dict) and response.get("success"):
            return {
                "success": True,
                "message": response.get("message", "Missing reference search completed."),
                "data": response.get("data"),
            }
        return response if isinstance(response, dict) else {"success": False, "message": str(response)}

    except Exception as e:
        return {"success": False, "message": f"Error searching missing references: {e!s}"}
