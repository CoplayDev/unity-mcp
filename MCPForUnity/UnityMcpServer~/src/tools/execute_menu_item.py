"""
Defines the execute_menu_item tool for executing and reading Unity Editor menu items.
"""
from typing import Annotated, Any

from fastmcp import Context

from models import MCPResponse
from registry import mcp_for_unity_tool
from unity_connection import async_send_command_with_retry


@mcp_for_unity_tool(
    description="Execute a Unity menu item by path."
)
async def execute_menu_item(
    ctx: Context,
    menu_path: Annotated[str,
                         "Menu path for 'execute' or 'exists' (e.g., 'File/Save Project')"] | None = None,
    unity_instance: Annotated[str,
                             "Target Unity instance (project name, hash, or 'Name@hash'). If not specified, uses default instance."] | None = None,
) -> MCPResponse:
    """
    Execute a Unity Editor menu item specified by its menu path, optionally targeting a specific Unity instance.
    
    Parameters:
        menu_path (str | None): Unity menu path to execute (e.g., "File/Save Project"). If None, no menu path is sent.
        unity_instance (str | None): Target Unity instance identifier (project name, hash, or "Name@hash"). If None, the default instance is used.
    
    Returns:
        MCPResponse if the command result is a dictionary, otherwise the raw command result.
    """
    await ctx.info(f"Processing execute_menu_item: {menu_path}")
    params_dict: dict[str, Any] = {"menuPath": menu_path}
    params_dict = {k: v for k, v in params_dict.items() if v is not None}
    result = await async_send_command_with_retry("execute_menu_item", params_dict, instance_id=unity_instance)
    return MCPResponse(**result) if isinstance(result, dict) else result