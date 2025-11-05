from pydantic import BaseModel
from models import MCPResponse
from registry import mcp_for_unity_resource
from unity_connection import async_send_command_with_retry


class EditorStateData(BaseModel):
    """Editor state data fields."""
    isPlaying: bool = False
    isPaused: bool = False
    isCompiling: bool = False
    isUpdating: bool = False
    timeSinceStartup: float = 0.0
    activeSceneName: str = ""
    selectionCount: int = 0
    activeObjectName: str | None = None


class EditorStateResponse(MCPResponse):
    """Dynamic editor state information that changes frequently."""
    data: EditorStateData = EditorStateData()


@mcp_for_unity_resource(
    uri="unity://editor/state",
    name="editor_state",
    description="Current editor runtime state including play mode, compilation status, active scene, and selection summary. Refresh frequently for up-to-date information."
)
async def get_editor_state() -> EditorStateResponse | MCPResponse:
    """Get current editor runtime state."""
    response = await async_send_command_with_retry("get_editor_state", {})
    return EditorStateResponse(**response) if isinstance(response, dict) else response
