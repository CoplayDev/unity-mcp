from pydantic import BaseModel
from fastmcp import Context

from models import MCPResponse
from services.registry import mcp_for_unity_resource
from services.tools import get_unity_instance_from_context
from transport.unity_transport import send_with_unity_instance
from transport.legacy.unity_connection import async_send_command_with_retry


class Vector3(BaseModel):
    """3D vector."""
    x: float = 0.0
    y: float = 0.0
    z: float = 0.0


class ActiveToolData(BaseModel):
    """Active tool data fields."""
    activeTool: str = ""
    isCustom: bool = False
    pivotMode: str = ""
    pivotRotation: str = ""
    handleRotation: Vector3 = Vector3()
    handlePosition: Vector3 = Vector3()


class ActiveToolResponse(MCPResponse):
    """Information about the currently active editor tool."""
    data: ActiveToolData = ActiveToolData()


@mcp_for_unity_resource(
    uri="unity://editor/active-tool",
    name="editor_active_tool",
    description="Currently active editor tool (Move, Rotate, Scale, etc.) and transform handle settings."
)
async def get_active_tool(ctx: Context) -> ActiveToolResponse | MCPResponse:
    """Get active editor tool information."""
    unity_instance = get_unity_instance_from_context(ctx)
    response = await send_with_unity_instance(
        async_send_command_with_retry,
        unity_instance,
        "get_active_tool",
        {}
    )
    return ActiveToolResponse(**response) if isinstance(response, dict) else response
