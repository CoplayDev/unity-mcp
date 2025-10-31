from typing import Annotated, Literal, Any

from fastmcp import Context
from registry import mcp_for_unity_tool
from unity_connection import send_command_with_retry


@mcp_for_unity_tool(description="Manage Unity scenes. Tip: For broad client compatibility, pass build_index as a quoted string (e.g., '0').")
def manage_scene(
    ctx: Context,
    action: Annotated[Literal["create", "load", "save", "get_hierarchy", "get_active", "get_build_settings"], "Perform CRUD operations on Unity scenes."],
    name: Annotated[str,
                    "Scene name. Not required get_active/get_build_settings"] | None = None,
    path: Annotated[str,
                    "Asset path for scene operations (default: 'Assets/')"] | None = None,
    build_index: Annotated[int | str,
                           "Build index for load/build settings actions (accepts int or string, e.g., 0 or '0')"] | None = None,
    unity_instance: Annotated[str,
                             "Target Unity instance (project name, hash, or 'Name@hash'). If not specified, uses default instance."] | None = None,
) -> dict[str, Any]:
    """
    Manage Unity scene operations (create, load, save) and scene queries.
    
    Parameters:
        path (str | None): Asset path for scene operations. Defaults to the Unity project Assets root (e.g., "Assets/") if not provided.
        build_index (int | str | None): Build index for actions that accept an index (accepts integers or numeric strings like "0"); non-numeric or missing values are treated as not provided.
        unity_instance (str | None): Target Unity instance identifier (project name, hash, or "Name@hash"). If omitted, the default instance is used.
    
    Returns:
        dict[str, Any]: A normalized response with keys:
            - `success` (bool): `true` if the operation succeeded, `false` otherwise.
            - `message` (str): Human-readable status or error message.
            - `data` (Any, optional): Optional payload returned by the Unity side when available.
    """
    ctx.info(f"Processing manage_scene: {action}")
    try:
        # Coerce numeric inputs defensively
        def _coerce_int(value, default=None):
            if value is None:
                return default
            try:
                if isinstance(value, bool):
                    return default
                if isinstance(value, int):
                    return int(value)
                s = str(value).strip()
                if s.lower() in ("", "none", "null"):
                    return default
                return int(float(s))
            except Exception:
                return default

        coerced_build_index = _coerce_int(build_index, default=None)

        params = {"action": action}
        if name:
            params["name"] = name
        if path:
            params["path"] = path
        if coerced_build_index is not None:
            params["buildIndex"] = coerced_build_index

        # Use centralized retry helper with instance routing
        response = send_command_with_retry("manage_scene", params, instance_id=unity_instance)

        # Preserve structured failure data; unwrap success into a friendlier shape
        if isinstance(response, dict) and response.get("success"):
            return {"success": True, "message": response.get("message", "Scene operation successful."), "data": response.get("data")}
        return response if isinstance(response, dict) else {"success": False, "message": str(response)}

    except Exception as e:
        return {"success": False, "message": f"Python error managing scene: {str(e)}"}