"""
Defines the manage_asset tool for interacting with Unity assets.
"""
import asyncio
from typing import Annotated, Any, Literal

from mcp.server.fastmcp import FastMCP, Context

from unity_connection import async_send_command_with_retry
from telemetry_decorator import telemetry_tool


def register_manage_asset_tools(mcp: FastMCP):
    """Registers the manage_asset tool with the MCP server."""

    @mcp.tool(name="manage_asset", description="Performs asset operations (import, create, modify, delete, etc.) in Unity.")
    @telemetry_tool("manage_asset")
    async def manage_asset(
        ctx: Context,
        action: Annotated[Literal["import", "create", "modify", "delete", "duplicate", "move", "rename", "search", "get_info", "create_folder", "get_components"], "Perform CRUD operations on assets."],
        path: Annotated[str, "Asset path (e.g., 'Materials/MyMaterial.mat') or search scope."],
        asset_type: Annotated[str,
                              "Asset type (e.g., 'Material', 'Folder') - required for 'create'."] | None = None,
        properties: Annotated[dict[str, Any],
                              "Dictionary of properties for 'create'/'modify'."] | None = None,
        destination: Annotated[str,
                               "Target path for 'duplicate'/'move'."] | None = None,
        generate_preview: Annotated[bool,
                                    "Generate a preview/thumbnail for the asset when supported."] = False,
        search_pattern: Annotated[str,
                                  "Search pattern (e.g., '*.prefab')."] | None = None,
        filter_type: Annotated[str, "Filter type for search"] | None = None,
        filter_date_after: Annotated[str,
                                     "Date after which to filter"] | None = None,
        page_size: Annotated[int, "Page size for pagination"] | None = None,
        page_number: Annotated[int, "Page number for pagination"] | None = None
    ) -> dict[str, Any]:
        ctx.info(f"Processing manage_asset: {action}")
        # Ensure properties is a dict if None
        if properties is None:
            properties = {}

        # Coerce numeric inputs defensively
        def _coerce_int(value, default=None):
            if value is None:
                return default
            try:
                if isinstance(value, bool):
                    return default
                if isinstance(value, int):
                    return int(value)
                s = str(value).strip()
                if s.lower() in ("", "none", "null"):
                    return default
                return int(float(s))
            except Exception:
                return default

        page_size = _coerce_int(page_size)
        page_number = _coerce_int(page_number)

        # Prepare parameters for the C# handler
        params_dict = {
            "action": action.lower(),
            "path": path,
            "assetType": asset_type,
            "properties": properties,
            "destination": destination,
            "generatePreview": generate_preview,
            "searchPattern": search_pattern,
            "filterType": filter_type,
            "filterDateAfter": filter_date_after,
            "pageSize": page_size,
            "pageNumber": page_number
        }

        # Remove None values to avoid sending unnecessary nulls
        params_dict = {k: v for k, v in params_dict.items() if v is not None}

        # Get the current asyncio event loop
        loop = asyncio.get_running_loop()

        # Use centralized async retry helper to avoid blocking the event loop
        result = await async_send_command_with_retry("manage_asset", params_dict, loop=loop)
        # Return the result obtained from Unity
        return result if isinstance(result, dict) else {"success": False, "message": str(result)}
