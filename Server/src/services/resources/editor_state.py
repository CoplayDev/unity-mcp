from pydantic import BaseModel
from fastmcp import Context

from models import MCPResponse
from services.registry import mcp_for_unity_resource
from services.tools import get_unity_instance_from_context
from transport.unity_transport import send_with_unity_instance
from transport.legacy.unity_connection import async_send_command_with_retry


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
async def get_editor_state(ctx: Context) -> EditorStateResponse | MCPResponse:
    """Get current editor runtime state."""
    unity_instance = get_unity_instance_from_context(ctx)
    response = await send_with_unity_instance(
        async_send_command_with_retry,
        unity_instance,
        "get_editor_state",
        {}
    )
    return EditorStateResponse(**response) if isinstance(response, dict) else response
