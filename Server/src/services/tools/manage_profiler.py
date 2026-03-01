from typing import Annotated, Any, Literal

from fastmcp import Context
from mcp.types import ToolAnnotations

from services.registry import mcp_for_unity_tool
from services.tools import get_unity_instance_from_context
from transport.unity_transport import send_with_unity_instance
from transport.legacy.unity_connection import async_send_command_with_retry


@mcp_for_unity_tool(
    description="Reads Unity Profiler frame data and controls profiler state. "
    "Uses ProfilerDriver to access the same data visible in the Profiler window. "
    "Read-only actions: read_frames, status. "
    "Modifying actions: enable, disable, clear.",
    annotations=ToolAnnotations(
        title="Manage Profiler",
    ),
)
async def manage_profiler(
    ctx: Context,
    action: Annotated[
        Literal["read_frames", "enable", "disable", "status", "clear"],
        "Action to perform. "
        "'read_frames' reads profiler frame data (same as Profiler window). "
        "'enable' starts profiler recording. "
        "'disable' stops profiler recording. "
        "'status' returns current profiler state. "
        "'clear' clears all recorded frames."
    ],
    frame_count: Annotated[
        int | str,
        "Number of recent frames to read (default: 1). Only used with 'read_frames'."
    ] | None = None,
    thread: Annotated[
        int | str,
        "Thread index to read (default: 0). "
        "0=Main Thread (game logic, ECS, physics, animation), "
        "1=Render Thread (GPU commands, shader), "
        "2+=Job Worker Threads (Burst/Jobs parallel work). "
        "Only used with 'read_frames'."
    ] | None = None,
    filter: Annotated[
        str,
        "Filter samples by name (case-insensitive substring match). Only used with 'read_frames'."
    ] | None = None,
    min_ms: Annotated[
        float | str,
        "Minimum milliseconds to include a sample (default: 0.01). Only used with 'read_frames'."
    ] | None = None,
) -> dict[str, Any]:
    unity_instance = get_unity_instance_from_context(ctx)

    try:
        params: dict[str, Any] = {"action": action}

        if action == "read_frames":
            if frame_count is not None:
                params["frameCount"] = int(frame_count)
            if thread is not None:
                params["thread"] = int(thread)
            if filter is not None:
                params["filter"] = filter
            if min_ms is not None:
                params["minMs"] = float(min_ms)

        response = await send_with_unity_instance(
            async_send_command_with_retry, unity_instance, "manage_profiler", params
        )

        if isinstance(response, dict) and response.get("success"):
            return {
                "success": True,
                "message": response.get("message", "Profiler operation successful."),
                "data": response.get("data"),
            }
        return response if isinstance(response, dict) else {"success": False, "message": str(response)}

    except Exception as e:
        return {"success": False, "message": f"Python error managing profiler: {str(e)}"}
