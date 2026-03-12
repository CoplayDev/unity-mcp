"""
Tool for Unity cameras — inspection, modification, screen/world conversions, render to file.
Actions: list_cameras, get_camera, set_camera, render_to_file,
         world_to_screen, screen_to_ray, get_main_camera.
Uses built-in UnityEngine.Camera — no package dependency.
"""
from typing import Annotated, Any, Literal

from fastmcp import Context
from mcp.types import ToolAnnotations

from services.registry import mcp_for_unity_tool
from services.tools import get_unity_instance_from_context
from transport.unity_transport import send_with_unity_instance
from transport.legacy.unity_connection import async_send_command_with_retry


@mcp_for_unity_tool(
    description=(
        "Unity camera management and coordinate conversions. "
        "Actions: list_cameras (all cameras with depth, culling mask), "
        "get_camera (full camera properties), "
        "set_camera (modify FOV, near/far, clear flags), "
        "render_to_file (render camera view to PNG — Play mode), "
        "world_to_screen (world point to screen coordinates), "
        "screen_to_ray (screen point to world ray), "
        "get_main_camera (quick access to Camera.main info). "
        "Uses built-in UnityEngine.Camera — no package dependency."
    ),
    annotations=ToolAnnotations(
        title="Manage Camera",
    ),
)
async def manage_camera(
    ctx: Context,
    action: Annotated[
        Literal[
            "list_cameras", "get_camera", "set_camera",
            "render_to_file", "world_to_screen", "screen_to_ray",
            "get_main_camera"
        ],
        "Action to perform on Unity cameras."
    ],
    # Target
    target: Annotated[str, "Camera GameObject name or instance ID"] | None = None,
    # Properties
    properties: Annotated[str, "JSON object of properties to set"] | None = None,
    # Render params
    path: Annotated[str, "Output file path (for render_to_file)"] | None = None,
    width: Annotated[int, "Render width in pixels (default 1920)"] | None = None,
    height: Annotated[int, "Render height in pixels (default 1080)"] | None = None,
    # Coordinate params
    position: Annotated[str, "Position as 'x,y,z' (world) or 'x,y' (screen)"] | None = None,
    # Pagination
    page_size: Annotated[int, "Max results to return (default 50)"] | None = None,
    cursor: Annotated[int, "Pagination cursor (0-based offset)"] | None = None,
) -> dict[str, Any]:
    unity_instance = get_unity_instance_from_context(ctx)

    params = {
        "action": action,
        "target": target,
        "properties": properties,
        "path": path,
        "width": width,
        "height": height,
        "position": position,
        "page_size": page_size,
        "cursor": cursor,
    }
    params = {k: v for k, v in params.items() if v is not None}

    try:
        response = await send_with_unity_instance(
            async_send_command_with_retry, unity_instance, "manage_camera", params
        )

        if isinstance(response, dict) and response.get("success"):
            return {
                "success": True,
                "message": response.get("message", "Camera operation successful."),
                "data": response.get("data"),
            }
        return response if isinstance(response, dict) else {"success": False, "message": str(response)}

    except Exception as e:
        return {"success": False, "message": f"Python error managing camera: {str(e)}"}
