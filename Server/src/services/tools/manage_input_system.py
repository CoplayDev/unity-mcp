"""
Tool for Unity new Input System — action assets, bindings, devices.
Actions: list_action_assets, get_action_map, get_action, list_devices, get_device, list_player_inputs.
Requires com.unity.inputsystem package.
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
        "Unity Input System inspection. "
        "Actions: list_action_assets (find InputActionAsset in project), "
        "get_action_map (actions in a map with bindings), "
        "get_action (bindings, interactions, processors), "
        "list_devices (connected input devices), "
        "get_device (device layout, controls), "
        "list_player_inputs (find PlayerInput components). "
        "Requires com.unity.inputsystem package."
    ),
    annotations=ToolAnnotations(title="Manage Input System"),
)
async def manage_input_system(
    ctx: Context,
    action: Annotated[
        Literal[
            "list_action_assets", "get_action_map", "get_action",
            "list_devices", "get_device", "list_player_inputs"
        ],
        "Action to perform on Unity Input System."
    ],
    asset: Annotated[str, "InputActionAsset name or path"] | None = None,
    map_name: Annotated[str, "Action map name"] | None = None,
    action_name: Annotated[str, "Input action name"] | None = None,
    device_name: Annotated[str, "Device name or layout"] | None = None,
    page_size: Annotated[int, "Max results to return (default 50)"] | None = None,
    cursor: Annotated[int, "Pagination cursor (0-based offset)"] | None = None,
) -> dict[str, Any]:
    unity_instance = get_unity_instance_from_context(ctx)

    params = {"action": action, "asset": asset, "map_name": map_name,
              "action_name": action_name, "device_name": device_name,
              "page_size": page_size, "cursor": cursor}
    params = {k: v for k, v in params.items() if v is not None}

    try:
        response = await send_with_unity_instance(
            async_send_command_with_retry, unity_instance, "manage_input_system", params)
        if isinstance(response, dict) and response.get("success"):
            return {"success": True, "message": response.get("message", "OK"), "data": response.get("data")}
        return response if isinstance(response, dict) else {"success": False, "message": str(response)}
    except Exception as e:
        return {"success": False, "message": f"Python error managing input system: {str(e)}"}
