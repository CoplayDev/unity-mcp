from mcp.server.fastmcp import FastMCP, Context
from typing import Dict, Any, List
from unity_connection import get_unity_connection

def register_manage_gameobject_tools(mcp: FastMCP):
    """Register all GameObject management tools with the MCP server."""

    @mcp.tool()
    def manage_gameobject(
        ctx: Context,
        action: str,
        target: str = None,  # GameObject identifier by name or path
        search_method: str = None,
        # --- Combined Parameters for Create/Modify ---
        name: str = None,  # Used for both 'create' (new object name) and 'modify' (rename)
        tag: str = None,  # Used for both 'create' (initial tag) and 'modify' (change tag)
        parent: str = None,  # Used for both 'create' (initial parent) and 'modify' (change parent)
        position: List[float] = None,
        rotation: List[float] = None,
        scale: List[float] = None,
        components_to_add: List[str] = None,  # List of component names to add
        primitive_type: str = None,
        save_as_prefab: bool = False,
        prefab_path: str = None,
        prefab_folder: str = "Assets/Prefabs",
        # --- Parameters for 'modify' ---
        set_active: bool = None,
        layer: str = None,  # Layer name
        components_to_remove: List[str] = None,
        component_properties: Dict[str, Dict[str, Any]] = None,
        # --- Parameters for 'find' ---
        search_term: str = None,
        find_all: bool = False,
        search_in_children: bool = False,
        search_inactive: bool = False,
        # -- Component Management Arguments --
        component_name: str = None,
    ) -> Dict[str, Any]:
        """Manages GameObjects: create, modify, delete, find, and component operations.

        Args:
            action: Operation (e.g., 'create', 'modify', 'find', 'add_component', 'remove_component', 'set_component_property').
            target: GameObject identifier (name or path string) for modify/delete/component actions.
            search_method: How to find objects ('by_name', 'by_id', 'by_path', etc.). Used with 'find' and some 'target' lookups.
            name: GameObject name - used for both 'create' (initial name) and 'modify' (rename).
            tag: Tag name - used for both 'create' (initial tag) and 'modify' (change tag).
            parent: Parent GameObject reference - used for both 'create' (initial parent) and 'modify' (change parent).
            layer: Layer name - used for both 'create' (initial layer) and 'modify' (change layer).
            component_properties: Dict mapping Component names to their properties to set.
                                  Example: {"Rigidbody": {"mass": 10.0, "useGravity": True}},
                                  To set references:
                                  - Use asset path string for Prefabs/Materials, e.g., {"MeshRenderer": {"material": "Assets/Materials/MyMat.mat"}}
                                  - Use a dict for scene objects/components, e.g.:
                                    {"MyScript": {"otherObject": {"find": "Player", "method": "by_name"}}} (assigns GameObject)
                                    {"MyScript": {"playerHealth": {"find": "Player", "component": "HealthComponent"}}} (assigns Component)
                                  Example set nested property:
                                  - Access shared material: {"MeshRenderer": {"sharedMaterial.color": [1, 0, 0, 1]}}
            components_to_add: List of component names to add.
            Action-specific arguments (e.g., position, rotation, scale for create/modify;
                     component_name for component actions;
                     search_term, find_all for 'find').

        Returns:
            Dictionary with operation results ('success', 'message', 'data').
        """
        try:
            # --- Early check for attempting to modify a prefab asset ---
            # ----------------------------------------------------------

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
                "componentName": component_name
            }
            params = {k: v for k, v in params.items() if v is not None}
            
            # --- Handle Prefab Path Logic ---
            if action == "create" and params.get("saveAsPrefab"): # Check if 'saveAsPrefab' is explicitly True in params
                if "prefabPath" not in params:
                    if "name" not in params or not params["name"]:
                        return {"success": False, "message": "Cannot create default prefab path: 'name' parameter is missing."}
                    # Use the provided prefab_folder (which has a default) and the name to construct the path
                    constructed_path = f"{prefab_folder}/{params['name']}.prefab"
                    # Ensure clean path separators (Unity prefers '/')
                    params["prefabPath"] = constructed_path.replace("\\", "/")
                elif not params["prefabPath"].lower().endswith(".prefab"):
                    return {"success": False, "message": f"Invalid prefab_path: '{params['prefabPath']}' must end with .prefab"}
            # Ensure prefab_folder itself isn't sent if prefabPath was constructed or provided
            # The C# side only needs the final prefabPath
            params.pop("prefab_folder", None) 
            # --------------------------------
            
            # Send the command to Unity via the established connection
            # Use the get_unity_connection function to retrieve the active connection instance
            # Changed "MANAGE_GAMEOBJECT" to "manage_gameobject" to potentially match Unity expectation
            response = get_unity_connection().send_command("manage_gameobject", params)

            # Check if the response indicates success
            # If the response is not successful, raise an exception with the error message
            if response.get("success"):
                return {"success": True, "message": response.get("message", "GameObject operation successful."), "data": response.get("data")}
            else:
                return {"success": False, "message": response.get("error", "An unknown error occurred during GameObject management.")}

        except Exception as e:
            return {"success": False, "message": f"Python error managing GameObject: {str(e)}"} 