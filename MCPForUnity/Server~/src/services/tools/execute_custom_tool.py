from fastmcp import Context
from models.models import MCPResponse

from services.custom_tool_service import (
    CustomToolService,
    resolve_project_id_for_unity_instance,
)
from services.registry import mcp_for_unity_tool
from services.tools import get_unity_instance_from_context


@mcp_for_unity_tool(
    name="execute_custom_tool",
    description="Execute a project-scoped custom tool registered by Unity.",
)
async def execute_custom_tool(ctx: Context, tool_name: str, parameters: dict | None = None) -> MCPResponse:
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

    if not isinstance(parameters, dict):
        return MCPResponse(
            success=False,
            message="parameters must be an object/dictionary",
        )

    service = CustomToolService.get_instance()
    return await service.execute_tool(project_id, tool_name, unity_instance, parameters)
