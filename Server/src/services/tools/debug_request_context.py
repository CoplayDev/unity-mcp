from typing import Any

from fastmcp import Context
from services.registry import mcp_for_unity_tool
from transport.unity_instance_middleware import get_unity_instance_middleware


@mcp_for_unity_tool(
    description="Return the current FastMCP request context details (client_id, session_id, and meta dump)."
)
def debug_request_context(ctx: Context) -> dict[str, Any]:
    # Check request_context properties
    rc = getattr(ctx, "request_context", None)
    rc_client_id = getattr(rc, "client_id", None)
    rc_session_id = getattr(rc, "session_id", None)
    meta = getattr(rc, "meta", None)

    # Check direct ctx properties (per latest FastMCP docs)
    ctx_session_id = getattr(ctx, "session_id", None)
    ctx_client_id = getattr(ctx, "client_id", None)

    meta_dump = None
    if meta is not None:
        try:
            dump_fn = getattr(meta, "model_dump", None)
            if callable(dump_fn):
                meta_dump = dump_fn(exclude_none=False)
            elif isinstance(meta, dict):
                meta_dump = dict(meta)
        except Exception as e:
            meta_dump = {"_error": str(e)}

    # List all ctx attributes for debugging
    ctx_attrs = [attr for attr in dir(ctx) if not attr.startswith("_")]

    # Get session state info via middleware
    middleware = get_unity_instance_middleware()
    derived_key = middleware._get_session_key(ctx)
    active_instance = middleware.get_active_instance(ctx)

    return {
        "success": True,
        "data": {
            "request_context": {
                "client_id": rc_client_id,
                "session_id": rc_session_id,
                "meta": meta_dump,
            },
            "direct_properties": {
                "session_id": ctx_session_id,
                "client_id": ctx_client_id,
            },
            "session_state": {
                "derived_key": derived_key,
                "active_instance": active_instance,
            },
            "available_attributes": ctx_attrs,
        },
    }
