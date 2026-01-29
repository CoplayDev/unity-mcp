import json
from typing import Annotated, Any, Literal

from fastmcp import Context
from mcp.types import ToolAnnotations

from services.registry import mcp_for_unity_tool
from services.tools import get_unity_instance_from_context
from transport.unity_transport import send_with_unity_instance
from transport.legacy.unity_connection import async_send_command_with_retry
from services.tools.utils import coerce_bool, parse_json_payload, coerce_int, normalize_vector3
from services.tools.preflight import preflight


def _normalize_component_properties(value: Any) -> tuple[dict[str, dict[str, Any]] | None, str | None]:
    """
    Robustly normalize component_properties to a dict.
    Returns (parsed_dict, error_message). If error_message is set, parsed_dict is None.
    """
    if value is None:
        return None, None

    # Already a dict - validate structure
    if isinstance(value, dict):
        return value, None

    # Try parsing as JSON string
    if isinstance(value, str):
        # Check for obviously invalid values
        if value in ("[object Object]", "undefined", "null", ""):
            return None, f"component_properties received invalid value: '{value}'. Expected a JSON object like {{\"ComponentName\": {{\"property\": value}}}}"

        parsed = parse_json_payload(value)
        if isinstance(parsed, dict):
            return parsed, None

        return None, f"component_properties must be a JSON object (dict), got string that parsed to {type(parsed).__name__}"

    return None, f"component_properties must be a dict or JSON string, got {type(value).__name__}"


