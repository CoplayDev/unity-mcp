"""
Tool for Unity Timeline — PlayableDirector, tracks, clips, playback control.
Actions: list_directors, get_director, play, pause, stop, set_time, list_tracks, get_bindings.
Requires com.unity.timeline package.
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
        "Unity Timeline management. "
        "Actions: list_directors (all PlayableDirector components), "
        "get_director (state, time, duration, wrap mode), "
        "play/pause/stop (control playback), "
        "set_time (seek to time), "
        "list_tracks (tracks in timeline asset), "
        "get_bindings (track-to-object bindings). "
        "Requires com.unity.timeline package."
    ),
    annotations=ToolAnnotations(title="Manage Timeline"),
)
async def manage_timeline(
    ctx: Context,
    action: Annotated[
        Literal[
            "list_directors", "get_director", "play", "pause", "stop",
            "set_time", "list_tracks", "get_bindings"
        ],
        "Action to perform on Unity Timeline."
    ],
    target: Annotated[str, "GameObject name or instance ID"] | None = None,
    time: Annotated[float, "Time in seconds (for set_time)"] | None = None,
    page_size: Annotated[int, "Max results to return (default 50)"] | None = None,
    cursor: Annotated[int, "Pagination cursor (0-based offset)"] | None = None,
) -> dict[str, Any]:
    unity_instance = get_unity_instance_from_context(ctx)

    params = {"action": action, "target": target, "time": time,
              "page_size": page_size, "cursor": cursor}
    params = {k: v for k, v in params.items() if v is not None}

    try:
        response = await send_with_unity_instance(
            async_send_command_with_retry, unity_instance, "manage_timeline", params)
        if isinstance(response, dict) and response.get("success"):
            return {"success": True, "message": response.get("message", "OK"), "data": response.get("data")}
        return response if isinstance(response, dict) else {"success": False, "message": str(response)}
    except Exception as e:
        return {"success": False, "message": f"Python error managing timeline: {str(e)}"}
