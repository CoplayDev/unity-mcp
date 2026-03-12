"""
Tool for Unity VideoPlayer — inspect, control playback, set URL/clip.
Actions: list_players, get_player, set_player, play, pause, stop, set_time.
Uses built-in UnityEngine.Video — no package dependency.
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
        "Unity VideoPlayer operations. "
        "Actions: list_players (all VideoPlayer components), "
        "get_player (URL/clip, playback state, time, length), "
        "set_player (modify source, playback speed, loop, audio output), "
        "play (play video), "
        "pause (pause video), "
        "stop (stop video), "
        "set_time (seek to time). "
        "Uses built-in UnityEngine.Video — no package dependency."
    ),
    annotations=ToolAnnotations(title="Manage Video"),
)
async def manage_video(
    ctx: Context,
    action: Annotated[
        Literal[
            "list_players", "get_player", "set_player",
            "play", "pause", "stop", "set_time"
        ],
        "Action to perform on Unity VideoPlayer."
    ],
    target: Annotated[str, "GameObject name or instance ID with VideoPlayer"] | None = None,
    properties: Annotated[str, "JSON object with properties to set (for set_player)"] | None = None,
    time: Annotated[float, "Time in seconds (for set_time)"] | None = None,
    page_size: Annotated[int, "Max results to return (default 50)"] | None = None,
    cursor: Annotated[int, "Pagination cursor (0-based offset)"] | None = None,
) -> dict[str, Any]:
    unity_instance = get_unity_instance_from_context(ctx)

    params = {"action": action, "target": target, "properties": properties,
              "time": time, "page_size": page_size, "cursor": cursor}
    params = {k: v for k, v in params.items() if v is not None}

    try:
        response = await send_with_unity_instance(
            async_send_command_with_retry, unity_instance, "manage_video", params)
        if isinstance(response, dict) and response.get("success"):
            return {"success": True, "message": response.get("message", "OK"), "data": response.get("data")}
        return response if isinstance(response, dict) else {"success": False, "message": str(response)}
    except Exception as e:
        return {"success": False, "message": f"Python error managing video: {str(e)}"}
