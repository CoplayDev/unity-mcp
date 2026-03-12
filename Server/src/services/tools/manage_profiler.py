"""
Tool for Unity Profiler — counters, categories, recording, memory snapshots.
Actions: get_counters, list_categories, start_recording, stop_recording,
         get_frame_data, get_memory_snapshot.
Uses built-in Unity.Profiling — no package dependency.
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
        "Unity Profiler operations. "
        "Actions: get_counters (read named profiler counters), "
        "list_categories (available ProfilerCategory names), "
        "start_recording (begin profiler recording to file), "
        "stop_recording (stop recording), "
        "get_frame_data (last N frames timing data), "
        "get_memory_snapshot (detailed memory breakdown). "
        "Uses built-in Unity.Profiling — no package dependency."
    ),
    annotations=ToolAnnotations(title="Manage Profiler"),
)
async def manage_profiler(
    ctx: Context,
    action: Annotated[
        Literal[
            "get_counters", "list_categories", "start_recording",
            "stop_recording", "get_frame_data", "get_memory_snapshot"
        ],
        "Action to perform on Unity Profiler."
    ],
    counters: Annotated[str, "Comma-separated counter names (for get_counters)"] | None = None,
    path: Annotated[str, "File path for profiler recording output"] | None = None,
    count: Annotated[int, "Number of frames to retrieve (for get_frame_data)"] | None = None,
) -> dict[str, Any]:
    unity_instance = get_unity_instance_from_context(ctx)

    params = {"action": action, "counters": counters, "path": path, "count": count}
    params = {k: v for k, v in params.items() if v is not None}

    try:
        response = await send_with_unity_instance(
            async_send_command_with_retry, unity_instance, "manage_profiler", params)
        if isinstance(response, dict) and response.get("success"):
            return {"success": True, "message": response.get("message", "OK"), "data": response.get("data")}
        return response if isinstance(response, dict) else {"success": False, "message": str(response)}
    except Exception as e:
        return {"success": False, "message": f"Python error managing profiler: {str(e)}"}
