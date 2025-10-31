from typing import Annotated, Any, Literal

from fastmcp import Context
from registry import mcp_for_unity_tool
from unity_connection import send_command_with_retry


@mcp_for_unity_tool(
    description="Bridge for prefab management commands (stage control and creation)."
)
def manage_prefabs(
    ctx: Context,
    action: Annotated[Literal[
        "open_stage",
        "close_stage",
        "save_open_stage",
        "create_from_gameobject",
    ], "Manage prefabs (stage control and creation)."],
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
    unity_instance: Annotated[str,
                             "Target Unity instance (project name, hash, or 'Name@hash'). If not specified, uses default instance."] | None = None,
) -> dict[str, Any]:
    """
    Manage prefab stages and create prefabs in a connected Unity instance.
    
    Parameters:
        action: One of "open_stage", "close_stage", "save_open_stage", or "create_from_gameobject" specifying the prefab operation to perform.
        prefab_path: Prefab asset path relative to the project Assets folder (e.g. "Assets/Prefabs/favorite.prefab"); used for stage/open/save operations and creation target.
        mode: Optional prefab stage mode (currently only "InIsolation" is supported).
        save_before_close: When true and action is "close_stage", save the prefab before exiting the stage.
        target: Scene GameObject name required when action is "create_from_gameobject".
        allow_overwrite: When true, allow replacing an existing prefab at the specified prefab_path.
        search_inactive: When true, include inactive GameObjects when resolving the target name.
        unity_instance: Identifier for the target Unity instance (project name, hash, or "Name@hash"); if omitted, the default instance is used.
    
    Returns:
        result (dict[str, Any]): Outcome object with:
            - "success" (bool): `true` if the operation succeeded, `false` otherwise.
            - "message" (str): Human-readable status or error message.
            - "data" (optional): Additional result data returned by the Unity tool.
    """
    ctx.info(f"Processing manage_prefabs: {action}")
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
        response = send_command_with_retry("manage_prefabs", params, instance_id=unity_instance)

        if isinstance(response, dict) and response.get("success"):
            return {
                "success": True,
                "message": response.get("message", "Prefab operation successful."),
                "data": response.get("data"),
            }
        return response if isinstance(response, dict) else {"success": False, "message": str(response)}
    except Exception as exc:
        return {"success": False, "message": f"Python error managing prefabs: {exc}"}