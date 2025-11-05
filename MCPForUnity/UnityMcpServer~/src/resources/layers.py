from fastmcp import Context

from models import MCPResponse
from registry import mcp_for_unity_resource
from tools import get_unity_instance_from_context, async_send_with_unity_instance
from unity_connection import async_send_command_with_retry


class LayersResponse(MCPResponse):
    """Dictionary of layer indices to layer names."""
    data: dict[int, str] = {}


@mcp_for_unity_resource(
    uri="unity://project/layers",
    name="project_layers",
    description="All layers defined in the project's TagManager with their indices (0-31). Read this before using add_layer or remove_layer tools."
)
async def get_layers(ctx: Context) -> LayersResponse | MCPResponse:
    """Get all project layers with their indices."""
    unity_instance = get_unity_instance_from_context(ctx)
    response = await async_send_with_unity_instance(
        async_send_command_with_retry,
        unity_instance,
        "get_layers",
        {}
    )
    return LayersResponse(**response) if isinstance(response, dict) else response
