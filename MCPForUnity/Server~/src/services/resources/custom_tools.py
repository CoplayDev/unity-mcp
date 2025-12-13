from fastmcp import Context
from pydantic import BaseModel

from models import MCPResponse
from services.custom_tool_service import (
    CustomToolService,
    resolve_project_id_for_unity_instance,
    ToolDefinitionModel,
)
from services.registry import mcp_for_unity_resource
from services.tools import get_unity_instance_from_context


class CustomToolsData(BaseModel):
    project_id: str
    tool_count: int
    tools: list[ToolDefinitionModel]


class CustomToolsResourceResponse(MCPResponse):
    data: CustomToolsData | None = None


@mcp_for_unity_resource(
    uri="unity://custom-tools",
    name="custom_tools",
    description="Lists custom tools available for the active Unity project.",
)
async def get_custom_tools(ctx: Context) -> CustomToolsResourceResponse | MCPResponse:
    unity_instance = get_unity_instance_from_context(ctx)
    if not unity_instance:
        return MCPResponse(
            success=False,
            message="No active Unity instance. Call set_active_instance with Name@hash from unity://instances.",
        )

    project_id = resolve_project_id_for_unity_instance(unity_instance)
    if project_id is None:
        return MCPResponse(
            success=False,
            message=f"Could not resolve project id for {unity_instance}. Ensure Unity is running and reachable.",
        )

    service = CustomToolService.get_instance()
    tools = await service.list_registered_tools(project_id)

    data = CustomToolsData(
        project_id=project_id,
        tool_count=len(tools),
        tools=tools,
    )

    return CustomToolsResourceResponse(
        success=True,
        message="Custom tools retrieved successfully.",
        data=data,
    )
