from models import MCPResponse
from registry import mcp_for_unity_resource
from unity_connection import async_send_command_with_retry


class GetMenuItemsResponse(MCPResponse):
    data: list[str] = []


@mcp_for_unity_resource(
    uri="mcpforunity://menu-items",
    name="get_menu_items",
    description="Provides a list of all menu items."
)
async def get_menu_items() -> GetMenuItemsResponse:
    """Provides a list of all menu items."""
    # Later versions of FastMCP support these as query parameters
    # See: https://gofastmcp.com/servers/resources#query-parameters
    params = {
        "refresh": True,
        "search": "",
    }

    response = await async_send_command_with_retry("get_menu_items", params)
    return GetMenuItemsResponse(**response) if isinstance(response, dict) else response
