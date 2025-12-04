from typing import Annotated, Any
from types import SimpleNamespace

from fastmcp import Context
from services.registry import mcp_for_unity_tool
from transport.legacy.unity_connection import get_unity_connection_pool
from transport.unity_instance_middleware import get_unity_instance_middleware
from transport.plugin_hub import PluginHub
from transport.unity_transport import _current_transport


@mcp_for_unity_tool(
    description="Set the active Unity instance for this client/session. Accepts Name@hash or hash."
)
async def set_active_instance(
        ctx: Context,
        instance: Annotated[str, "Target instance (Name@hash or hash prefix)"]
) -> dict[str, Any]:
    transport = _current_transport()

    # Discover running instances based on transport
    if transport == "http":
        sessions_data = await PluginHub.get_sessions()
        sessions = sessions_data.sessions
        instances = []
        for session_id, session in sessions.items():
            project = session.project or "Unknown"
            hash_value = session.hash
            if not hash_value:
                continue
            inst_id = f"{project}@{hash_value}"
            instances.append(SimpleNamespace(
                id=inst_id,
                hash=hash_value,
                name=project,
                session_id=session_id,
            ))
    else:
        pool = get_unity_connection_pool()
        instances = pool.discover_all_instances(force_refresh=True)

    if not instances:
        return {
            "success": False,
            "error": "No Unity instances are currently connected. Start Unity and press 'Start Session'."
        }
    ids = {inst.id: inst for inst in instances if getattr(inst, "id", None)}

    value = (instance or "").strip()
    if not value:
        return {
            "success": False,
            "error": "Instance identifier is required. "
                     "Use unity://instances to copy a Name@hash or provide a hash prefix."
        }
    resolved = None
    if "@" in value:
        resolved = ids.get(value)
        if resolved is None:
            return {
                "success": False,
                "error": f"Instance '{value}' not found. "
                         "Use unity://instances to copy an exact Name@hash."
            }
    else:
        lookup = value.lower()
        matches = []
        for inst in instances:
            if not getattr(inst, "id", None):
                continue
            inst_hash = getattr(inst, "hash", "")
            if inst_hash and inst_hash.lower().startswith(lookup):
                matches.append(inst)
        if not matches:
            return {
                "success": False,
                "error": f"Instance hash '{value}' does not match any running Unity editors. "
                         "Use unity://instances to confirm the available hashes."
            }
        if len(matches) > 1:
            matching_ids = ", ".join(
                inst.id for inst in matches if getattr(inst, "id", None)
            ) or "multiple instances"
            return {
                "success": False,
                "error": f"Instance hash '{value}' is ambiguous ({matching_ids}). "
                         "Provide the full Name@hash from unity://instances."
            }
        resolved = matches[0]
    
    if resolved is None:
        # Should be unreachable due to logic above, but satisfies static analysis
        return {
            "success": False,
            "error": "Internal error: Instance resolution failed."
        }

    # Store selection in middleware (session-scoped)
    middleware = get_unity_instance_middleware()
    # We use middleware.set_active_instance to persist the selection.
    # The session key is an internal detail but useful for debugging response.
    middleware.set_active_instance(ctx, resolved.id)
    session_key = middleware.get_session_key(ctx)
    
    return {
        "success": True,
        "message": f"Active instance set to {resolved.id}",
        "data": {
            "instance": resolved.id,
            "session_key": session_key,
        },
    }
