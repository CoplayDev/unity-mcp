"""
Defines the get_action_trace tool for retrieving ActionTrace event history.

This tool provides access to the Unity editor's operation trace, allowing AI agents to:
- Review recent editor operations
- Filter events by importance, task, or conversation
- Query events since a specific sequence number
- Include semantic analysis or aggregated transaction summaries
- Use preset query modes for common scenarios (P0 feature)

Unity implementation: MCPForUnity/Editor/Tools/GetActionTraceTool.cs
C# resource: MCPForUnity/Editor/Resources/ActionTrace/ActionTraceViewResource.cs

Aligned with simplified schema (Basic, WithSemantics, Aggregated).
Removed unsupported parameters: event_types, include_payload, include_context
Added summary_only for transaction aggregation mode.
Added query_mode for P0 Agentic Workflow optimization.
"""
from typing import Annotated, Any

from fastmcp import Context
from mcp.types import ToolAnnotations

from services.registry import mcp_for_unity_tool
from services.tools import get_unity_instance_from_context
from services.tools.utils import coerce_int, coerce_bool
from transport.unity_transport import send_with_unity_instance
from transport.legacy.unity_connection import async_send_command_with_retry


@mcp_for_unity_tool(
    description="""⚠️ DETAILED HISTORY - Use when summary insufficient

Raw event list for deep analysis. Prefer summary first for speed.

Presets: 'recent_errors' | 'recent_changes' | 'summary' | 'verbose'""",
    annotations=ToolAnnotations(
        title="Get Action Trace",
    ),
)
async def get_action_trace(
    ctx: Context,
    query_mode: Annotated[str | None, "Preset mode: 'recent_errors', 'recent_changes', 'summary', 'verbose'. Reduces parameter construction burden for common scenarios."] = None,
    limit: Annotated[int | str | None, "Maximum number of events to return (1-1000, default: 50). Note: Overridden by query_mode if specified."] = 50,
    since_sequence: Annotated[int | str | None, "Only return events after this sequence number. Use for incremental queries."] = None,
    include_semantics: Annotated[bool | str | None, "Whether to include semantic analysis (importance, category, intent)."] = None,
    min_importance: Annotated[str | None, "Minimum importance level: 'low', 'medium' (default), 'high', 'critical'."] = "medium",
    summary_only: Annotated[bool | str | None, "Return aggregated transactions instead of raw events (reduces token usage)."] = None,
    task_id: Annotated[str | None, "Filter by task ID (only show events associated with this task)."] = None,
    conversation_id: Annotated[str | None, "Filter by conversation ID."] = None,
) -> dict[str, Any]:
    # Get active instance from request state
    unity_instance = get_unity_instance_from_context(ctx)

    try:
        # Prepare parameters for Unity
        params_dict: dict[str, Any] = {}

        # P0: Add query_mode first (it will override other parameters on the C# side)
        if query_mode is not None:
            params_dict["query_mode"] = str(query_mode)

        # Coerce and add optional parameters
        if limit is not None:
            coerced_limit = coerce_int(limit)
            if coerced_limit is not None:
                params_dict["limit"] = coerced_limit

        if since_sequence is not None:
            coerced_sequence = coerce_int(since_sequence)
            if coerced_sequence is not None:
                params_dict["since_sequence"] = coerced_sequence

        if include_semantics is not None:
            params_dict["include_semantics"] = coerce_bool(include_semantics, default=False)

        if min_importance is not None:
            params_dict["min_importance"] = str(min_importance)

        if summary_only is not None:
            params_dict["summary_only"] = coerce_bool(summary_only, default=False)

        if task_id is not None:
            params_dict["task_id"] = str(task_id)

        if conversation_id is not None:
            params_dict["conversation_id"] = str(conversation_id)

        # Send command to Unity
        response = await send_with_unity_instance(
            async_send_command_with_retry,
            unity_instance,
            "get_action_trace",
            params_dict,
        )

        # Preserve structured failure data; unwrap success into a friendlier shape
        if isinstance(response, dict) and response.get("success"):
            return {
                "success": True,
                "message": response.get("message", "Retrieved ActionTrace events."),
                "data": response.get("data")
            }
        return response if isinstance(response, dict) else {"success": False, "message": str(response)}

    except Exception as e:
        return {"success": False, "message": f"Python error getting action trace: {str(e)}"}
