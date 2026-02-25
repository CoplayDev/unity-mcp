from typing import Annotated, Any

from fastmcp import Context
from mcp.types import ToolAnnotations

from services.registry import mcp_for_unity_tool
from services.tools import get_unity_instance_from_context
from transport.unity_transport import send_with_unity_instance
from transport.legacy.unity_connection import async_send_command_with_retry


@mcp_for_unity_tool(
    description="Forces Unity to reserialize assets, updating them to the current serialization format. "
    "Useful after Unity version upgrades, script changes that affect serialized data, "
    "or when assets need their .meta files regenerated. "
    "Provide either 'path' for a single asset or 'paths' for multiple assets.",
    annotations=ToolAnnotations(
        title="Reserialize Assets",
    ),
)
async def reserialize_assets(
    ctx: Context,
    path: Annotated[
        str,
        "Single asset path to reserialize (e.g., 'Assets/Prefabs/Player.prefab')."
    ] | None = None,
    paths: Annotated[
        list[str],
        "Array of asset paths to reserialize."
    ] | None = None,
) -> dict[str, Any]:
    unity_instance = get_unity_instance_from_context(ctx)

    try:
        params: dict[str, Any] = {}

        if paths is not None:
            params["paths"] = paths
        elif path is not None:
            params["path"] = path
        else:
            return {"success": False, "message": "'path' or 'paths' parameter required."}

        response = await send_with_unity_instance(
            async_send_command_with_retry, unity_instance, "reserialize_assets", params
        )

        if isinstance(response, dict) and response.get("success"):
            return {
                "success": True,
                "message": response.get("message", "Assets reserialized."),
                "data": response.get("data"),
            }
        return response if isinstance(response, dict) else {"success": False, "message": str(response)}

    except Exception as e:
        return {"success": False, "message": f"Python error reserializing assets: {str(e)}"}
