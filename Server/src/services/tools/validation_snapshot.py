"""
Aggregates all runtime validation data into a single response.
Replaces 15-20 individual MCP calls with 1-2 calls (capture + compare).
Actions: capture, compare.
Requires com.unity.entities package and DOTSRPG components in the Unity project.
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
        "Capture a full runtime validation snapshot in ONE call — entity counts "
        "(total/alive/dead/by-team), health distribution (min/max/mean), position "
        "samples, NaN bounds check, rendering stats (FPS/draw calls/batches), "
        "battle state, console errors, editor state. "
        "Use 'compare' to diff two snapshots and detect movement, anomalies, deltas. "
        "Requires com.unity.entities + DOTSRPG components. Play mode required for entity data."
    ),
    annotations=ToolAnnotations(
        title="Validation Snapshot",
    ),
)
async def validation_snapshot(
    ctx: Context,
    action: Annotated[
        Literal["capture", "compare"],
        "Action: 'capture' collects all validation data, 'compare' diffs two snapshots."
    ],
    sample_size: Annotated[
        int,
        "Number of entity positions to sample (default 20, max 100). For 'capture' only."
    ] | None = None,
    snapshot_a: Annotated[
        dict[str, Any] | str,
        "Previous snapshot JSON (for 'compare' action)."
    ] | None = None,
    snapshot_b: Annotated[
        dict[str, Any] | str,
        "Current snapshot JSON (for 'compare' action)."
    ] | None = None,
) -> dict[str, Any]:
    unity_instance = get_unity_instance_from_context(ctx)

    params = {
        "action": action,
        "sample_size": sample_size,
        "snapshot_a": snapshot_a,
        "snapshot_b": snapshot_b,
    }
    params = {k: v for k, v in params.items() if v is not None}

    try:
        response = await send_with_unity_instance(
            async_send_command_with_retry, unity_instance, "validation_snapshot", params
        )

        if isinstance(response, dict) and response.get("success"):
            return {
                "success": True,
                "message": response.get("message", "Validation snapshot successful."),
                "data": response.get("data"),
            }
        return response if isinstance(response, dict) else {"success": False, "message": str(response)}

    except Exception as e:
        return {"success": False, "message": f"Python error in validation_snapshot: {str(e)}"}
