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
    Manage Unity scenes by creating, loading, saving, or querying scene information.
    
    Parameters:
        action (Literal["create", "load", "save", "get_hierarchy", "get_active", "get_build_settings"]):
            Operation to perform on the scene.
        name (str | None):
            Scene name for operations that require a scene (not required for `get_active` or `get_build_settings`).
        path (str | None):
            Asset path for scene operations (defaults to "Assets/" if not provided).
        build_index (int | str | None):
            Build index for load or build-settings operations. Accepts integers or numeric strings; empty, boolean, or non-convertible values are treated as unspecified.
        unity_instance (str | None):
            Target Unity instance identifier (project name, hash, or "Name@hash"). If omitted, the default instance is used.
    
    Returns:
        dict: A result dictionary. On success: `{"success": True, "message": <message>, "data": <data>}`. On failure: a dict with `success: False` and a `message` describing the error, or the original response dict if it was non-success structured data.
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