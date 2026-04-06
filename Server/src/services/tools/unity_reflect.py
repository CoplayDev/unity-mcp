from typing import Annotated, Any, Optional

from fastmcp import Context
from mcp.types import ToolAnnotations

from services.registry import mcp_for_unity_tool
from services.tools import get_unity_instance_from_context
from transport.unity_transport import send_with_unity_instance
from transport.legacy.unity_connection import async_send_command_with_retry

ALL_ACTIONS = ["get_type", "get_member", "search"]

VALID_SCOPES = ["unity", "packages", "project", "all"]


async def _send_reflect_command(
    ctx: Context,
    params_dict: dict[str, Any],
) -> dict[str, Any]:
    unity_instance = await get_unity_instance_from_context(ctx)
    result = await send_with_unity_instance(
        async_send_command_with_retry, unity_instance, "unity_reflect", params_dict
    )
    return result if isinstance(result, dict) else {"success": False, "message": str(result)}


@mcp_for_unity_tool(
    group="docs",
    description=(
        "Inspect Unity's live C# API via reflection. Use this to verify that classes, "
        "methods, and properties exist before writing C# code — training data may be "
        "wrong or outdated.\n\n"
        "Actions:\n"
        "- get_type: Member summary (names only) for a class. Requires class_name.\n"
        "- get_member: Full signature detail for one member. Requires class_name + member_name.\n"
        "- search: Type name search across loaded assemblies. Requires query. Optional scope."
    ),
    annotations=ToolAnnotations(
        title="Unity Reflect",
        readOnlyHint=True,
        destructiveHint=False,
    ),
)
async def unity_reflect(
    ctx: Context,
    action: Annotated[str, "The reflection action to perform."],
    class_name: Annotated[Optional[str], "Fully qualified or simple C# class name."] = None,
    member_name: Annotated[Optional[str], "Method, property, or field name to inspect."] = None,
    query: Annotated[Optional[str], "Search query for type name search."] = None,
    scope: Annotated[Optional[str], "Assembly scope for search: unity, packages, project, all."] = None,
) -> dict[str, Any]:
    action_lower = action.lower()
    if action_lower not in ALL_ACTIONS:
        return {
            "success": False,
            "message": f"Unknown action '{action}'. Valid actions: {', '.join(ALL_ACTIONS)}",
        }

    # Validate required params per action
    if action_lower == "get_type":
        if not class_name:
            return {
                "success": False,
                "message": "get_type requires class_name.",
            }
    elif action_lower == "get_member":
        if not class_name or not member_name:
            return {
                "success": False,
                "message": "get_member requires class_name and member_name.",
            }
    elif action_lower == "search":
        if not query:
            return {
                "success": False,
                "message": "search requires query.",
            }

    params_dict: dict[str, Any] = {"action": action_lower}

    if class_name is not None:
        params_dict["class_name"] = class_name
    if member_name is not None:
        params_dict["member_name"] = member_name
    if query is not None:
        params_dict["query"] = query
    if action_lower == "search" and scope is not None:
        if scope not in VALID_SCOPES:
            return {
                "success": False,
                "message": f"Invalid scope '{scope}'. Valid scopes: {', '.join(VALID_SCOPES)}",
            }
        params_dict["scope"] = scope

    return await _send_reflect_command(ctx, params_dict)
