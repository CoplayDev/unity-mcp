from typing import Annotated, Any, Literal

from mcp.server.fastmcp import FastMCP, Context
from telemetry_decorator import telemetry_tool

from unity_connection import send_command_with_retry


def register_manage_gameobject_tools(mcp: FastMCP):
    """Register all GameObject management tools with the MCP server."""

    @mcp.tool(name="manage_gameobject", description="Manage GameObjects. Note: for 'get_components', the `data` field contains a dictionary of component names and their serialized properties. For 'get_component', specify 'component_name' to retrieve only that component's serialized data.")
    @telemetry_tool("manage_gameobject")
    def manage_gameobject(
        ctx: Context,
        action: Annotated[Literal["create", "modify", "delete", "find", "add_component", "remove_component", "set_component_property", "get_components", "get_component"], "Perform CRUD operations on GameObjects and components."],
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
        position: Annotated[list[float],
                            "Position - used for both 'create' (initial position) and 'modify' (change position)"] | None = None,
        rotation: Annotated[list[float],
                            "Rotation - used for both 'create' (initial rotation) and 'modify' (change rotation)"] | None = None,
        scale: Annotated[list[float],
                         "Scale - used for both 'create' (initial scale) and 'modify' (change scale)"] | None = None,
        components_to_add: Annotated[list[str],
                                     "List of component names to add"] | None = None,
        primitive_type: Annotated[str,
                                  "Primitive type for 'create' action"] | None = None,
        save_as_prefab: Annotated[bool,
                                  "If True, saves the created GameObject as a prefab"] | None = None,
        prefab_path: Annotated[str, "Path for prefab creation"] | None = None,
        prefab_folder: Annotated[str,
                                 "Folder for prefab creation"] | None = None,
        # --- Parameters for 'modify' ---
        set_active: Annotated[bool,
                              "If True, sets the GameObject active"] | None = None,
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
        find_all: Annotated[bool,
                            "If True, finds all GameObjects matching the search term"] | None = None,
        search_in_children: Annotated[bool,
                                      "If True, searches in children of the GameObject"] | None = None,
        search_inactive: Annotated[bool,
                                   "If True, searches inactive GameObjects"] | None = None,
        # -- Component Management Arguments --
        component_name: Annotated[str,
                                  "Component name for 'add_component' and 'remove_component' actions"] | None = None,
        # Controls whether serialization of private [SerializeField] fields is included
        includeNonPublicSerialized: Annotated[bool,
                                              "Controls whether serialization of private [SerializeField] fields is included"] | None = None,
    ) -> dict[str, Any]:
        ctx.info(f"Processing manage_gameobject: {action}")
        try:
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
                "includeNonPublicSerialized": includeNonPublicSerialized
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

            # Use centralized retry helper
            response = send_command_with_retry("manage_gameobject", params)

            # Check if the response indicates success
            # If the response is not successful, raise an exception with the error message
            if isinstance(response, dict) and response.get("success"):
                return {"success": True, "message": response.get("message", "GameObject operation successful."), "data": response.get("data")}
            return response if isinstance(response, dict) else {"success": False, "message": str(response)}

        except Exception as e:
            return {"success": False, "message": f"Python error managing GameObject: {str(e)}"}