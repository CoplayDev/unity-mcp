from typing import Annotated, Any

from fastmcp import Context
from registry import mcp_for_unity_tool
from unity_connection import get_unity_connection_pool
from unity_instance_middleware import get_unity_instance_middleware


@mcp_for_unity_tool(
	description="Set the active Unity instance for this client/session. Accepts Name@hash or hash."
)
def set_active_instance(
	ctx: Context,
	instance: Annotated[str, "Target instance (Name@hash or hash prefix)"]
) -> dict[str, Any]:
	# Discover running instances
	pool = get_unity_connection_pool()
	instances = pool.discover_all_instances(force_refresh=True)
	ids = {inst.id: inst for inst in instances}
	hashes = {}
	for inst in instances:
		# exact hash and prefix map; last write wins but we'll detect ambiguity
		hashes.setdefault(inst.hash, inst)
	
	# Disallow plain names to ensure determinism
	value = instance.strip()
	resolved = None
	if "@" in value:
		resolved = ids.get(value)
		if resolved is None:
			return {"success": False, "error": f"Instance '{value}' not found. Check unity://instances resource."}
	else:
		# Treat as hash/prefix; require unique match
		candidates = [inst for inst in instances if inst.hash.startswith(value)]
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
