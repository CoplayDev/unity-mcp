"""
Defines the execute_menu_item tool for executing and reading Unity Editor menu items.
"""
from typing import Annotated, Any

from fastmcp import Context
from mcp.types import ToolAnnotations

from models import MCPResponse
from services.registry import mcp_for_unity_tool
from services.tools import get_unity_instance_from_context
from transport.unity_transport import send_with_unity_instance
from transport.legacy.unity_connection import async_send_command_with_retry


@mcp_for_unity_tool(
    description="Execute a Unity menu item by path.",
    annotations=ToolAnnotations(
        title="Execute Menu Item",
        destructiveHint=True,
    ),
)
async def execute_menu_item(
    ctx: Context,
    menu_path: Annotated[str,
                         "Menu path for 'execute' or 'exists' (e.g., 'File/Save Project')"] | None = None,
) -> MCPResponse:
    unity_instance = get_unity_instance_from_context(ctx)
    params_dict: dict[str, Any] = {"menuPath": menu_path}
    params_dict = {k: v for k, v in params_dict.items() if v is not None}
    result = await send_with_unity_instance(async_send_command_with_retry, unity_instance, "execute_menu_item", params_dict)
    return MCPResponse(**result) if isinstance(result, dict) else result
