from fastmcp import Context

from models import MCPResponse
from models.unity_response import parse_resource_response
from services.registry import mcp_for_unity_resource
from services.tools import get_unity_instance_from_context
from transport.unity_transport import send_with_unity_instance
from transport.legacy.unity_connection import async_send_command_with_retry


@mcp_for_unity_resource(
    uri="mcpforunity://scene/cameras",
    name="cameras",
    description=(
        "List all cameras in the scene (Unity Camera + CinemachineCamera) with status. "
        "Includes Brain state, Cinemachine camera priorities, pipeline components, "
        "follow/lookAt targets, and Unity Camera info.\n\n"
        "URI: mcpforunity://scene/cameras"
    ),
)
async def get_cameras(ctx: Context) -> MCPResponse:
    """Get all cameras in the scene."""
    unity_instance = await get_unity_instance_from_context(ctx)
    response = await send_with_unity_instance(
        async_send_command_with_retry,
        unity_instance,
        "get_cameras",
        {},
    )
    return parse_resource_response(response, MCPResponse)
