"""
Tool for Unity Netcode for GameObjects — network manager, objects, connection state.
Actions: get_network_manager, list_network_objects, get_network_object,
         start_host, start_server, start_client, shutdown.
Requires com.unity.netcode.gameobjects package.
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
        "Unity Netcode for GameObjects operations. "
        "Actions: get_network_manager (transport, connection state, clients), "
        "list_network_objects (all NetworkObject components), "
        "get_network_object (ownership, network ID, spawn state), "
        "start_host (start as host), "
        "start_server (start as server), "
        "start_client (start as client), "
        "shutdown (stop networking). "
        "Requires com.unity.netcode.gameobjects package."
    ),
    annotations=ToolAnnotations(title="Manage Netcode"),
)
async def manage_netcode(
    ctx: Context,
    action: Annotated[
        Literal[
            "get_network_manager", "list_network_objects", "get_network_object",
            "start_host", "start_server", "start_client", "shutdown"
        ],
        "Action to perform on Unity Netcode."
    ],
    target: Annotated[str, "GameObject name or instance ID with NetworkObject"] | None = None,
    page_size: Annotated[int, "Max results to return (default 50)"] | None = None,
    cursor: Annotated[int, "Pagination cursor (0-based offset)"] | None = None,
) -> dict[str, Any]:
    unity_instance = get_unity_instance_from_context(ctx)

    params = {"action": action, "target": target,
              "page_size": page_size, "cursor": cursor}
    params = {k: v for k, v in params.items() if v is not None}

    try:
        response = await send_with_unity_instance(
            async_send_command_with_retry, unity_instance, "manage_netcode", params)
        if isinstance(response, dict) and response.get("success"):
            return {"success": True, "message": response.get("message", "OK"), "data": response.get("data")}
        return response if isinstance(response, dict) else {"success": False, "message": str(response)}
    except Exception as e:
        return {"success": False, "message": f"Python error managing netcode: {str(e)}"}
