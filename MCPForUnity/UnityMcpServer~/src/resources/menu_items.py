from models import MCPResponse
from registry import mcp_for_unity_resource
from unity_connection import async_send_command_with_retry


class GetMenuItemsResponse(MCPResponse):
    data: list[str] = []


@mcp_for_unity_resource(
    uri="mcpforunity://menu-items{?unity_instance}",
    name="get_menu_items",
    description="Provides a list of all menu items."
)
async def get_menu_items(unity_instance: str | None = None) -> GetMenuItemsResponse:
    """
    Retrieve a list of all menu items.
    
    Args:
        unity_instance (str | None): Optional Unity instance identifier (project name, hash, or 'Name@hash'). If omitted, the default instance is used.
    
    Returns:
        GetMenuItemsResponse or other: A GetMenuItemsResponse containing a `data` list of menu item strings when the backend returns a dict; otherwise the raw backend response.
    """
    params = {
        "refresh": True,
        "search": "",
    }

    response = await async_send_command_with_retry("get_menu_items", params, instance_id=unity_instance)
    return GetMenuItemsResponse(**response) if isinstance(response, dict) else response