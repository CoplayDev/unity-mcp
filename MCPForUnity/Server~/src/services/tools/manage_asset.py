"""
Defines the manage_asset tool for interacting with Unity assets.
"""
import ast
import asyncio
import json
from typing import Annotated, Any, Literal

from fastmcp import Context
from services.registry import mcp_for_unity_tool
from services.tools import get_unity_instance_from_context
from services.tools.utils import parse_json_payload
from transport.unity_transport import send_with_unity_instance
from transport.legacy.unity_connection import async_send_command_with_retry


@mcp_for_unity_tool(
    description="Performs asset operations (import, create, modify, delete, etc.) in Unity."
)
async def manage_asset(
    ctx: Context,
    action: Annotated[Literal["import", "create", "modify", "delete", "duplicate", "move", "rename", "search", "get_info", "create_folder", "get_components"], "Perform CRUD operations on assets."],
    path: Annotated[str, "Asset path (e.g., 'Materials/MyMaterial.mat') or search scope."],
    asset_type: Annotated[str,
                          "Asset type (e.g., 'Material', 'Folder') - required for 'create'."] | None = None,
    properties: Annotated[dict[str, Any] | str,
                          "Dictionary (or JSON string) of properties for 'create'/'modify'."] | None = None,
    destination: Annotated[str,
                           "Target path for 'duplicate'/'move'."] | None = None,
    generate_preview: Annotated[bool,
                                "Generate a preview/thumbnail for the asset when supported."] = False,
    search_pattern: Annotated[str,
                              "Search pattern (e.g., '*.prefab')."] | None = None,
    filter_type: Annotated[str, "Filter type for search"] | None = None,
    filter_date_after: Annotated[str,
                                 "Date after which to filter"] | None = None,
    page_size: Annotated[int | float | str,
                         "Page size for pagination"] | None = None,
    page_number: Annotated[int | float | str,
                           "Page number for pagination"] | None = None,
) -> dict[str, Any]:
    unity_instance = get_unity_instance_from_context(ctx)

    def _parse_properties_string(raw: str) -> tuple[dict[str, Any] | None, str | None]:
        try:
            parsed = json.loads(raw)
            if not isinstance(parsed, dict):
                return None, f"manage_asset: properties JSON must decode to a dictionary; received {type(parsed)}"
            return parsed, "JSON"
        except json.JSONDecodeError as json_err:
            try:
                parsed = ast.literal_eval(raw)
                if not isinstance(parsed, dict):
                    return None, f"manage_asset: properties string must evaluate to a dictionary; received {type(parsed)}"
                return parsed, "Python literal"
            except (ValueError, SyntaxError) as literal_err:
                return None, f"manage_asset: failed to parse properties string. JSON error: {json_err}; literal_eval error: {literal_err}"

    async def _normalize_properties(raw: dict[str, Any] | str | None) -> tuple[dict[str, Any] | None, str | None]:
        if raw is None:
            return {}, None
        if isinstance(raw, dict):
            await ctx.info(f"manage_asset: received properties as dict with keys: {list(raw.keys())}")
            return raw, None
        if isinstance(raw, str):
            await ctx.info(f"manage_asset: received properties as string (first 100 chars): {raw[:100]}")
            # Try our robust centralized parser first, then fallback to ast.literal_eval specific to manage_asset if needed
            parsed = parse_json_payload(raw)
            if isinstance(parsed, dict):
                 await ctx.info("manage_asset: coerced properties using centralized parser")
                 return parsed, None

            # Fallback to original logic for ast.literal_eval which parse_json_payload avoids for safety/simplicity
            parsed, source = _parse_properties_string(raw)
            if parsed is None:
                return None, source
            await ctx.info(f"manage_asset: coerced properties from {source} string to dict")
            return parsed, None
        return None, f"manage_asset: properties must be a dict or JSON string; received {type(raw)}"

    properties, parse_error = await _normalize_properties(properties)
    if parse_error:
        await ctx.error(parse_error)
        return {"success": False, "message": parse_error}

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
    result = await send_with_unity_instance(async_send_command_with_retry, unity_instance, "manage_asset", params_dict, loop=loop)
    # Return the result obtained from Unity
    return result if isinstance(result, dict) else {"success": False, "message": str(result)}
