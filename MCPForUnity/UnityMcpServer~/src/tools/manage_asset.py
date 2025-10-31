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
    Perform the specified asset operation in a Unity instance.
    
    This sends a centralized "manage_asset" command to Unity with the provided parameters; string `properties` will be parsed as JSON when possible, and `page_size`/`page_number` are defensively coerced to integers. Parameters with a value of None are omitted from the sent payload.
    
    Parameters:
    	action (Literal): CRUD-like action to perform (e.g., "import", "create", "modify", "delete", "duplicate", "move", "rename", "search", "get_info", "create_folder", "get_components").
    	path (str): Asset path or search scope (e.g., "Materials/MyMaterial.mat").
    	asset_type (str | None): Asset type required for "create" (e.g., "Material", "Folder").
    	properties (dict | str | None): Properties for "create"/"modify"; if a JSON string is passed it will be parsed to a dict; defaults to an empty dict when omitted.
    	destination (str | None): Target path for "duplicate" or "move".
    	generate_preview (bool): Whether to generate an asset preview/thumbnail when supported.
    	search_pattern (str | None): Pattern used for searches (e.g., "*.prefab").
    	filter_type (str | None): Type filter for search results.
    	filter_date_after (str | None): Date string used to filter results after the given date.
    	page_size (int | float | str | None): Page size for pagination; will be coerced to an integer when possible.
    	page_number (int | float | str | None): Page number for pagination; will be coerced to an integer when possible.
    	unity_instance (str | None): Target Unity instance identifier (project name, hash, or "Name@hash"); if omitted, the default instance is used.
    
    Returns:
    	dict: The response from Unity as a dictionary. If Unity returns a non-dict result, returns {"success": False, "message": str(result)}.
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