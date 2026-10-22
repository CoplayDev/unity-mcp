"""Bounded Addressables configuration resources."""

from fastmcp import Context

from models import MCPResponse
from models.unity_response import normalize_unity_response, parse_resource_response
from services.registry import mcp_for_unity_resource
from services.tools.manage_addressables import manage_addressables


@mcp_for_unity_resource(
    uri="mcpforunity://addressables/settings",
    name="addressables_settings",
    description=(
        "Read basic Addressables settings without creating them. "
        "Requires com.unity.addressables and an existing Addressables settings asset."
    ),
)
async def get_addressables_settings(ctx: Context) -> MCPResponse:
    response = await manage_addressables(ctx, action="get_settings")
    return parse_resource_response(normalize_unity_response(response), MCPResponse)


@mcp_for_unity_resource(
    uri="mcpforunity://addressables/groups",
    name="addressables_groups",
    description=(
        "Read the first 50 Addressables groups. Continue with manage_addressables group_list "
        "using the returned next_cursor, or choose page_size there. "
        "Requires com.unity.addressables and existing Addressables settings; does not create them."
    ),
)
async def get_addressables_groups(ctx: Context) -> MCPResponse:
    response = await manage_addressables(ctx, action="group_list")
    return parse_resource_response(normalize_unity_response(response), MCPResponse)
