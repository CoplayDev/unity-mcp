"""
Resource for listing all available Unity Editor instances.
"""
from typing import Any

from registry import mcp_for_unity_resource
from unity_connection import get_unity_connection_pool


@mcp_for_unity_resource(
    uri="mcpforunity://unity-instances",
    name="unity_instances",
    description="Provides a list of all running Unity Editor instances with their details."
)
def get_unity_instances() -> dict[str, Any]:
    """
    List all available Unity Editor instances.

    Returns information about each instance including:
    - id: Unique identifier (ProjectName@hash)
    - name: Project name
    - path: Full project path
    - hash: 8-character hash of project path
    - port: TCP port number
    - status: Current status (running, reloading, etc.)
    - last_heartbeat: Last heartbeat timestamp
    - unity_version: Unity version (if available)

    Returns:
        Dictionary containing list of instances and metadata
    """
    try:
        pool = get_unity_connection_pool()
        instances = pool.discover_all_instances(force_refresh=True)

        # Check for duplicate project names
        name_counts = {}
        for inst in instances:
            name_counts[inst.name] = name_counts.get(inst.name, 0) + 1

        duplicates = [name for name, count in name_counts.items() if count > 1]

        result = {
            "success": True,
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
        return {
            "success": False,
            "error": f"Failed to list Unity instances: {str(e)}",
            "instance_count": 0,
            "instances": []
        }
