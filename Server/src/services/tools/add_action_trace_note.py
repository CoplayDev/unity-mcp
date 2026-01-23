"""
Defines the add_action_trace_note tool for AI agents to record comments to the ActionTrace.

This enables multi-agent collaboration and task-level tracking:
- task_id: Groups notes from a single task across multiple tool calls
- conversation_id: Tracks continuity across sessions
- agent_id: Identifies which AI wrote the note
- related_sequences: Links notes to specific ActionTrace events

Unity implementation: MCPForUnity/Editor/Tools/AddActionTraceNoteTool.cs
"""
import json
import uuid
from contextvars import ContextVar
from typing import Annotated, Any

from fastmcp import Context
from mcp.types import ToolAnnotations

from services.registry import mcp_for_unity_tool
from services.tools import get_unity_instance_from_context
from services.tools.utils import coerce_bool
from transport.unity_transport import send_with_unity_instance
from transport.legacy.unity_connection import async_send_command_with_retry

# Context variables for task/conversation tracking
# These can be set at the session level to automatically apply to all notes
_current_task_id: ContextVar[str] = ContextVar('current_task_id', default='')
_current_conversation_id: ContextVar[str] = ContextVar('current_conversation_id', default='')


def _coerce_int_list(value) -> list[int] | None:
    """
    Convert string or list to list of integers.

    Handles MCP protocol serialization where arrays may be passed as JSON strings.
    Compatible with the pattern used in run_tests.py.
    """
    if value is None:
        return None
    if isinstance(value, list):
        # Filter and convert to int
        result = []
        for v in value:
            if v is not None:
                try:
                    result.append(int(v))
                except (ValueError, TypeError):
                    pass  # Skip non-integer values
        return result if result else None
    if isinstance(value, str):
        if not value.strip():
            return None
        # Try parsing JSON array first
        if value.startswith("["):
            try:
                parsed = json.loads(value)
                if isinstance(parsed, list):
                    return _coerce_int_list(parsed)  # Recursively handle parsed list
            except json.JSONDecodeError:
                pass
        # Try comma-delimited integers
        try:
            return [int(v.strip()) for v in value.split(",") if v.strip()]
        except ValueError:
            pass
    return None


@mcp_for_unity_tool(
    description="""✍️ DOCUMENT YOUR WORK - Helps future AI understand context

Record decisions, workarounds, completed tasks (>3 steps).

✓ Good: "Refactored PlayerController: speed 5→8, fixed collision"
✗ Bad: "Created cube" (ActionTrace already records this)""",
    annotations=ToolAnnotations(
        title="Add Action Trace Note",
    ),
)
async def add_action_trace_note(
    ctx: Context,
    note: Annotated[str, "The note text to record. Should be concise and informative (e.g., 'Completed player movement system refactor, increased speed from 5 to 8')."],
    intent: Annotated[str, "The intent/category of the note (e.g., 'refactoring', 'bugfix', 'feature', 'optimization')."] | None = None,
    agent_id: Annotated[str, "Identifier for the AI agent writing the note (e.g., 'claude-opus-4.5', 'gpt-4'). Defaults to 'unknown'."] | None = None,
    agent_model: Annotated[str, "Model version (optional, for detailed tracking)."] | None = None,
    task_id: Annotated[str, "Task-level identifier. Groups all notes from a single task. If not specified, will be auto-generated or retrieved from context."] | None = None,
    conversation_id: Annotated[str, "Conversation/session identifier for cross-session tracking. If not specified, will be auto-generated or retrieved from context."] | None = None,
    related_sequences: Annotated[list[int] | str, "List of ActionTrace event sequence numbers to link this note to (optional). Accepts array or JSON string (e.g., '[3000, 3001]' or comma-separated '3000, 3001')."] | None = None,
) -> dict[str, Any]:
    """
    Add an AI comment to the ActionTrace.

    This tool enables AI agents to record summaries, decisions, or task completion notes.
    Notes are recorded as AINote events with critical importance (1.0).

    Multi-Agent Collaboration:
    - Use task_id to group notes from multiple agents working on the same task
    - Example: Claude does refactoring (task_id='refactor-player'), GPT-4 adds tests (same task_id)

    Context Management:
    - task_id and conversation_id can be set via context variables at session start
    - If not provided in parameters, they will be automatically retrieved from context
    - If context is empty, new IDs will be auto-generated

    Use Cases:
    1. Task completion: "Completed player movement system refactor, speed increased from 5 to 8"
    2. Decision recording: "Decided to use object pool pattern for bullet management"
    3. Bug explanation: "Fixed lightmap not updating at runtime issue"
    4. Design rationale: "Using State Machine instead of simple enum for easier state extension"

    Returns:
        Success response with recorded sequence number and metadata.
    """
    # Get active instance from request state
    unity_instance = get_unity_instance_from_context(ctx)

    # Retrieve from context if not explicitly provided
    effective_task_id = task_id or _current_task_id.get()
    effective_conv_id = conversation_id or _current_conversation_id.get()

    # Auto-generate if still empty
    if not effective_task_id:
        effective_task_id = f"task-{uuid.uuid4().hex[:8]}"
    if not effective_conv_id:
        effective_conv_id = f"conv-{uuid.uuid4().hex[:8]}"

    # P0 Fix: Persist auto-generated IDs back to context for future calls
    # This ensures task-level grouping works across multiple note additions
    if not task_id:
        _current_task_id.set(effective_task_id)
    if not conversation_id:
        _current_conversation_id.set(effective_conv_id)

    # Prepare parameters for Unity
    params_dict: dict[str, Any] = {
        "note": note,
        "agent_id": agent_id or "unknown",
        "task_id": effective_task_id,
        "conversation_id": effective_conv_id,
    }

    # Optional fields
    if intent:
        params_dict["intent"] = intent

    if agent_model:
        params_dict["agent_model"] = agent_model

    # Coerce related_sequences to list of integers (handles MCP string serialization)
    coerced_related_sequences = _coerce_int_list(related_sequences)
    if coerced_related_sequences:
        params_dict["related_sequences"] = coerced_related_sequences

    # Send command to Unity
    response = await send_with_unity_instance(
        async_send_command_with_retry,
        unity_instance,
        "add_action_trace_note",
        params_dict,
    )

    return response if isinstance(response, dict) else {"success": False, "message": str(response)}


def set_task_context(task_id: str, conversation_id: str = "") -> None:
    """
    Set the current task/conversation context for all subsequent action trace notes.

    This is useful for batch operations where multiple tool calls belong to the same task.

    Args:
        task_id: Task identifier (e.g., 'refactor-player-movement')
        conversation_id: Optional conversation identifier (auto-generated if empty)

    Example:
        ```python
        # Set context at start of task
        set_task_context("refactor-player", "session-2024-01-15")

        # All subsequent notes will automatically use these IDs
        await add_action_trace_note(ctx, note="Step 1: Modified PlayerController")
        await add_action_trace_note(ctx, note="Step 2: Increased movement speed to 8")
        ```
    """
    _current_task_id.set(task_id)
    if conversation_id:
        _current_conversation_id.set(conversation_id)
    else:
        _current_conversation_id.set(f"conv-{uuid.uuid4().hex[:8]}")


def get_task_context() -> tuple[str, str]:
    """
    Get the current task/conversation context.

    Returns:
        Tuple of (task_id, conversation_id)
    """
    return _current_task_id.get(), _current_conversation_id.get()
