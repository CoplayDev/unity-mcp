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
        "Actions: get_stats (single-frame snapshot: draw calls, batches, FPS, cpuMainMs), "
        "get_memory (allocated/reserved/mono/graphics memory), "
        "get_profiler (frame timing, time scale, system info), "
        "get_stats_aggregated (N-frame aggregated: min/max/avg/p50/p95 for FPS, CPU, draw calls — "
        "uses long-lived ProfilerRecorders, much more reliable than single snapshots. "
        "Param: frames=number of recent frames to aggregate, 0=all available), "
        "get_system_stats (per-DOTS-system CPU breakdown sorted by cost — "
        "shows which systems consume the most frame budget. Param: top_n=number of systems), "
        "get_session_report (full Play session report from start to stop — "
        "includes Markdown summary, JSON timeline, CSV. Params: include_timeline=bool, include_csv=bool), "
        "list_sessions (list saved session files from Logs/PerfSessions/ — works anytime), "
        "load_session (load a saved session JSON by filename), "
        "analyze_session (analyze a saved session: bottleneck detection, system ranking, issues. "
        "Param: filename=session JSON file). "
        "Aggregated/system/session actions require Play mode; list/load/analyze work anytime."
    ),
    annotations=ToolAnnotations(
        title="Rendering Stats",
    ),
)
async def rendering_stats(
    ctx: Context,
    action: Annotated[
        Literal[
            "get_stats", "get_memory", "get_profiler",
            "get_stats_aggregated", "get_system_stats", "get_session_report",
            "list_sessions", "load_session", "analyze_session"
        ],
        "Action to perform. get_stats=single snapshot, get_stats_aggregated=N-frame percentiles, "
        "get_system_stats=per-system CPU breakdown, get_session_report=full session timeline+summary, "
        "list_sessions=list saved sessions, load_session=load session JSON, analyze_session=bottleneck analysis."
    ],
    frames: Annotated[int | None, "For get_stats_aggregated: number of recent frames (0=all)."] = None,
    top_n: Annotated[int | None, "For get_system_stats: number of top systems to return."] = None,
    include_timeline: Annotated[bool | None, "For get_session_report: include JSON timeline."] = True,
    include_csv: Annotated[bool | None, "For get_session_report: include CSV data."] = True,
    filename: Annotated[str | None, "For load_session/analyze_session: session JSON filename."] = None,
) -> dict[str, Any]:
    unity_instance = get_unity_instance_from_context(ctx)

    params: dict[str, Any] = {"action": action}
    if frames is not None:
        params["frames"] = frames
    if top_n is not None:
        params["top_n"] = top_n
    if include_timeline is not None:
        params["include_timeline"] = include_timeline
    if include_csv is not None:
        params["include_csv"] = include_csv
    if filename is not None:
        params["filename"] = filename

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
