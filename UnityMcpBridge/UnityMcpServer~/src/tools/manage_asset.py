"""
Defines the manage_asset tool for interacting with Unity assets.
"""
import asyncio
from typing import Annotated, Any, Literal

from mcp.server.fastmcp import FastMCP, Context

from unity_connection import get_unity_connection, async_send_command_with_retry
from telemetry_decorator import telemetry_tool


def register_manage_asset_tools(mcp: FastMCP):
    """Registers the manage_asset tool with the MCP server."""

    @mcp.tool(description="Performs asset operations (import, create, modify, delete, etc.) in Unity.")
    @telemetry_tool("manage_asset")
    async def manage_asset(
        ctx: Context,
        action: Annotated[Literal["import", "create", "modify", "delete", "duplicate", "move", "rename", "search", "get_info", "create_folder", "get_components"], "Operations"],
        path: Annotated[str, "Asset path (e.g., 'Materials/MyMaterial.mat') or search scope."],
        asset_type: Annotated[str,
                              "Asset type (e.g., 'Material', 'Folder') - required for 'create'."] | None = None,
        properties: Annotated[dict[str, Any],
                              "Dictionary of properties for 'create'/'modify'."] | None = None,
        destination: Annotated[str,
                               "Target path for 'duplicate'/'move'."] | None = None,
        generate_preview: Annotated[bool,
                                    "When true, `close_stage` will save the prefab before exiting the stage."] = False,
        search_pattern: Annotated[str,
                                  "Search pattern (e.g., '*.prefab')."] | None = None,
        filter_type: Annotated[str, "Filter type for search"] | None = None,
        filter_date_after: Annotated[str,
                                     "Date after which to filter"] | None = None,
        page_size: Annotated[int, "Page size for pagination"] | None = None,
        page_number: Annotated[int, "Page number for pagination"] | None = None
    ) -> dict[str, Any]:
        """ (import, create, modify, delete, etc.) in Unity.

        Args:
            ctx: The MCP context.
            action: Operation to perform (e.g., 'import', 'create', 'modify', 'delete', 'duplicate', 'move', 'rename', 'search', 'get_info', 'create_folder', 'get_components').
            path: Asset path (e.g., "Materials/MyMaterial.mat") or search scope.
            asset_type: Asset type (e.g., 'Material', 'Folder') - required for 'create'.
            properties: Dictionary of properties for 'create'/'modify'.
                example properties for Material: {"color": [1, 0, 0, 1], "shader": "Standard"}.
                example properties for Texture: {"width": 1024, "height": 1024, "format": "RGBA32"}.
                example properties for PhysicsMaterial: {"bounciness": 1.0, "staticFriction": 0.5, "dynamicFriction": 0.5}.
            destination: Target path for 'duplicate'/'move'.
            search_pattern: Search pattern (e.g., '*.prefab').
            filter_*: Filters for search (type, date).
            page_*: Pagination for search.

        Returns:
            A dictionary with operation results ('success', 'data', 'error').
        """
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
        # Get the Unity connection instance
        connection = get_unity_connection()

        # Use centralized async retry helper to avoid blocking the event loop
        result = await async_send_command_with_retry("manage_asset", params_dict, loop=loop)
        # Return the result obtained from Unity
        return result if isinstance(result, dict) else {"success": False, "message": str(result)}
