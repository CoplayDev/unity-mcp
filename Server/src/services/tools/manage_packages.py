"""
Tool for Unity Package Manager — list, add, remove, inspect, search packages.
Actions: list, get_info, add, remove, search.
Uses built-in UnityEditor.PackageManager — no package dependency.
"""
from typing import Annotated, Any, Literal

from fastmcp import Context
from mcp.types import ToolAnnotations

from services.registry import mcp_for_unity_tool
from services.tools import get_unity_instance_from_context
from transport.unity_transport import send_with_unity_instance
from transport.legacy.unity_connection import async_send_command_with_retry


@mcp_for_unity_tool(
    description=(
        "Unity Package Manager operations. "
        "Actions: list (all installed packages), "
        "get_info (package detail — version, deps, description), "
        "add (install by name or git URL), "
        "remove (uninstall package), "
        "search (search Unity registry). "
        "Uses built-in UnityEditor.PackageManager — no package dependency."
    ),
    annotations=ToolAnnotations(title="Manage Packages"),
)
async def manage_packages(
    ctx: Context,
    action: Annotated[
        Literal["list", "get_info", "add", "remove", "search"],
        "Action to perform on Unity Package Manager."
    ],
    # Package params
    package_name: Annotated[str, "Package name (e.g. com.unity.cinemachine)"] | None = None,
    package_id: Annotated[str, "Package ID or git URL (for add)"] | None = None,
    query: Annotated[str, "Search query (for search)"] | None = None,
    include_built_in: Annotated[bool, "Include built-in packages in list"] | None = None,
) -> dict[str, Any]:
    unity_instance = get_unity_instance_from_context(ctx)

    params = {
        "action": action,
        "package_name": package_name,
        "package_id": package_id,
        "query": query,
        "include_built_in": include_built_in,
    }
    params = {k: v for k, v in params.items() if v is not None}

    try:
        response = await send_with_unity_instance(
            async_send_command_with_retry, unity_instance, "manage_packages", params
        )

        if isinstance(response, dict) and response.get("success"):
            return {
                "success": True,
                "message": response.get("message", "Package operation successful."),
                "data": response.get("data"),
            }
        return response if isinstance(response, dict) else {"success": False, "message": str(response)}

    except Exception as e:
        return {"success": False, "message": f"Python error managing packages: {str(e)}"}
