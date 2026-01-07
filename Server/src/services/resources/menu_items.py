from fastmcp import Context

from models import MCPResponse
from services.registry import mcp_for_unity_resource
from services.tools import get_unity_instance_from_context
from transport.unity_transport import send_with_unity_instance
from transport.legacy.unity_connection import async_send_command_with_retry


class GetMenuItemsResponse(MCPResponse):
    data: list[str] = []


@mcp_for_unity_resource(
    uri="mcpforunity://menu-items",
    name="menu_items",
    description="Provides a list of all menu items."
)
async def get_menu_items(ctx: Context) -> GetMenuItemsResponse | MCPResponse:
    """Provides a list of all menu items.
    """
    unity_instance = get_unity_instance_from_context(ctx)
    params = {
        "refresh": True,
        "search": "",
    }

    response = await send_with_unity_instance(
        async_send_command_with_retry,
        unity_instance,
        "get_menu_items",
        params,
    )
    return GetMenuItemsResponse(**response) if isinstance(response, dict) else response
