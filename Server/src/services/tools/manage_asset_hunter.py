"""
MCP tool for Asset Hunter Pro — unused assets, duplicates, dependencies,
build reports, and exclusion settings.
Requires the HeurekaGames Asset Hunter PRO package in the Unity project.
"""
from typing import Annotated, Any, Literal

from fastmcp import Context
from mcp.types import ToolAnnotations

from services.registry import mcp_for_unity_tool
from services.tools import get_unity_instance_from_context
from services.tools.utils import coerce_int
from transport.unity_transport import send_with_unity_instance
from transport.legacy.unity_connection import async_send_command_with_retry


@mcp_for_unity_tool(
    description=(
        "Interact with Asset Hunter Pro to analyze project assets. "
        "Actions: 'scan_unused' (find unused assets from latest build report), "
        "'get_duplicates' (find duplicate assets by content hash), "
        "'get_dependencies' (query asset references or reverse references), "
        "'get_build_report' (summary of latest build report), "
        "'get_settings' (current exclusion/ignore settings). "
        "Requires HeurekaGames Asset Hunter PRO package. "
        "Build report actions require a prior Unity build with AHP enabled."
    ),
    annotations=ToolAnnotations(
        title="Manage Asset Hunter",
    ),
)
async def manage_asset_hunter(
    ctx: Context,
    action: Annotated[
        Literal["scan_unused", "get_duplicates", "get_dependencies", "get_build_report", "get_settings"],
        "Action to perform."
    ],
    asset_path: Annotated[
        str,
        "Asset path for 'get_dependencies' (e.g. 'Assets/Sprites/icon.png')."
    ] | None = None,
    direction: Annotated[
        Literal["references", "referenced_by"],
        "For 'get_dependencies': 'references' = what this asset uses, "
        "'referenced_by' = what uses this asset. Default: 'references'."
    ] | None = None,
    filter_type: Annotated[
        str,
        "For 'scan_unused': filter by asset type name (e.g. 'Texture2D', 'Material')."
    ] | None = None,
    page_size: Annotated[
        int | str,
        "Items per page (default 50, max 500)."
    ] | None = None,
    cursor: Annotated[
        int | str,
        "Paging cursor (0-based offset). Use nextCursor from previous response."
    ] | None = None,
) -> dict[str, Any]:
    unity_instance = await get_unity_instance_from_context(ctx)

    params = {
        "action": action,
        "asset_path": asset_path,
        "direction": direction,
        "filter_type": filter_type,
        "page_size": coerce_int(page_size, default=None),
        "cursor": coerce_int(cursor, default=None),
    }
    params = {k: v for k, v in params.items() if v is not None}

    try:
        response = await send_with_unity_instance(
            async_send_command_with_retry, unity_instance,
            "manage_asset_hunter", params
        )
        if isinstance(response, dict):
            return response
        return {"success": False, "message": str(response)}
    except Exception as e:
        return {"success": False, "message": f"Error in manage_asset_hunter: {e}"}
