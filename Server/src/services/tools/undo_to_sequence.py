"""
Defines the undo_to_sequence tool for reverting Unity editor state to a specific ActionTrace sequence.

This tool provides "regret medicine" for AI operations - allowing the editor state
to be reverted to a previous point identified by an ActionTrace sequence number.

Unity implementation: MCPForUnity/Editor/Tools/UndoToSequenceTool.cs
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
    description="Reverts the Unity editor state to a specific ActionTrace sequence number. This provides a way to 'undo' to a previous state. Use dry_run=true first to preview how many Undo steps will be performed. Warning: Redo (Ctrl+Y) may not work correctly after this operation.",
    annotations=ToolAnnotations(
        title="Undo to Sequence",
    ),
)
async def undo_to_sequence(
    ctx: Context,
    sequence_id: Annotated[int | str, "Target sequence number to revert to. Must be a past sequence number (less than current sequence)."],
    dry_run: Annotated[bool | str, "If true, only calculate steps without executing. Use this to preview the undo operation before actually performing it."] | None = None,
) -> dict[str, Any]:
    """
    Undo Unity editor state to a specific ActionTrace sequence.

    This tool allows reverting the editor to a previous state by:
    1. Finding all events after the target sequence
    2. Counting unique Undo groups in that range
    3. Performing multiple Undo operations to reach the target state

    Parameters:
        sequence_id: Target sequence number to revert to (required)
        dry_run: If true, only return information without executing (default: false)

    Returns:
        On success:
        - target_sequence: The sequence number reverted to
        - current_sequence: The current sequence before undo
        - undo_steps_needed/performed: Number of Undo operations
        - events_after_target: Number of events that will be/have been undone
        - affected_undo_groups: Array of Undo group IDs (dry_run only)
        - note: Warning about Redo behavior

    Limitations:
        - Only works if Undo history is intact (no domain reload)
        - Cannot redo after this operation (standard Ctrl+Y won't work)
        - Best used immediately after realizing a mistake

    Example:
        # First, check current sequence
        result = await get_actionTrace(ctx, limit=1)
        current_seq = result["items"][0]["sequence"]

        # Preview undo to 100 steps ago
        await undo_to_sequence(ctx, sequence_id=current_seq - 100, dry_run=True)

        # Actually perform the undo
        await undo_to_sequence(ctx, sequence_id=current_seq - 100)
    """
    # Get active instance from request state
    unity_instance = get_unity_instance_from_context(ctx)

    # Coerce sequence_id parameter (required)
    coerced_sequence = coerce_int(sequence_id)
    if coerced_sequence is None:
        return {
            "success": False,
            "message": "sequence_id parameter is required and must be a number."
        }

    # Coerce dry_run parameter (optional)
    dry_run = coerce_bool(dry_run, default=False)

    # Prepare parameters for the C# handler
    params_dict: dict[str, Any] = {
        "sequence_id": coerced_sequence,
    }

    if dry_run:
        params_dict["dry_run"] = True

    # Send command to Unity
    response = await send_with_unity_instance(
        async_send_command_with_retry,
        unity_instance,
        "undo_to_sequence",
        params_dict,
    )

    return response if isinstance(response, dict) else {"success": False, "message": str(response)}
