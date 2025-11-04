from pydantic import BaseModel
from models import MCPResponse
from registry import mcp_for_unity_resource
from unity_connection import async_send_command_with_retry


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
async def get_windows() -> WindowsResponse:
    """Get all open editor windows."""
    response = await async_send_command_with_retry("get_windows", {})
    return WindowsResponse(**response) if isinstance(response, dict) else response
