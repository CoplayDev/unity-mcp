"""
Defines the manage_menu_item tool for executing and reading Unity Editor menu items.
"""
import asyncio
from typing import Annotated, Any, Literal

from mcp.server.fastmcp import Context
from registry import mcp_for_unity_tool
from unity_connection import async_send_command_with_retry


@mcp_for_unity_tool(
    description="Manage Unity menu items (execute/list/exists). If you're not sure what menu item to use, use the 'list' action to find it before using 'execute'."
)
async def manage_menu_item(
    ctx: Context,
    action: Annotated[Literal["execute", "list", "exists"], "Read and execute Unity menu items."],
    menu_path: Annotated[str,
                         "Menu path for 'execute' or 'exists' (e.g., 'File/Save Project')"] | None = None,
    search: Annotated[str,
                      "Optional filter string for 'list' (e.g., 'Save')"] | None = None,
    refresh: Annotated[bool,
                       "Optional flag to force refresh of the menu cache when listing"] | None = None,
) -> dict[str, Any]:
    await ctx.info(f"Processing manage_menu_item: {action}")
    # Prepare parameters for the C# handler
    params_dict: dict[str, Any] = {
        "action": action,
        "menuPath": menu_path,
        "search": search,
        "refresh": refresh,
    }
    # Remove None values
    params_dict = {k: v for k, v in params_dict.items() if v is not None}

    # Get the current asyncio event loop
    loop = asyncio.get_running_loop()

    # Use centralized async retry helper
    result = await async_send_command_with_retry("manage_menu_item", params_dict, loop=loop)
    return result if isinstance(result, dict) else {"success": False, "message": str(result)}
