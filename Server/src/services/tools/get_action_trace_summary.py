"""
Defines the get_action_trace_summary tool for AI-friendly aggregated summaries.

This is a P0 feature for Agentic Workflow - provides compressed, structured
summaries instead of hundreds of individual events. Reduces token usage and
improves AI context understanding.

Core benefits:
- Token efficiency: 100 events -> ~200 tokens (vs ~5000 tokens raw)
- Pattern recognition: Built-in anomaly detection (excessive modifications, high error rate)
- Action guidance: Suggested next steps based on detected patterns

Unity implementation: MCPForUnity/Editor/Tools/GetActionTraceSummaryTool.cs
"""
from typing import Annotated, Any

from fastmcp import Context
from mcp.types import ToolAnnotations

from services.registry import mcp_for_unity_tool
from services.tools import get_unity_instance_from_context
from services.tools.utils import coerce_int
from transport.unity_transport import send_with_unity_instance
from transport.legacy.unity_connection import async_send_command_with_retry


@mcp_for_unity_tool(
    description="Get AI-friendly summary of recent ActionTrace events. Returns categorized changes, warnings, and suggested actions to reduce token usage and improve context understanding.",
    annotations=ToolAnnotations(
        title="Get Action Trace Summary",
    ),
)
async def get_action_trace_summary(
    ctx: Context,
    time_range: Annotated[str, "Time window: '5m', '15m', '1h', 'today' (default: '1h')"] = "1h",
    limit: Annotated[int | str | None, "Maximum events to analyze (1-500, default: 200)"] = None,
    task_id: Annotated[str | None, "Filter by task ID (for multi-agent scenarios)"] = None,
    conversation_id: Annotated[str | None, "Filter by conversation ID"] = None,
    min_importance: Annotated[str | None, "Minimum importance level: 'low', 'medium', 'high', 'critical'"] = None,
) -> dict[str, Any]:
    """
    Get a structured summary of ActionTrace events.

    This tool is optimized for AI agents. Instead of processing hundreds of
    individual events, the AI receives:
    - summary: Human-readable description of what happened
    - categories: Counts by type, importance, and operation
    - top_targets: Most active objects
    - errors: Critical events that need attention
    - warnings: Detected anomalies (e.g., excessive modifications)
    - suggested_actions: Recommended next steps

    Preset Modes (time_range):
    - '5m': Last 5 minutes - recent activity
    - '15m': Last 15 minutes - medium timeframe
    - '1h': Last hour - default overview
    - 'today': Last 24 hours - daily summary

    Use Cases:
    1. Quick status check: "What happened recently?"
       -> get_action_trace_summary(time_range='15m')

    2. Error investigation: "Are there any errors?"
       -> get_action_trace_summary(min_importance='high')

    3. Task review: "What did I accomplish in this task?"
       -> get_action_trace_summary(task_id='task-abc123')

    Returns:
        On success:
        - time_range: The time window analyzed
        - summary: Human-readable description (e.g., "Created 3 objects, modified 12 properties")
        - categories: {total_count, created_count, modified_count, deleted_count, error_count, by_type, by_importance}
        - top_targets: Most active objects with their operation counts
        - errors: List of critical events (up to 5)
        - warnings: Detected anomalies (up to 3)
        - suggested_actions: Recommended next steps
        - current_sequence: Latest sequence number

    Example:
        response = await get_action_trace_summary(ctx, time_range='5m')
        # Returns: {"summary": "Created 2 objects, modified 5 properties", ...}
    """
    # Get active instance from request state
    unity_instance = get_unity_instance_from_context(ctx)

    # Prepare parameters for Unity
    params_dict: dict[str, Any] = {
        "time_range": str(time_range),
    }

    # Coerce and add optional parameters
    if limit is not None:
        coerced_limit = coerce_int(limit)
        if coerced_limit is not None:
            params_dict["limit"] = coerced_limit

    if task_id is not None:
        params_dict["task_id"] = str(task_id)

    if conversation_id is not None:
        params_dict["conversation_id"] = str(conversation_id)

    if min_importance is not None:
        params_dict["min_importance"] = str(min_importance)

    # Send command to Unity
    response = await send_with_unity_instance(
        async_send_command_with_retry,
        unity_instance,
        "get_action_trace_summary",
        params_dict,
    )

    # Preserve structured failure data; unwrap success into a friendlier shape
    if isinstance(response, dict) and response.get("success"):
        return {
            "success": True,
            "message": response.get("message", "Generated ActionTrace summary."),
            "data": response.get("data")
        }
    return response if isinstance(response, dict) else {"success": False, "message": str(response)}
