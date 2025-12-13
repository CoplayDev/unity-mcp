"""
Resource to list all available Unity Editor instances.
"""
from typing import Any

from fastmcp import Context
from services.registry import mcp_for_unity_resource
from transport.legacy.unity_connection import get_unity_connection_pool
from transport.plugin_hub import PluginHub
from transport.unity_transport import _current_transport


@mcp_for_unity_resource(
    uri="unity://instances",
    name="unity_instances",
    description="Lists all running Unity Editor instances with their details."
)
async def unity_instances(ctx: Context) -> dict[str, Any]:
    """
    List all available Unity Editor instances.

    Returns information about each instance including:
    - id: Unique identifier (ProjectName@hash)
    - name: Project name
    - path: Full project path (stdio only)
    - hash: 8-character hash of project path
    - port: TCP port number (stdio only)
    - status: Current status (running, reloading, etc.) (stdio only)
    - last_heartbeat: Last heartbeat timestamp (stdio only)
    - unity_version: Unity version (if available)
    - connected_at: Connection timestamp (HTTP only)

    Returns:
        Dictionary containing list of instances and metadata
    """
    await ctx.info("Listing Unity instances")

    try:
        transport = _current_transport()
        if transport == "http":
            # HTTP/WebSocket transport: query PluginHub
            sessions_data = await PluginHub.get_sessions()
            sessions = sessions_data.sessions

            instances = []
            for session_id, session_info in sessions.items():
                project = session_info.project
                project_hash = session_info.hash

                if not project or not project_hash:
                    raise ValueError(
                        "PluginHub session missing required 'project' or 'hash' fields."
                    )

                instances.append({
                    "id": f"{project}@{project_hash}",
                    "name": project,
                    "hash": project_hash,
                    "unity_version": session_info.unity_version,
                    "connected_at": session_info.connected_at,
                    "session_id": session_id,
                })

            # Check for duplicate project names
            name_counts = {}
            for inst in instances:
                name_counts[inst["name"]] = name_counts.get(
                    inst["name"], 0) + 1

            duplicates = [name for name,
                          count in name_counts.items() if count > 1]

            result = {
                "success": True,
                "transport": transport,
                "instance_count": len(instances),
                "instances": instances,
            }

            if duplicates:
                result["warning"] = (
                    f"Multiple instances found with duplicate project names: {duplicates}. "
                    f"Use full format (e.g., 'ProjectName@hash') to specify which instance."
                )

            return result
        else:
            # Stdio/TCP transport: query connection pool
            pool = get_unity_connection_pool()
            instances = pool.discover_all_instances(force_refresh=False)

            # Check for duplicate project names
            name_counts = {}
            for inst in instances:
                name_counts[inst.name] = name_counts.get(inst.name, 0) + 1

            duplicates = [name for name,
                          count in name_counts.items() if count > 1]

            result = {
                "success": True,
                "transport": transport,
                "instance_count": len(instances),
                "instances": [inst.to_dict() for inst in instances],
            }

            if duplicates:
                result["warning"] = (
                    f"Multiple instances found with duplicate project names: {duplicates}. "
                    f"Use full format (e.g., 'ProjectName@hash') to specify which instance."
                )

            return result

    except Exception as e:
        await ctx.error(f"Error listing Unity instances: {e}")
        return {
            "success": False,
            "error": f"Failed to list Unity instances: {str(e)}",
            "instance_count": 0,
            "instances": []
        }
