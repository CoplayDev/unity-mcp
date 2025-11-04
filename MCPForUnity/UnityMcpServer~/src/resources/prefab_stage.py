from pydantic import BaseModel
from models import MCPResponse
from registry import mcp_for_unity_resource
from unity_connection import async_send_command_with_retry


class PrefabStageData(BaseModel):
    """Prefab stage data fields."""
    isOpen: bool = False
    assetPath: str | None = None
    prefabRootName: str | None = None
    mode: str | None = None
    isDirty: bool = False


class PrefabStageResponse(MCPResponse):
    """Information about the current prefab editing context."""
    data: PrefabStageData = PrefabStageData()


@mcp_for_unity_resource(
    uri="unity://editor/prefab-stage",
    name="editor_prefab_stage",
    description="Current prefab editing context if a prefab is open in isolation mode. Returns isOpen=false if no prefab is being edited."
)
async def get_prefab_stage() -> PrefabStageResponse:
    """Get current prefab stage information."""
    response = await async_send_command_with_retry("get_prefab_stage", {})
    return PrefabStageResponse(**response) if isinstance(response, dict) else response
