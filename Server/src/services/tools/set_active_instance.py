from typing import Annotated, Any
from types import SimpleNamespace

from fastmcp import Context
from services.registry import mcp_for_unity_tool
from transport.legacy.unity_connection import get_unity_connection_pool
from transport.unity_instance_middleware import get_unity_instance_middleware
from transport.plugin_hub import PluginHub
from transport.unity_transport import _is_http_transport as _core_is_http_transport


def _is_http_transport() -> bool:
    # Delegate to transport helper so detection stays consistent across modules.
    return _core_is_http_transport()


@mcp_for_unity_tool(
    description="Set the active Unity instance for this client/session. Accepts Name@hash or hash."
)
async def set_active_instance(
        ctx: Context,
        instance: Annotated[str, "Target instance (Name@hash or hash prefix)"]
) -> dict[str, Any]:
    # Discover running instances based on transport
    if _is_http_transport():
        sessions_data = await PluginHub.get_sessions()
        sessions = sessions_data.get("sessions", {}) if isinstance(
            sessions_data, dict) else {}
        instances = []
        for session_id, session in sessions.items():
            project = session.get("project") or session.get(
                "project_name") or "Unknown"
            hash_value = session.get("hash")
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
    ids = {inst.id: inst for inst in instances}
    hashes = {}
    for inst in instances:
        # exact hash and prefix map; last write wins but we'll detect ambiguity
        hashes.setdefault(inst.hash, inst)

    # Disallow plain names to ensure determinism
    value = (instance or "").strip()
    if not value:
        return {
            "success": False,
            "error": "Instance identifier must not be empty. Specify Name@hash or a unique hash prefix."
        }
    resolved = None
    if "@" in value:
        resolved = ids.get(value)
        if resolved is None:
            return {"success": False, "error": f"Instance '{value}' not found. Check unity://instances resource."}
    else:
        # Treat as hash/prefix; require unique match
        candidates = [
            inst for inst in instances if inst.hash.startswith(value)]
        if len(candidates) == 1:
            resolved = candidates[0]
        elif len(candidates) == 0:
            return {"success": False, "error": f"No instance with hash '{value}'."}
        else:
            return {"success": False, "error": f"Hash '{value}' matches multiple instances: {[c.id for c in candidates]}"}

    # Store selection in middleware (session-scoped)
    middleware = get_unity_instance_middleware()
    middleware.set_active_instance(ctx, resolved.id)
    return {"success": True, "message": f"Active instance set to {resolved.id}", "data": {"instance": resolved.id}}
