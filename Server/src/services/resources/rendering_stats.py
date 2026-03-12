from fastmcp import Context

from models import MCPResponse
from models.unity_response import parse_resource_response
from services.registry import mcp_for_unity_resource
from services.tools import get_unity_instance_from_context
from transport.unity_transport import send_with_unity_instance
from transport.legacy.unity_connection import async_send_command_with_retry


@mcp_for_unity_resource(
    uri="mcpforunity://rendering/stats",
    name="rendering_stats",
    description="Snapshot of rendering performance statistics (draw calls, batches, triangles, frame time, etc.).",
)
async def get_rendering_stats(ctx: Context) -> MCPResponse:
    unity_instance = await get_unity_instance_from_context(ctx)
    response = await send_with_unity_instance(
        async_send_command_with_retry, unity_instance, "get_rendering_stats", {}
    )
    return parse_resource_response(response, MCPResponse)
