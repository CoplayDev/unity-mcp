from typing import Annotated, Any, Literal

from fastmcp import Context
from mcp.types import ToolAnnotations

from services.registry import mcp_for_unity_tool
from services.tools import get_unity_instance_from_context
from services.tools.utils import coerce_bool
from transport.unity_transport import send_with_unity_instance
from transport.legacy.unity_connection import async_send_command_with_retry
from services.tools.preflight import preflight


# Required parameters for each action
REQUIRED_PARAMS = {
    "get_info": ["prefab_path"],
    "get_hierarchy": ["prefab_path"],
    "create_from_gameobject": ["target", "prefab_path"],
    "modify_contents": ["prefab_path"],
}


@mcp_for_unity_tool(
    description=(
        "Manages Unity Prefab assets via headless operations (no UI, no prefab stages). "
        "Actions: get_info, get_hierarchy, create_from_gameobject, modify_contents. "
        "Use modify_contents for headless prefab editing - ideal for automated workflows. "
        "Use manage_asset action=search filterType=Prefab to list prefabs."
    ),
    annotations=ToolAnnotations(
        title="Manage Prefabs",
        destructiveHint=True,
    ),
)
async def manage_prefabs(
    ctx: Context,
    action: Annotated[
        Literal[
            "create_from_gameobject",
            "get_info",
            "get_hierarchy",
            "modify_contents",
        ],
        "Prefab operation to perform.",
    ],
    prefab_path: Annotated[str, "Prefab asset path (e.g., Assets/Prefabs/MyPrefab.prefab)."] | None = None,
    target: Annotated[str, "Target GameObject: scene object for create_from_gameobject, or object within prefab for modify_contents (name or path like 'Parent/Child')."] | None = None,
    allow_overwrite: Annotated[bool, "Allow replacing existing prefab."] | None = None,
    search_inactive: Annotated[bool, "Include inactive GameObjects in search."] | None = None,
    unlink_if_instance: Annotated[bool, "Unlink from existing prefab before creating new one."] | None = None,
    # modify_contents parameters
    position: Annotated[list[float] | dict[str, float] | str, "New local position [x, y, z] or {x, y, z} for modify_contents."] | None = None,
    rotation: Annotated[list[float] | dict[str, float] | str, "New local rotation (euler angles) [x, y, z] or {x, y, z} for modify_contents."] | None = None,
    scale: Annotated[list[float] | dict[str, float] | str, "New local scale [x, y, z] or {x, y, z} for modify_contents."] | None = None,
    name: Annotated[str, "New name for the target object in modify_contents."] | None = None,
    tag: Annotated[str, "New tag for the target object in modify_contents."] | None = None,
    layer: Annotated[str, "New layer name for the target object in modify_contents."] | None = None,
    set_active: Annotated[bool, "Set active state of target object in modify_contents."] | None = None,
    parent: Annotated[str, "New parent object name/path within prefab for modify_contents."] | None = None,
    components_to_add: Annotated[list[str], "Component types to add in modify_contents."] | None = None,
    components_to_remove: Annotated[list[str], "Component types to remove in modify_contents."] | None = None,
) -> dict[str, Any]:
    # Back-compat: map 'name' → 'target' for create_from_gameobject (Unity accepts both)
    if action == "create_from_gameobject" and target is None and name is not None:
        target = name

    # Validate required parameters
    required = REQUIRED_PARAMS.get(action, [])
    for param_name in required:
        # Use updated local value for target after back-compat mapping
        param_value = target if param_name == "target" else locals().get(param_name)
        # Check for None and empty/whitespace strings
        if param_value is None or (isinstance(param_value, str) and not param_value.strip()):
            return {
                "success": False,
                "message": f"Action '{action}' requires parameter '{param_name}'."
            }

    unity_instance = get_unity_instance_from_context(ctx)

    # Preflight check for operations to ensure Unity is ready
    try:
        gate = await preflight(ctx, wait_for_no_compile=True, refresh_if_dirty=True)
        if gate is not None:
            return gate.model_dump()
    except Exception as exc:
        return {
            "success": False,
            "message": f"Unity preflight check failed: {exc}"
        }

    try:
        # Build parameters dictionary
        params: dict[str, Any] = {"action": action}

        # Handle prefab path parameter
        if prefab_path:
            params["prefabPath"] = prefab_path

        if target:
            params["target"] = target

        allow_overwrite_val = coerce_bool(allow_overwrite)
        if allow_overwrite_val is not None:
            params["allowOverwrite"] = allow_overwrite_val

        search_inactive_val = coerce_bool(search_inactive)
        if search_inactive_val is not None:
            params["searchInactive"] = search_inactive_val

        unlink_if_instance_val = coerce_bool(unlink_if_instance)
        if unlink_if_instance_val is not None:
            params["unlinkIfInstance"] = unlink_if_instance_val

        # modify_contents parameters
        if position is not None:
            params["position"] = position
        if rotation is not None:
            params["rotation"] = rotation
        if scale is not None:
            params["scale"] = scale
        if name is not None:
            params["name"] = name
        if tag is not None:
            params["tag"] = tag
        if layer is not None:
            params["layer"] = layer
        set_active_val = coerce_bool(set_active)
        if set_active_val is not None:
            params["setActive"] = set_active_val
        if parent is not None:
            params["parent"] = parent
        if components_to_add is not None:
            params["componentsToAdd"] = components_to_add
        if components_to_remove is not None:
            params["componentsToRemove"] = components_to_remove

        # Send command to Unity
        response = await send_with_unity_instance(
            async_send_command_with_retry, unity_instance, "manage_prefabs", params
        )

        # Return Unity response directly; ensure success field exists
        # Handle MCPResponse objects (returned on error) by converting to dict
        if hasattr(response, 'model_dump'):
            return response.model_dump()
        if isinstance(response, dict):
            if "success" not in response:
                response["success"] = False
            return response
        return {
            "success": False,
            "message": f"Unexpected response type: {type(response).__name__}"
        }

    except TimeoutError:
        return {
            "success": False,
            "message": "Unity connection timeout. Please check if Unity is running and responsive."
        }
    except Exception as exc:
        return {
            "success": False,
            "message": f"Error managing prefabs: {exc}"
        }
