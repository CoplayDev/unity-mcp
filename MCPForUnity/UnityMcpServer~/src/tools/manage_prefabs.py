from typing import Annotated, Any, Literal

from fastmcp import Context
from registry import mcp_for_unity_tool
from tools import get_unity_instance_from_context, send_with_unity_instance
from unity_connection import send_command_with_retry


@mcp_for_unity_tool(
    description="Performs prefab operations (create, modify, delete, etc.)."
)
def manage_prefabs(
    ctx: Context,
    action: Annotated[Literal["create", "modify", "delete", "get_components"], "Perform prefab operations."],
    prefab_path: Annotated[str,
                           "Prefab asset path relative to Assets e.g. Assets/Prefabs/favorite.prefab"] | None = None,
    mode: Annotated[str,
                    "Optional prefab stage mode (only 'InIsolation' is currently supported)"] | None = None,
    save_before_close: Annotated[bool,
                                 "When true, `close_stage` will save the prefab before exiting the stage."] | None = None,
    target: Annotated[str,
                      "Scene GameObject name required for create_from_gameobject"] | None = None,
    allow_overwrite: Annotated[bool,
                               "Allow replacing an existing prefab at the same path"] | None = None,
    search_inactive: Annotated[bool,
                               "Include inactive objects when resolving the target name"] | None = None,
    component_properties: Annotated[str, "Component properties in JSON format"] | None = None,
) -> dict[str, Any]:
    # Get active instance from session state
    # Removed session_state import
    unity_instance = get_unity_instance_from_context(ctx)
    try:
        params: dict[str, Any] = {"action": action}

        if prefab_path:
            params["prefabPath"] = prefab_path
        if mode:
            params["mode"] = mode
        if save_before_close is not None:
            params["saveBeforeClose"] = bool(save_before_close)
        if target:
            params["target"] = target
        if allow_overwrite is not None:
            params["allowOverwrite"] = bool(allow_overwrite)
        if search_inactive is not None:
            params["searchInactive"] = bool(search_inactive)
        response = send_with_unity_instance(send_command_with_retry, unity_instance, "manage_prefabs", params)

        if isinstance(response, dict) and response.get("success"):
            return {
                "success": True,
                "message": response.get("message", "Prefab operation successful."),
                "data": response.get("data"),
            }
        return response if isinstance(response, dict) else {"success": False, "message": str(response)}
    except Exception as exc:
        return {"success": False, "message": f"Python error managing prefabs: {exc}"}
