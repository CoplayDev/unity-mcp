from pydantic import BaseModel
from fastmcp import Context

from models import MCPResponse
from services.registry import mcp_for_unity_resource
from services.tools import get_unity_instance_from_context
from transport.unity_transport import send_with_unity_instance
from transport.legacy.unity_connection import async_send_command_with_retry


class ProjectInfoData(BaseModel):
    """Project info data fields."""
    projectRoot: str = ""
    projectName: str = ""
    unityVersion: str = ""
    platform: str = ""
    assetsPath: str = ""


class ProjectInfoResponse(MCPResponse):
    """Static project configuration information."""
    data: ProjectInfoData = ProjectInfoData()


@mcp_for_unity_resource(
    uri="unity://project/info",
    name="project_info",
    description="Static project information including root path, Unity version, and platform. This data rarely changes."
)
async def get_project_info(ctx: Context) -> ProjectInfoResponse | MCPResponse:
    """Get static project configuration information."""
    unity_instance = get_unity_instance_from_context(ctx)
    response = await send_with_unity_instance(
        async_send_command_with_retry,
        unity_instance,
        "get_project_info",
        {}
    )
    return ProjectInfoResponse(**response) if isinstance(response, dict) else response
