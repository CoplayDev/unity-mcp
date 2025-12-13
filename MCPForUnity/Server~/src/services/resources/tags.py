from pydantic import Field
from fastmcp import Context

from models import MCPResponse
from services.registry import mcp_for_unity_resource
from services.tools import get_unity_instance_from_context
from transport.unity_transport import send_with_unity_instance
from transport.legacy.unity_connection import async_send_command_with_retry


class TagsResponse(MCPResponse):
    """List of all tags in the project."""
    data: list[str] = Field(default_factory=list)


@mcp_for_unity_resource(
    uri="unity://project/tags",
    name="project_tags",
    description="All tags defined in the project's TagManager. Read this before using add_tag or remove_tag tools."
)
async def get_tags(ctx: Context) -> TagsResponse | MCPResponse:
    """Get all project tags."""
    unity_instance = get_unity_instance_from_context(ctx)
    response = await send_with_unity_instance(
        async_send_command_with_retry,
        unity_instance,
        "get_tags",
        {}
    )
    return TagsResponse(**response) if isinstance(response, dict) else response
