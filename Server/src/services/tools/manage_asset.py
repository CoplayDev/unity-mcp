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
from services.tools.utils import parse_json_payload, coerce_int
from transport.unity_transport import send_with_unity_instance
from transport.legacy.unity_connection import async_send_command_with_retry
from services.tools.preflight import preflight


@mcp_for_unity_tool(
    description=(
        "Performs asset operations (import, create, modify, delete, etc.) in Unity.\n\n"
        "Tip (payload safety): for `action=\"search\"`, prefer paging (`page_size`, `page_number`) and keep "
        "`generate_preview=false` (previews can add large base64 blobs)."
    )
)
async def manage_asset(
    ctx: Context,
    action: Annotated[Literal["import", "create", "modify", "delete", "duplicate", "move", "rename", "search", "get_info", "create_folder", "get_components"], "Perform CRUD operations on assets."],
    path: Annotated[str, "Asset path (e.g., 'Materials/MyMaterial.mat') or search scope (e.g., 'Assets')."],
    asset_type: Annotated[str,
                          "Asset type (e.g., 'Material', 'Folder') - required for 'create'. Note: For ScriptableObjects, use manage_scriptable_object."] | None = None,
    properties: Annotated[dict[str, Any] | str,
                          "Dictionary (or JSON string) of properties for 'create'/'modify'."] | None = None,
    destination: Annotated[str,
                           "Target path for 'duplicate'/'move'."] | None = None,
    generate_preview: Annotated[bool,
                                "Generate a preview/thumbnail for the asset when supported. "
                                "Warning: previews may include large base64 payloads; keep false unless needed."] = False,
    search_pattern: Annotated[str,
                              "Search pattern (e.g., '*.prefab' or AssetDatabase filters like 't:MonoScript'). "
                              "Recommended: put queries like 't:MonoScript' here and set path='Assets'."] | None = None,
    filter_type: Annotated[str, "Filter type for search"] | None = None,
    filter_date_after: Annotated[str,
                                 "Date after which to filter"] | None = None,
    page_size: Annotated[int | float | str,
                         "Page size for pagination. Recommended: 25 (smaller for LLM-friendly responses)."] | None = None,
    page_number: Annotated[int | float | str,
                           "Page number for pagination (1-based)."] | None = None,
) -> dict[str, Any]:
    unity_instance = get_unity_instance_from_context(ctx)

    # Best-effort guard: if Unity is compiling/reloading or known external changes are pending,
    # wait/refresh to avoid stale reads and flaky timeouts.
    gate = await preflight(ctx, wait_for_no_compile=True, refresh_if_dirty=True)
    if gate is not None:
        return gate.model_dump()

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

    page_size = coerce_int(page_size)
    page_number = coerce_int(page_number)

    # --- Payload-safe normalization for common LLM mistakes (search) ---
    # Unity's C# handler treats `path` as a folder scope. If a model mistakenly puts a query like
    # "t:MonoScript" into `path`, Unity will consider it an invalid folder and fall back to searching
    # the entire project, which is token-heavy. Normalize such cases into search_pattern + Assets scope.
    action_l = (action or "").lower()
    if action_l == "search":
        try:
            raw_path = (path or "").strip()
        except (AttributeError, TypeError):
            # Handle case where path is not a string despite type annotation
            raw_path = ""

        # If the caller put an AssetDatabase query into `path`, treat it as `search_pattern`.
        if (not search_pattern) and raw_path.startswith("t:"):
            search_pattern = raw_path
            path = "Assets"
            await ctx.info("manage_asset(search): normalized query from `path` into `search_pattern` and set path='Assets'")

        # If the caller used `asset_type` to mean a search filter, map it to filter_type.
        # (In Unity, filterType becomes `t:<filterType>`.)
        if (not filter_type) and asset_type and isinstance(asset_type, str):
            filter_type = asset_type
            await ctx.info("manage_asset(search): mapped `asset_type` into `filter_type` for safer server-side filtering")

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
