import json
import math
from typing import Annotated, Any, Literal, Union

from fastmcp import Context
from services.registry import mcp_for_unity_tool
from services.tools import get_unity_instance_from_context
from transport.unity_transport import send_with_unity_instance
from transport.legacy.unity_connection import async_send_command_with_retry
from services.tools.utils import coerce_bool, parse_json_payload


@mcp_for_unity_tool(
    description="Performs CRUD operations on GameObjects and components."
)
async def manage_gameobject(
    ctx: Context,
    action: Annotated[Literal["create", "modify", "delete", "find", "add_component", "remove_component", "set_component_property", "get_components", "get_component", "duplicate", "move_relative"], "Perform CRUD operations on GameObjects and components."] | None = None,
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
    position: Annotated[Union[list[float], str],
                        "Position - [x,y,z] or string '[x,y,z]' for client compatibility"] | None = None,
    rotation: Annotated[Union[list[float], str],
                        "Rotation - [x,y,z] or string '[x,y,z]' for client compatibility"] | None = None,
    scale: Annotated[Union[list[float], str],
                     "Scale - [x,y,z] or string '[x,y,z]' for client compatibility"] | None = None,
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
    component_properties: Annotated[Union[dict[str, dict[str, Any]], str],
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
    # --- Parameters for 'duplicate' ---
    new_name: Annotated[str,
                        "New name for the duplicated object (default: SourceName_Copy)"] | None = None,
    offset: Annotated[Union[list[float], str],
                      "Offset from original/reference position - [x,y,z] or string '[x,y,z]'"] | None = None,
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

    if action is None:
        return {
            "success": False,
            "message": "Missing required parameter 'action'. Valid actions: create, modify, delete, find, add_component, remove_component, set_component_property, get_components, get_component, duplicate, move_relative"
        }

    # Coercers to tolerate stringified booleans and vectors
    def _coerce_vec(value, default=None):
        if value is None:
            return default
        
        # First try to parse if it's a string
        val = parse_json_payload(value)
        
        def _to_vec3(parts):
            try:
                vec = [float(parts[0]), float(parts[1]), float(parts[2])]
            except (ValueError, TypeError):
                return default
            return vec if all(math.isfinite(n) for n in vec) else default
            
        if isinstance(val, list) and len(val) == 3:
            return _to_vec3(val)
            
        # Handle legacy comma-separated strings "1,2,3" that parse_json_payload doesn't handle (since they aren't JSON arrays)
        if isinstance(val, str):
            s = val.strip()
            # minimal tolerant parse for "[x,y,z]" or "x,y,z"
            if s.startswith("[") and s.endswith("]"):
                s = s[1:-1]
            # support "x,y,z" and "x y z"
            parts = [p.strip()
                     for p in (s.split(",") if "," in s else s.split())]
            if len(parts) == 3:
                return _to_vec3(parts)
        return default

    position = _coerce_vec(position, default=position)
    rotation = _coerce_vec(rotation, default=rotation)
    scale = _coerce_vec(scale, default=scale)
    offset = _coerce_vec(offset, default=offset)
    save_as_prefab = coerce_bool(save_as_prefab)
    set_active = coerce_bool(set_active)
    find_all = coerce_bool(find_all)
    search_in_children = coerce_bool(search_in_children)
    search_inactive = coerce_bool(search_inactive)
    includeNonPublicSerialized = coerce_bool(includeNonPublicSerialized)
    world_space = coerce_bool(world_space, default=True)

    # Coerce 'component_properties' from JSON string to dict for client compatibility
    component_properties = parse_json_payload(component_properties)
    
    # Ensure final type is a dict (object) if provided
    if component_properties is not None and not isinstance(component_properties, dict):
        return {"success": False, "message": "component_properties must be a JSON object (dict)."}
        
    try:
        # Map tag to search_term when search_method is by_tag for backward compatibility
        if action == "find" and search_method == "by_tag" and tag is not None and search_term is None:
            search_term = tag

        # Validate parameter usage to prevent silent failures
        if action == "find":
            if name is not None:
                return {
                    "success": False,
                    "message": "For 'find' action, use 'search_term' parameter, not 'name'. Remove 'name' parameter. Example: search_term='Player', search_method='by_name'"
                }
            if search_term is None:
                return {
                    "success": False,
                    "message": "For 'find' action, 'search_term' parameter is required. Use search_term (not 'name') to specify what to find."
                }

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
