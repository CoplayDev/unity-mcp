from typing import Annotated, Any, Optional

from fastmcp import Context
from mcp.types import ToolAnnotations

from services.registry import mcp_for_unity_tool
from services.tools import get_unity_instance_from_context
from transport.unity_transport import send_with_unity_instance
from transport.legacy.unity_connection import async_send_command_with_retry

QUERY_ACTIONS = ["list_packages", "search_packages", "get_package_info", "ping", "status"]
MUTATE_ACTIONS = [
    "add_package", "remove_package", "embed_package", "resolve_packages",
    "add_registry", "remove_registry",
]
ALL_ACTIONS = QUERY_ACTIONS + MUTATE_ACTIONS


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
        "Query Unity package information (read-only).\n\n"
        "- list_packages: List all installed packages\n"
        "- search_packages: Search Unity registry by keyword\n"
        "- get_package_info: Get details about a specific installed package\n"
        "- ping: Check package manager availability\n"
        "- status: Poll async job status (for add/remove/list/search operations)"
    ),
    annotations=ToolAnnotations(
        title="Query Packages",
        readOnlyHint=True,
        destructiveHint=False,
    ),
)
async def query_packages(
    ctx: Context,
    action: Annotated[str, "The query action to perform."],
    package: Annotated[Optional[str], "Package name for get_package_info."] = None,
    query: Annotated[Optional[str], "Search query for search_packages."] = None,
    job_id: Annotated[Optional[str], "Job ID for polling status."] = None,
) -> dict[str, Any]:
    action_lower = action.lower()
    if action_lower not in QUERY_ACTIONS:
        return {
            "success": False,
            "message": f"Unknown query action '{action}'. Valid actions: {', '.join(QUERY_ACTIONS)}",
        }

    params_dict: dict[str, Any] = {"action": action_lower}
    for key, val in [("package", package), ("query", query), ("job_id", job_id)]:
        if val is not None:
            params_dict[key] = val

    return await _send_packages_command(ctx, params_dict)


@mcp_for_unity_tool(
    group="core",
    description=(
        "Modify Unity packages: install, remove, embed, and configure registries. "
        "Triggers domain reload on add/remove.\n\n"
        "INSTALL/REMOVE:\n"
        "- add_package: Install a package (name, name@version, git URL, or file: path)\n"
        "- remove_package: Remove a package (checks dependents; use force=true to override)\n\n"
        "REGISTRIES:\n"
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
    name: Annotated[Optional[str], "Registry name for add_registry."] = None,
    url: Annotated[Optional[str], "Registry URL for add_registry."] = None,
    scopes: Annotated[Optional[list[str]], "Registry scopes for add_registry."] = None,
) -> dict[str, Any]:
    action_lower = action.lower()
    if action_lower not in MUTATE_ACTIONS:
        return {
            "success": False,
            "message": f"Unknown action '{action}'. Valid actions: {', '.join(MUTATE_ACTIONS)}",
        }

    params_dict: dict[str, Any] = {"action": action_lower}
    param_map = {
        "package": package,
        "force": force,
        "name": name,
        "url": url,
        "scopes": scopes,
    }
    for key, val in param_map.items():
        if val is not None:
            params_dict[key] = val

    return await _send_packages_command(ctx, params_dict)
