"""
Tool for reading Unity rendering statistics, memory usage, and profiler data.
Most useful during Play mode when the rendering pipeline is active.
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
        "Read Unity rendering and performance statistics. "
        "Actions: get_stats (draw calls, batches, triangles, vertices, FPS, "
        "setPassCalls, shadowCasters, texture memory, batching breakdown), "
        "get_memory (total allocated/reserved, mono heap, graphics driver memory), "
        "get_profiler (frame timing, time scale, system info incl. GPU/CPU). "
        "Most stats require Play mode with Game view visible."
    ),
    annotations=ToolAnnotations(
        title="Rendering Stats",
    ),
)
async def rendering_stats(
    ctx: Context,
    action: Annotated[
        Literal["get_stats", "get_memory", "get_profiler"],
        "Action to perform. get_stats=rendering counters, get_memory=memory usage, get_profiler=frame timing and system info."
    ],
) -> dict[str, Any]:
    unity_instance = get_unity_instance_from_context(ctx)

    params = {"action": action}

    try:
        response = await send_with_unity_instance(
            async_send_command_with_retry, unity_instance,
            "rendering_stats", params
        )

        if isinstance(response, dict) and response.get("success"):
            return {
                "success": True,
                "message": response.get("message", "Stats captured."),
                "data": response.get("data"),
            }
        return response if isinstance(response, dict) else {"success": False, "message": str(response)}

    except Exception as e:
        return {"success": False, "message": f"Python error reading rendering stats: {str(e)}"}
