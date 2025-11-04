from pydantic import BaseModel
from models import MCPResponse
from registry import mcp_for_unity_resource
from unity_connection import async_send_command_with_retry


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
async def get_project_info() -> ProjectInfoResponse:
    """Get static project configuration information."""
    response = await async_send_command_with_retry("get_project_info", {})
    return ProjectInfoResponse(**response) if isinstance(response, dict) else response
