from typing import Annotated, Any, Optional

from fastmcp import Context
from mcp.types import ToolAnnotations

from services.registry import mcp_for_unity_tool
from services.tools import get_unity_instance_from_context
from transport.unity_transport import send_with_unity_instance
from transport.legacy.unity_connection import async_send_command_with_retry

ALL_ACTIONS = [
    "list_packages", "search_packages", "get_package_info", "ping", "status",
    "add_package", "remove_package", "embed_package", "resolve_packages",
    "add_registry", "remove_registry", "list_registries",
]


async def _send_packages_command(
    ctx: Context,
    params_dict: dict[str, Any],
) -> dict[str, Any]:
    unity_instance = await get_unity_instance_from_context(ctx)
    result = await send_with_unity_instance(
        async_send_command_with_retry, unity_instance, "manage_packages", params_dict
    )
    return result if isinstance(result, dict) else {"success": False, "message": str(result)}


@mcp_for_unity_tool(
    group="core",
    description=(
        "Manage Unity packages: query, install, remove, embed, and configure registries.\n\n"
        "QUERY (read-only):\n"
        "- list_packages: List all installed packages\n"
        "- search_packages: Search Unity registry by keyword\n"
        "- get_package_info: Get details about a specific installed package\n"
        "- ping: Check package manager availability\n"
        "- status: Poll async job status (job_id required for list/search; optional for add/remove/embed)\n\n"
        "INSTALL/REMOVE:\n"
        "- add_package: Install a package (name, name@version, git URL, or file: path)\n"
        "- remove_package: Remove a package (checks dependents; use force=true to override)\n\n"
        "REGISTRIES:\n"
        "- list_registries: List all scoped registries\n"
        "- add_registry: Add a scoped registry (e.g., OpenUPM)\n"
        "- remove_registry: Remove a scoped registry\n\n"
        "UTILITY:\n"
        "- embed_package: Copy package to local Packages/ for editing\n"
        "- resolve_packages: Force re-resolution of all packages"
    ),
    annotations=ToolAnnotations(
        title="Manage Packages",
        destructiveHint=True,
        readOnlyHint=False,
    ),
)
async def manage_packages(
    ctx: Context,
    action: Annotated[str, "The package action to perform."],
    package: Annotated[Optional[str], "Package identifier (name, name@version, git URL, or file: path)."] = None,
    force: Annotated[Optional[bool], "Force removal even if other packages depend on it."] = None,
    query: Annotated[Optional[str], "Search query for search_packages."] = None,
    job_id: Annotated[Optional[str], "Job ID for polling status."] = None,
    name: Annotated[Optional[str], "Registry name for add_registry/remove_registry."] = None,
    url: Annotated[Optional[str], "Registry URL for add_registry."] = None,
    scopes: Annotated[Optional[list[str]], "Registry scopes for add_registry."] = None,
) -> dict[str, Any]:
    action_lower = action.lower()
    if action_lower not in ALL_ACTIONS:
        return {
            "success": False,
            "message": f"Unknown action '{action}'. Valid actions: {', '.join(ALL_ACTIONS)}",
        }

    params_dict: dict[str, Any] = {"action": action_lower}
    param_map = {
        "package": package,
        "force": force,
        "query": query,
        "job_id": job_id,
        "name": name,
        "url": url,
        "scopes": scopes,
    }
    for key, val in param_map.items():
        if val is not None:
            params_dict[key] = val

    return await _send_packages_command(ctx, params_dict)
