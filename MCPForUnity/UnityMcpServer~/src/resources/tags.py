from pydantic import Field
from models import MCPResponse
from registry import mcp_for_unity_resource
from unity_connection import async_send_command_with_retry


class TagsResponse(MCPResponse):
    """List of all tags in the project."""
    data: list[str] = Field(default_factory=list)


@mcp_for_unity_resource(
    uri="unity://project/tags",
    name="project_tags",
    description="All tags defined in the project's TagManager. Read this before using add_tag or remove_tag tools."
)
async def get_tags() -> TagsResponse:
    """Get all project tags."""
    response = await async_send_command_with_retry("get_tags", {})
    return TagsResponse(**response) if isinstance(response, dict) else response
