from pydantic import BaseModel
from fastmcp import Context

from models import MCPResponse
from services.registry import mcp_for_unity_resource
from services.tools import get_unity_instance_from_context
from transport.unity_transport import send_with_unity_instance
from transport.legacy.unity_connection import async_send_command_with_retry


class WindowPosition(BaseModel):
    """Window position and size."""
    x: float = 0.0
    y: float = 0.0
    width: float = 0.0
    height: float = 0.0


class WindowInfo(BaseModel):
    """Information about an editor window."""
    title: str = ""
    typeName: str = ""
    isFocused: bool = False
    position: WindowPosition = WindowPosition()
    instanceID: int = 0


class WindowsResponse(MCPResponse):
    """List of all open editor windows."""
    data: list[WindowInfo] = []


@mcp_for_unity_resource(
    uri="unity://editor/windows",
    name="editor_windows",
    description="All currently open editor windows with their titles, types, positions, and focus state."
)
async def get_windows(ctx: Context) -> WindowsResponse | MCPResponse:
    """Get all open editor windows."""
    unity_instance = get_unity_instance_from_context(ctx)
    response = await send_with_unity_instance(
        async_send_command_with_retry,
        unity_instance,
        "get_windows",
        {}
    )
    return WindowsResponse(**response) if isinstance(response, dict) else response
