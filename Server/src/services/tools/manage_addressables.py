"""
Tool for Unity Addressables — groups, entries, labels, content builds.
Actions: list_groups, get_group, list_entries, get_entry, list_labels, build, analyze.
Requires com.unity.addressables package.
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
        "Unity Addressables asset system operations. "
        "Actions: list_groups (all Addressable groups), "
        "get_group (entries, schemas, build/load paths), "
        "list_entries (entries in group with addresses and labels), "
        "get_entry (entry detail — GUID, address, labels), "
        "list_labels (all labels in settings), "
        "build (build Addressable content), "
        "analyze (run analyze rules for duplicates/issues). "
        "Requires com.unity.addressables package."
    ),
    annotations=ToolAnnotations(title="Manage Addressables"),
)
async def manage_addressables(
    ctx: Context,
    action: Annotated[
        Literal[
            "list_groups", "get_group", "list_entries", "get_entry",
            "list_labels", "build", "analyze"
        ],
        "Action to perform on Unity Addressables."
    ],
    group_name: Annotated[str, "Addressable group name"] | None = None,
    address: Annotated[str, "Addressable entry address"] | None = None,
    guid: Annotated[str, "Addressable entry GUID"] | None = None,
    clean: Annotated[bool, "Clean build (for build action)"] | None = None,
    page_size: Annotated[int, "Max results to return (default 50)"] | None = None,
    cursor: Annotated[int, "Pagination cursor (0-based offset)"] | None = None,
) -> dict[str, Any]:
    unity_instance = get_unity_instance_from_context(ctx)

    params = {"action": action, "group_name": group_name, "address": address,
              "guid": guid, "clean": clean, "page_size": page_size, "cursor": cursor}
    params = {k: v for k, v in params.items() if v is not None}

    try:
        response = await send_with_unity_instance(
            async_send_command_with_retry, unity_instance, "manage_addressables", params)
        if isinstance(response, dict) and response.get("success"):
            return {"success": True, "message": response.get("message", "OK"), "data": response.get("data")}
        return response if isinstance(response, dict) else {"success": False, "message": str(response)}
    except Exception as e:
        return {"success": False, "message": f"Python error managing addressables: {str(e)}"}
