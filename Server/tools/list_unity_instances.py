"""
Tool to list all available Unity Editor instances.
"""
from typing import Annotated, Any

from fastmcp import Context
from registry import mcp_for_unity_tool
from unity_connection import get_unity_connection_pool


@mcp_for_unity_tool(description="List all running Unity Editor instances with their details.")
def list_unity_instances(
    ctx: Context,
    force_refresh: Annotated[bool, "Force refresh the instance list, bypassing cache"] = False
) -> dict[str, Any]:
    """
    Produce a structured summary of all detected Unity Editor instances.
    
    Parameters:
        force_refresh (bool): If True, bypass cached discovery and rescan for instances.
    
    Returns:
        dict: A result dictionary with the following keys:
            - success (bool): `True` if discovery completed, `False` on error.
            - instance_count (int): Number of instances discovered.
            - instances (list[dict]): List of instance summaries; each dictionary includes
              keys such as `id` (ProjectName@hash), `name`, `path`, `hash`, `port`,
              `status`, `last_heartbeat`, and `unity_version` when available.
            - warning (str, optional): Present when duplicate project names are detected,
              advising use of the full `ProjectName@hash` format to disambiguate.
            - error (str, optional): Present when `success` is `False`, describing the failure.
    """
    ctx.info(f"Listing Unity instances (force_refresh={force_refresh})")

    try:
        pool = get_unity_connection_pool()
        instances = pool.discover_all_instances(force_refresh=force_refresh)

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
        ctx.error(f"Error listing Unity instances: {e}")
        return {
            "success": False,
            "error": f"Failed to list Unity instances: {str(e)}",
            "instance_count": 0,
            "instances": []
        }