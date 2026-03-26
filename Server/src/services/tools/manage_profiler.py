from typing import Annotated, Any

from fastmcp import Context
from mcp.types import ToolAnnotations

from services.registry import mcp_for_unity_tool
from services.tools import get_unity_instance_from_context
from transport.unity_transport import send_with_unity_instance
from transport.legacy.unity_connection import async_send_command_with_retry

PROFILER_ACTIONS = [
    "get_frame_timing",
    "get_script_timing",
    "get_physics_timing",
    "get_gc_alloc",
    "get_animation_timing",
]


@mcp_for_unity_tool(
    group="core",
    description=(
        "Read Unity Profiler counters for CPU timing, GC allocation, and animation.\n\n"
        "FRAME TIMING:\n"
        "- get_frame_timing: Main thread, render thread, CPU frame time, GPU frame time (ms)\n\n"
        "SCRIPT TIMING:\n"
        "- get_script_timing: Update, FixedUpdate, LateUpdate script execution time (ms)\n\n"
        "PHYSICS TIMING:\n"
        "- get_physics_timing: Physics.Processing, Physics.FetchResults time (ms)\n\n"
        "GC ALLOCATION:\n"
        "- get_gc_alloc: GC allocation bytes and count per frame\n\n"
        "ANIMATION TIMING:\n"
        "- get_animation_timing: Animator.Update time (ms)"
    ),
    annotations=ToolAnnotations(
        title="Manage Profiler",
        destructiveHint=False,
        readOnlyHint=True,
    ),
)
async def manage_profiler(
    ctx: Context,
    action: Annotated[str, "The profiler action to perform."],
) -> dict[str, Any]:
    action_lower = action.lower()
    if action_lower not in PROFILER_ACTIONS:
        return {
            "success": False,
            "message": f"Unknown action '{action}'. Valid actions: {', '.join(PROFILER_ACTIONS)}",
        }

    unity_instance = await get_unity_instance_from_context(ctx)

    result = await send_with_unity_instance(
        async_send_command_with_retry, unity_instance, "manage_profiler", {"action": action_lower}
    )
    return result if isinstance(result, dict) else {"success": False, "message": str(result)}
