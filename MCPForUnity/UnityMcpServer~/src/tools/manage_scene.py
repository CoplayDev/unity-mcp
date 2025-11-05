from typing import Annotated, Literal, Any

from fastmcp import Context
from registry import mcp_for_unity_tool
from tools import get_unity_instance_from_context, send_with_unity_instance
from unity_connection import send_command_with_retry


@mcp_for_unity_tool(
    description="Performs CRUD operations on Unity scenes."
)
def manage_scene(
    ctx: Context,
    action: Annotated[Literal["create", "load", "save", "get_hierarchy", "get_active", "get_build_settings"], "Perform CRUD operations on Unity scenes."],
    name: Annotated[str, "Scene name."] | None = None,
    path: Annotated[str, "Scene path."] | None = None,
    build_index: Annotated[int | str, "Unity build index (quote as string, e.g., '0')."] | None = None,
) -> dict[str, Any]:
    # Get active instance from session state
    # Removed session_state import
    unity_instance = get_unity_instance_from_context(ctx)
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
        response = send_with_unity_instance(send_command_with_retry, unity_instance, "manage_scene", params)

        # Preserve structured failure data; unwrap success into a friendlier shape
        if isinstance(response, dict) and response.get("success"):
            return {"success": True, "message": response.get("message", "Scene operation successful."), "data": response.get("data")}
        return response if isinstance(response, dict) else {"success": False, "message": str(response)}

    except Exception as e:
        return {"success": False, "message": f"Python error managing scene: {str(e)}"}