@mcp_for_unity_tool(
    description="Performs CRUD operations on GameObjects. Actions: create, modify, delete, duplicate, move_relative. For finding GameObjects use find_gameobjects tool. For component operations use manage_components tool.",
    annotations=ToolAnnotations(
        title="Manage GameObject",
        destructiveHint=True,
    ),
)
async def manage_gameobject(
    ctx: Context,
    action: Annotated[Literal["create", "modify", "delete", "duplicate",
                              "move_relative"], "Action to perform on GameObject."] | None = None,
    target: Annotated[str,
                      "GameObject identifier by name or path for modify/delete/component actions"] | None = None,
    search_method: Annotated[Literal["by_id", "by_name", "by_path", "by_tag", "by_layer", "by_component"],
                             "How to find objects. Used with 'find' and some 'target' lookups."] | None = None,
    name: Annotated[str,
                    "GameObject name for 'create' (initial name) and 'modify' (rename) actions ONLY. For 'find' action, use 'search_term' instead."] | None = None,
    tag: Annotated[str,
                   "Tag name - used for both 'create' (initial tag) and 'modify' (change tag)"] | None = None,
    parent: Annotated[str,
                      "Parent GameObject reference - used for both 'create' (initial parent) and 'modify' (change parent)"] | None = None,
    position: Annotated[list[float] | dict[str, float] | str,
                        "Position as [x, y, z] array, {x, y, z} object, or JSON string"] | None = None,
    rotation: Annotated[list[float] | dict[str, float] | str,
                        "Rotation as [x, y, z] euler angles array, {x, y, z} object, or JSON string"] | None = None,
    scale: Annotated[list[float] | dict[str, float] | str,
                     "Scale as [x, y, z] array, {x, y, z} object, or JSON string"] | None = None,
    components_to_add: Annotated[list[str],
                                 "List of component names to add"] | None = None,
    primitive_type: Annotated[str,
                              "Primitive type for 'create' action"] | None = None,
    save_as_prefab: Annotated[bool | str,
                              "If True, saves the created GameObject as a prefab (accepts true/false or 'true'/'false')"] | None = None,
    prefab_path: Annotated[str, "Path for prefab creation"] | None = None,
    prefab_folder: Annotated[str,
                             "Folder for prefab creation"] | None = None,
    # --- Parameters for 'modify' ---
    set_active: Annotated[bool | str,
                          "If True, sets the GameObject active (accepts true/false or 'true'/'false')"] | None = None,
    layer: Annotated[str, "Layer name"] | None = None,
    components_to_remove: Annotated[list[str],
                                    "List of component names to remove"] | None = None,
    component_properties: Annotated[dict[str, dict[str, Any]],
                                    """Dictionary of component names to their properties to set. For example:
                                    `{"MyScript": {"otherObject": {"find": "Player", "method": "by_name"}}}` assigns GameObject
                                    `{"MyScript": {"playerHealth": {"find": "Player", "component": "HealthComponent"}}}` assigns Component
                                    Example set nested property:
                                    - Access shared material: `{"MeshRenderer": {"sharedMaterial.color": [1, 0, 0, 1]}}`"""] | None = None,
    # --- Parameters for 'find' ---
    search_term: Annotated[str,
                           "Search term for 'find' action ONLY. Use this (not 'name') when searching for GameObjects."] | None = None,
    find_all: Annotated[bool | str,
                        "If True, finds all GameObjects matching the search term (accepts true/false or 'true'/'false')"] | None = None,
    search_in_children: Annotated[bool | str,
                                  "If True, searches in children of the GameObject (accepts true/false or 'true'/'false')"] | None = None,
    search_inactive: Annotated[bool | str,
                               "If True, searches inactive GameObjects (accepts true/false or 'true'/'false')"] | None = None,
    # -- Component Management Arguments --
    component_name: Annotated[str,
                              "Component name for 'add_component' and 'remove_component' actions"] | None = None,
    # Controls whether serialization of private [SerializeField] fields is included
    includeNonPublicSerialized: Annotated[bool | str,
                                          "Controls whether serialization of private [SerializeField] fields is included (accepts true/false or 'true'/'false')"] | None = None,
    # --- Paging/safety for get_components ---
    page_size: Annotated[int | str,
                         "Page size for get_components paging."] | None = None,
    cursor: Annotated[int | str,
                      "Opaque cursor for get_components paging (offset)."] | None = None,
    max_components: Annotated[int | str,
                              "Hard cap on returned components per request (safety)."] | None = None,
    include_properties: Annotated[bool | str,
                                  "If true, include serialized component properties (bounded)."] | None = None,
    # --- Parameters for 'duplicate' ---
    new_name: Annotated[str,
                        "New name for the duplicated object (default: SourceName_Copy)"] | None = None,
    offset: Annotated[list[float] | str,
                      "Offset from original/reference position as [x, y, z] array (list or JSON string)"] | None = None,
    # --- Parameters for 'move_relative' ---
    reference_object: Annotated[str,
                                "Reference object for relative movement (required for move_relative)"] | None = None,
    direction: Annotated[Literal["left", "right", "up", "down", "forward", "back", "front", "backward", "behind"],
                         "Direction for relative movement (e.g., 'right', 'up', 'forward')"] | None = None,
    distance: Annotated[float,
                        "Distance to move in the specified direction (default: 1.0)"] | None = None,
    world_space: Annotated[bool | str,
                           "If True (default), use world space directions; if False, use reference object's local directions"] | None = None,
) -> dict[str, Any]:
    # Get active instance from session state
    # Removed session_state import
    unity_instance = get_unity_instance_from_context(ctx)

    gate = await preflight(ctx, wait_for_no_compile=True, refresh_if_dirty=True)
    if gate is not None:
        return gate.model_dump()

    if action is None:
        return {
            "success": False,
            "message": "Missing required parameter 'action'. Valid actions: create, modify, delete, duplicate, move_relative. For finding GameObjects use find_gameobjects tool. For component operations use manage_components tool."
        }

    # --- Normalize vector parameters with detailed error handling ---
    position, position_error = normalize_vector3(position, "position")
    if position_error:
        return {"success": False, "message": position_error}
    rotation, rotation_error = normalize_vector3(rotation, "rotation")
    if rotation_error:
        return {"success": False, "message": rotation_error}
    scale, scale_error = normalize_vector3(scale, "scale")
    if scale_error:
        return {"success": False, "message": scale_error}
    offset, offset_error = normalize_vector3(offset, "offset")
    if offset_error:
        return {"success": False, "message": offset_error}

    # --- Normalize boolean parameters ---
    save_as_prefab = coerce_bool(save_as_prefab)
    set_active = coerce_bool(set_active)
    find_all = coerce_bool(find_all)
    search_in_children = coerce_bool(search_in_children)
    search_inactive = coerce_bool(search_inactive)
    includeNonPublicSerialized = coerce_bool(includeNonPublicSerialized)
    include_properties = coerce_bool(include_properties)
    world_space = coerce_bool(world_space, default=True)

    # --- Normalize integer parameters ---
    page_size = coerce_int(page_size, default=None)
    cursor = coerce_int(cursor, default=None)
    max_components = coerce_int(max_components, default=None)

    # --- Normalize component_properties with detailed error handling ---
    component_properties, comp_props_error = _normalize_component_properties(
        component_properties)
    if comp_props_error:
        return {"success": False, "message": comp_props_error}

    try:
        # Validate parameter usage to prevent silent failures
        if action in ["create", "modify"]:
            if search_term is not None:
                return {
                    "success": False,
                    "message": f"For '{action}' action, use 'name' parameter, not 'search_term'."
                }

        # Prepare parameters, removing None values
        params = {
            "action": action,
            "target": target,
            "searchMethod": search_method,
            "name": name,
            "tag": tag,
            "parent": parent,
            "position": position,
            "rotation": rotation,
            "scale": scale,
            "componentsToAdd": components_to_add,
            "primitiveType": primitive_type,
            "saveAsPrefab": save_as_prefab,
            "prefabPath": prefab_path,
            "prefabFolder": prefab_folder,
            "setActive": set_active,
            "layer": layer,
            "componentsToRemove": components_to_remove,
            "componentProperties": component_properties,
            "searchTerm": search_term,
            "findAll": find_all,
            "searchInChildren": search_in_children,
            "searchInactive": search_inactive,
            "componentName": component_name,
            "includeNonPublicSerialized": includeNonPublicSerialized,
            "pageSize": page_size,
            "cursor": cursor,
            "maxComponents": max_components,
            "includeProperties": include_properties,
            # Parameters for 'duplicate'
            "new_name": new_name,
            "offset": offset,
            # Parameters for 'move_relative'
            "reference_object": reference_object,
            "direction": direction,
            "distance": distance,
            "world_space": world_space,
        }
        params = {k: v for k, v in params.items() if v is not None}

        # --- Handle Prefab Path Logic ---
        # Check if 'saveAsPrefab' is explicitly True in params
        if action == "create" and params.get("saveAsPrefab"):
            if "prefabPath" not in params:
                if "name" not in params or not params["name"]:
                    return {"success": False, "message": "Cannot create default prefab path: 'name' parameter is missing."}
                # Use the provided prefab_folder (which has a default) and the name to construct the path
                constructed_path = f"{prefab_folder}/{params['name']}.prefab"
                # Ensure clean path separators (Unity prefers '/')
                params["prefabPath"] = constructed_path.replace("\\", "/")
            elif not params["prefabPath"].lower().endswith(".prefab"):
                return {"success": False, "message": f"Invalid prefab_path: '{params['prefabPath']}' must end with .prefab"}
        # Ensure prefabFolder itself isn't sent if prefabPath was constructed or provided
        # The C# side only needs the final prefabPath
        params.pop("prefabFolder", None)
        # --------------------------------

        # Use centralized retry helper with instance routing
        response = await send_with_unity_instance(
            async_send_command_with_retry,
            unity_instance,
            "manage_gameobject",
            params,
        )

        # Check if the response indicates success
        # If the response is not successful, raise an exception with the error message
        if isinstance(response, dict) and response.get("success"):
            return {"success": True, "message": response.get("message", "GameObject operation successful."), "data": response.get("data")}
        return response if isinstance(response, dict) else {"success": False, "message": str(response)}

    except Exception as e:
        return {"success": False, "message": f"Python error managing GameObject: {e!s}"}
