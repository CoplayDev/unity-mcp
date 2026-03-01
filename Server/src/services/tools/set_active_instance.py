from typing import Annotated, Any

from fastmcp import Context
from mcp.types import ToolAnnotations

from services.registry import mcp_for_unity_tool
from transport.unity_instance_middleware import get_unity_instance_middleware


@mcp_for_unity_tool(
    unity_target=None,
    description="Set the active Unity instance for this client/session. Accepts Name@hash, hash prefix, or port number (stdio only).",
    annotations=ToolAnnotations(
        title="Set Active Instance",
    ),
)
async def set_active_instance(
        ctx: Context,
        instance: Annotated[str, "Target instance (Name@hash, hash prefix, or port number in stdio mode)"]
) -> dict[str, Any]:
    value = (instance or "").strip()
    if not value:
        return {
            "success": False,
            "error": "Instance identifier is required. "
                     "Use mcpforunity://instances to copy a Name@hash or provide a hash prefix."
        }
    middleware = get_unity_instance_middleware()
    try:
        resolved_id = await middleware._resolve_instance_value(value, ctx)
    except ValueError as exc:
        return {
            "success": False,
            "error": str(exc),
        }

    # Store selection in middleware (session-scoped)
    # We use middleware.set_active_instance to persist the selection.
    # The session key is an internal detail but useful for debugging response.
    middleware.set_active_instance(ctx, resolved_id)
    session_key = middleware.get_session_key(ctx)

    return {
        "success": True,
        "message": f"Active instance set to {resolved_id}",
        "data": {
            "instance": resolved_id,
            "session_key": session_key,
        },
    }
