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
) -> MCPResponse:
    await ctx.info(f"Processing execute_menu_item: {menu_path}")
    params_dict: dict[str, Any] = {"menuPath": menu_path}
    params_dict = {k: v for k, v in params_dict.items() if v is not None}
    result = await async_send_command_with_retry("execute_menu_item", params_dict)
    return MCPResponse(**result) if isinstance(result, dict) else result
