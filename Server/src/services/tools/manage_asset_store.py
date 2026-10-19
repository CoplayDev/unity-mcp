from typing import Annotated, Any, Optional

from fastmcp import Context
from mcp.types import ToolAnnotations

from services.registry import mcp_for_unity_tool
from services.tools import get_unity_instance_from_context
from transport.unity_transport import send_with_unity_instance
from transport.legacy.unity_connection import async_send_command_with_retry

ALL_ACTIONS = [
    "check_auth", "list_purchases", "download", "import",
]


async def _send_asset_store_command(
    ctx: Context,
    params_dict: dict[str, Any],
) -> dict[str, Any]:
    unity_instance = await get_unity_instance_from_context(ctx)
    result = await send_with_unity_instance(
        async_send_command_with_retry, unity_instance, "manage_asset_store", params_dict
    )
    return result if isinstance(result, dict) else {"success": False, "message": str(result)}


@mcp_for_unity_tool(
    group="core",
    description=(
        "Manage Unity Asset Store packages: list purchases, download, and import.\n\n"
        "AUTH:\n"
        "- check_auth: Check if user is logged into their Unity account\n\n"
        "QUERY:\n"
        "- list_purchases: List purchased Asset Store packages (My Assets)\n\n"
        "INSTALL:\n"
        "- download: Download an Asset Store package by product ID\n"
        "- import: Import an already-downloaded Asset Store package"
    ),
    annotations=ToolAnnotations(
        title="Manage Asset Store",
        destructiveHint=True,
        readOnlyHint=False,
    ),
)
async def manage_asset_store(
    ctx: Context,
    action: Annotated[str, "The asset store action to perform."],
    product_id: Annotated[Optional[int], "Asset Store product ID (for download/import actions)."] = None,
    page: Annotated[Optional[int], "Page number for list_purchases (1-based)."] = None,
    page_size: Annotated[Optional[int], "Results per page for list_purchases."] = None,
) -> dict[str, Any]:
    action_lower = action.lower()
    if action_lower not in ALL_ACTIONS:
        return {
            "success": False,
            "message": f"Unknown action '{action}'. Valid actions: {', '.join(ALL_ACTIONS)}",
        }

    params_dict: dict[str, Any] = {"action": action_lower}
    param_map = {
        "product_id": product_id,
        "page": page,
        "page_size": page_size,
    }
    for key, val in param_map.items():
        if val is not None:
            params_dict[key] = val

    return await _send_asset_store_command(ctx, params_dict)
