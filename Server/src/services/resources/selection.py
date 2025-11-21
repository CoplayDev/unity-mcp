from pydantic import BaseModel
from fastmcp import Context

from models import MCPResponse
from services.registry import mcp_for_unity_resource
from services.tools import get_unity_instance_from_context
from transport.unity_transport import send_with_unity_instance
from transport.legacy.unity_connection import async_send_command_with_retry


class SelectionObjectInfo(BaseModel):
    """Information about a selected object."""
    name: str | None = None
    type: str | None = None
    instanceID: int | None = None


class SelectionGameObjectInfo(BaseModel):
    """Information about a selected GameObject."""
    name: str | None = None
    instanceID: int | None = None


class SelectionData(BaseModel):
    """Selection data fields."""
    activeObject: str | None = None
    activeGameObject: str | None = None
    activeTransform: str | None = None
    activeInstanceID: int = 0
    count: int = 0
    objects: list[SelectionObjectInfo] = []
    gameObjects: list[SelectionGameObjectInfo] = []
    assetGUIDs: list[str] = []


class SelectionResponse(MCPResponse):
    """Detailed information about the current editor selection."""
    data: SelectionData = SelectionData()


@mcp_for_unity_resource(
    uri="unity://editor/selection",
    name="editor_selection",
    description="Detailed information about currently selected objects in the editor, including GameObjects, assets, and their properties."
)
async def get_selection(ctx: Context) -> SelectionResponse | MCPResponse:
    """Get detailed editor selection information."""
    unity_instance = get_unity_instance_from_context(ctx)
    response = await send_with_unity_instance(
        async_send_command_with_retry,
        unity_instance,
        "get_selection",
        {}
    )
    return SelectionResponse(**response) if isinstance(response, dict) else response
