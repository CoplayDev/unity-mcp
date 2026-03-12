"""
Tool for Unity Localization — locales, string tables, entry inspection/editing.
Actions: list_locales, get_active_locale, set_active_locale, list_tables, get_entry, set_entry.
Requires com.unity.localization package.
"""
from typing import Annotated, Any, Literal

from fastmcp import Context
from mcp.types import ToolAnnotations

from services.registry import mcp_for_unity_tool
from services.tools import get_unity_instance_from_context
from transport.unity_transport import send_with_unity_instance
from transport.legacy.unity_connection import async_send_command_with_retry


@mcp_for_unity_tool(
    description=(
        "Unity Localization operations. "
        "Actions: list_locales (available locales), "
        "get_active_locale (current active locale), "
        "set_active_locale (switch active locale), "
        "list_tables (string/asset table collections), "
        "get_entry (get localized string for key+locale), "
        "set_entry (set localized string). "
        "Requires com.unity.localization package."
    ),
    annotations=ToolAnnotations(title="Manage Localization"),
)
async def manage_localization(
    ctx: Context,
    action: Annotated[
        Literal[
            "list_locales", "get_active_locale", "set_active_locale",
            "list_tables", "get_entry", "set_entry"
        ],
        "Action to perform on Unity Localization."
    ],
    locale_code: Annotated[str, "Locale code (e.g. 'en', 'ja', 'fr')"] | None = None,
    table: Annotated[str, "String table collection name"] | None = None,
    key: Annotated[str, "Localization key/entry name"] | None = None,
    locale: Annotated[str, "Target locale for get/set entry"] | None = None,
    value: Annotated[str, "Localized string value (for set_entry)"] | None = None,
    type: Annotated[str, "Table type: 'string' or 'asset' (for list_tables)"] | None = None,
) -> dict[str, Any]:
    unity_instance = get_unity_instance_from_context(ctx)

    params = {"action": action, "locale_code": locale_code, "table": table,
              "key": key, "locale": locale, "value": value, "type": type}
    params = {k: v for k, v in params.items() if v is not None}

    try:
        response = await send_with_unity_instance(
            async_send_command_with_retry, unity_instance, "manage_localization", params)
        if isinstance(response, dict) and response.get("success"):
            return {"success": True, "message": response.get("message", "OK"), "data": response.get("data")}
        return response if isinstance(response, dict) else {"success": False, "message": str(response)}
    except Exception as e:
        return {"success": False, "message": f"Python error managing localization: {str(e)}"}
