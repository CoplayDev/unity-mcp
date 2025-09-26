"""
Defines the manage_menu_item tool for executing and reading Unity Editor menu items.
"""
import asyncio
from typing import Annotated, Any, Literal

from mcp.server.fastmcp import FastMCP, Context
from telemetry_decorator import telemetry_tool

from unity_connection import get_unity_connection, async_send_command_with_retry


def register_manage_menu_item_tools(mcp: FastMCP):
    """Registers the manage_menu_item tool with the MCP server."""

    @mcp.tool()
    @telemetry_tool("manage_menu_item")
    async def manage_menu_item(
        ctx: Context,
        action: Annotated[Literal["execute", "list", "exists"], "One of 'execute', 'list', 'exists'"],
        menu_path: Annotated[str | None,
                             "Menu path for 'execute' or 'exists' (e.g., 'File/Save Project')"] = None,
        search: Annotated[str | None,
                          "Optional filter string for 'list' (e.g., 'Save')"] = None,
        refresh: Annotated[bool | None,
                           "Optional flag to force refresh of the menu cache when listing"] = None,
    ) -> dict[str, Any]:
        """Manage Unity menu items (execute/list/exists).

        Args:
            ctx: The MCP context.
            action: One of 'execute', 'list', 'exists'.
            menu_path: Menu path for 'execute' or 'exists' (e.g., "File/Save Project").
            search: Optional filter string for 'list'.
            refresh: Optional flag to force refresh of the menu cache when listing.

        Returns:
            A dictionary with operation results ('success', 'data', 'error').
        """
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
        # Touch the connection to ensure availability (mirrors other tools' pattern)
        _ = get_unity_connection()

        # Use centralized async retry helper
        result = await async_send_command_with_retry("manage_menu_item", params_dict, loop=loop)
        return result if isinstance(result, dict) else {"success": False, "message": str(result)}
