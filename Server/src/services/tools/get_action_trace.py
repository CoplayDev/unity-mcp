"""
Defines the get_action_trace tool for retrieving ActionTrace event history.

This tool provides access to the Unity editor's operation trace, allowing AI agents to:
- Review recent editor operations
- Filter events by type, importance, or context
- Query events since a specific sequence number
- Include semantic analysis and context associations

Unity implementation: MCPForUnity/Editor/Tools/GetActionTraceTool.cs
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
    description="Retrieve ActionTrace event history from Unity editor. Provides access to recent operations, filtered by type, importance, or sequence range. Use this to review what has been done in the editor session.",
    annotations=ToolAnnotations(
        title="Get Action Trace",
    ),
)
async def get_action_trace(
    ctx: Context,
    limit: Annotated[int | str | None, "Maximum number of events to return (1-1000, default: 50)."] = None,
    since_sequence: Annotated[int | str | None, "Only return events after this sequence number. Use for incremental queries."] = None,
    event_types: Annotated[list[str] | str | None, "Filter by event types (e.g., ['SceneOpened', 'AssetImported', 'GameObjectCreated'])."] = None,
    include_payload: Annotated[bool | str | None, "Whether to include full event payload (default: true)."] = None,
    include_context: Annotated[bool | str | None, "Whether to include context associations (default: false)."] = None,
    include_semantics: Annotated[bool | str | None, "Whether to include semantic analysis results (default: false)."] = None,
    min_importance: Annotated[str | None, "Minimum importance level: 'critical', 'high', 'medium' (default), 'low', 'all'."] = None,
    task_id: Annotated[str | None, "Filter by task ID (only show events associated with this task)."] = None,
    conversation_id: Annotated[str | None, "Filter by conversation ID."] = None,
) -> dict[str, Any]:
    # Get active instance from request state
    unity_instance = get_unity_instance_from_context(ctx)

    try:
        # Prepare parameters for Unity
        params_dict: dict[str, Any] = {}

        # Coerce and add optional parameters
        if limit is not None:
            coerced_limit = coerce_int(limit)
            if coerced_limit is not None:
                params_dict["Limit"] = coerced_limit

        if since_sequence is not None:
            coerced_sequence = coerce_int(since_sequence)
            if coerced_sequence is not None:
                params_dict["SinceSequence"] = coerced_sequence

        if event_types is not None:
            # Handle both string and list input
            if isinstance(event_types, str):
                # If it's a string, treat as a single type or parse as JSON array
                import json
                try:
                    parsed = json.loads(event_types)
                    if isinstance(parsed, list):
                        event_types = parsed
                    else:
                        event_types = [event_types]
                except (json.JSONDecodeError, TypeError):
                    # Not JSON, treat as single type
                    event_types = [event_types]

            if isinstance(event_types, list) and event_types:
                params_dict["EventTypes"] = event_types

        if include_payload is not None:
            params_dict["IncludePayload"] = coerce_bool(include_payload, default=True)

        if include_context is not None:
            params_dict["IncludeContext"] = coerce_bool(include_context, default=False)

        if include_semantics is not None:
            params_dict["IncludeSemantics"] = coerce_bool(include_semantics, default=False)

        if min_importance is not None:
            params_dict["MinImportance"] = str(min_importance)

        if task_id is not None:
            params_dict["TaskId"] = str(task_id)

        if conversation_id is not None:
            params_dict["ConversationId"] = str(conversation_id)

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
