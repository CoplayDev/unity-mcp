"""
Defines the manage_asset tool for interacting with Unity assets.
"""
import asyncio
import json
from typing import Annotated, Any, Literal

from fastmcp import Context
from registry import mcp_for_unity_tool
from unity_connection import async_send_command_with_retry


@mcp_for_unity_tool(
    description="Performs asset operations (import, create, modify, delete, etc.) in Unity."
)
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
    page_size: Annotated[int | float | str, "Page size for pagination"] | None = None,
    page_number: Annotated[int | float | str, "Page number for pagination"] | None = None,
    unity_instance: Annotated[str,
                             "Target Unity instance (project name, hash, or 'Name@hash'). If not specified, uses default instance."] | None = None
) -> dict[str, Any]:
    """
    Perform an asset operation in a target Unity instance.
    
    Parameters:
        ctx (Context): Execution context for logging and interaction.
        action (Literal[...]): Operation to perform: "import", "create", "modify", "delete", "duplicate", "move", "rename", "search", "get_info", "create_folder", or "get_components".
        path (str): Asset path (e.g., "Materials/MyMaterial.mat") or search scope.
        asset_type (str | None): Asset type required when creating an asset (e.g., "Material", "Folder").
        properties (dict[str, Any] | None): Properties for "create" or "modify"; if a JSON string is provided it will be parsed to a dict.
        destination (str | None): Target path for "duplicate" or "move".
        generate_preview (bool): If true, request generation of a preview/thumbnail when supported.
        search_pattern (str | None): Pattern for searching assets (e.g., "*.prefab").
        filter_type (str | None): Additional filter to apply during search.
        filter_date_after (str | None): ISO-like date string to filter assets modified after this date.
        page_size (int | float | str | None): Page size for paginated search results; non-integer or invalid values are coerced or ignored.
        page_number (int | float | str | None): Page number for paginated search results; non-integer or invalid values are coerced or ignored.
        unity_instance (str | None): Target Unity instance identifier (project name, hash, or "Name@hash"); if omitted the default instance is used.
    
    Returns:
        result (dict[str, Any]): Response from Unity as a dictionary; on unexpected non-dict responses returns {"success": False, "message": <stringified result>}.
    """
    ctx.info(f"Processing manage_asset: {action} (unity_instance={unity_instance or 'default'})")
    # Coerce 'properties' from JSON string to dict for client compatibility
    if isinstance(properties, str):
        try:
            properties = json.loads(properties)
            ctx.info("manage_asset: coerced properties from JSON string to dict")
        except Exception as e:
            ctx.warn(f"manage_asset: failed to parse properties JSON string: {e}")
            # Leave properties as-is; Unity side may handle defaults
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

    # Use centralized async retry helper with instance routing
    result = await async_send_command_with_retry("manage_asset", params_dict, instance_id=unity_instance, loop=loop)
    # Return the result obtained from Unity
    return result if isinstance(result, dict) else {"success": False, "message": str(result)}