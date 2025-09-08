"""
Defines the manage_prefab tool for comprehensive Unity prefab management.
"""
from typing import Dict, Any, List, Optional, Literal, Union
from mcp.server.fastmcp import FastMCP, Context
from unity_connection import send_command_with_retry
import json

def register_manage_prefab_tools(mcp: FastMCP):
    """Registers the manage_prefab tool with the MCP server."""
    print("[DEBUG] Starting manage_prefab tool registration...")
    
    @mcp.tool()
    async def manage_prefab(
        ctx: Context,
        action: Literal["create", "instantiate", "open", "close", "save", "modify", "find", "get_info", "variant", "unpack"],
        prefab_path: Optional[str] = None,
        source_object: Optional[str] = None,
        source_type: Literal["gameobject", "empty"] = "gameobject",
        prefab_name: Optional[str] = None,
        components: Optional[List[str]] = None,
        position: Optional[List[float]] = None,
        rotation: Optional[List[float]] = None,
        scale: Optional[List[float]] = None,
        parent: Optional[str] = None,
        in_context: bool = False,
        save_changes: bool = True,
        search_term: Optional[str] = None,
        search_type: Literal["name", "path"] = "name",
        include_variants: bool = True,
        base_prefab_path: Optional[str] = None,
        variant_path: Optional[str] = None,
        target_object: Optional[str] = None,
        unpack_completely: bool = False,
        # New parameters for modify action
        modify_target: Optional[str] = None,
        modify_position: Optional[List[float]] = None,
        modify_rotation: Optional[List[float]] = None,
        modify_scale: Optional[List[float]] = None,
        modify_active: Optional[bool] = None,
        modify_name: Optional[str] = None,
        modify_tag: Optional[str] = None,
        modify_layer: Optional[str] = None,
        auto_save: bool = True
    ) -> Dict[str, Any]:
        """
        Manages Unity prefabs with comprehensive operations.
        
        This tool consolidates all prefab-related functionality that was previously
        scattered across manage_gameobject and manage_asset tools.

        Args:
            action: The prefab operation to perform
            prefab_path: Path to the prefab asset (e.g., "Assets/Prefabs/MyPrefab.prefab")
            source_object: GameObject name or instanceID to create prefab from (for create action)
            source_type: Type of prefab to create ("gameobject" from existing object, "empty" new)
            prefab_name: Name for the prefab (used with empty prefabs)
            components: List of component names to add to empty prefabs
            position: [x, y, z] position for instantiated prefabs
            rotation: [x, y, z] rotation for instantiated prefabs  
            scale: [x, y, z] scale for instantiated prefabs
            parent: Parent GameObject name for instantiated prefabs
            in_context: Open prefab in context mode (for open action)
            save_changes: Whether to save changes when closing prefab
            search_term: Term to search for (for find action)
            search_type: How to search ("name" or "path")
            include_variants: Include prefab variants in search results
            base_prefab_path: Base prefab for creating variants
            variant_path: Path for the new variant
            target_object: GameObject to unpack (for unpack action)
            unpack_completely: Unpack completely vs just outermost root

        Returns:
            Success/error response with operation results

        Examples:
            # Create prefab from existing GameObject
            manage_prefab(action="create", source_object="MyGameObject", prefab_path="Assets/Prefabs/MyPrefab.prefab")
            
            # Create empty prefab with components
            manage_prefab(action="create", source_type="empty", prefab_name="EmptyPrefab", 
                         prefab_path="Assets/Prefabs/Empty.prefab", components=["Rigidbody", "Collider"])
            
            # Instantiate prefab
            manage_prefab(action="instantiate", prefab_path="Assets/Prefabs/MyPrefab.prefab", 
                         position=[0, 5, 0], parent="Container")
            
            # Open prefab for editing
            manage_prefab(action="open", prefab_path="Assets/Prefabs/MyPrefab.prefab")
            
            # Close prefab and save changes
            manage_prefab(action="close", save_changes=True)
            
            # Find prefabs by name
            manage_prefab(action="find", search_term="Player", include_variants=False)
            
            # Get prefab information
            manage_prefab(action="get_info", prefab_path="Assets/Prefabs/MyPrefab.prefab")
            
            # Create prefab variant
            manage_prefab(action="variant", base_prefab_path="Assets/Prefabs/Base.prefab", 
                         variant_path="Assets/Prefabs/Variant.prefab")
            
            # Unpack prefab instance
            manage_prefab(action="unpack", target_object="MyPrefabInstance", unpack_completely=True)
            
            # Modify objects inside a prefab (opens, modifies, saves automatically)
            manage_prefab(action="modify", prefab_path="Assets/Prefabs/MyPrefab.prefab", 
                         modify_target="PipeTop", modify_scale=[0.5, 0.5, 0.5])
            
            # Modify multiple properties at once
            manage_prefab(action="modify", prefab_path="Assets/Prefabs/Player.prefab",
                         modify_target="PlayerModel", modify_scale=[1.2, 1.2, 1.2], 
                         modify_position=[0, 0.5, 0], auto_save=True)
        """
        
        # Build parameters dictionary for Unity
        params_dict = {
            "action": action,
        }
        
        # Add optional parameters based on action
        if prefab_path is not None:
            params_dict["prefabPath"] = prefab_path
        if source_object is not None:
            params_dict["sourceObject"] = source_object
        if source_type != "gameobject":
            params_dict["sourceType"] = source_type
        if prefab_name is not None:
            params_dict["prefabName"] = prefab_name
        if components is not None:
            params_dict["components"] = components
        if position is not None:
            params_dict["position"] = position
        if rotation is not None:
            params_dict["rotation"] = rotation  
        if scale is not None:
            params_dict["scale"] = scale
        if parent is not None:
            params_dict["parent"] = parent
        if in_context:
            params_dict["inContext"] = in_context
        if not save_changes:
            params_dict["saveChanges"] = save_changes
        if search_term is not None:
            params_dict["searchTerm"] = search_term
        if search_type != "name":
            params_dict["searchType"] = search_type
        if not include_variants:
            params_dict["includeVariants"] = include_variants
        if base_prefab_path is not None:
            params_dict["basePrefabPath"] = base_prefab_path
        if variant_path is not None:
            params_dict["variantPath"] = variant_path
        if target_object is not None:
            params_dict["targetObject"] = target_object
        if unpack_completely:
            params_dict["unpackCompletely"] = unpack_completely
        
        # Add modify-specific parameters
        if modify_target is not None:
            params_dict["modifyTarget"] = modify_target
        if modify_position is not None:
            params_dict["modifyPosition"] = modify_position
        if modify_rotation is not None:
            params_dict["modifyRotation"] = modify_rotation
        if modify_scale is not None:
            params_dict["modifyScale"] = modify_scale
        if modify_active is not None:
            params_dict["modifyActive"] = modify_active
        if modify_name is not None:
            params_dict["modifyName"] = modify_name
        if modify_tag is not None:
            params_dict["modifyTag"] = modify_tag
        if modify_layer is not None:
            params_dict["modifyLayer"] = modify_layer
        if not auto_save:
            params_dict["autoSave"] = auto_save

        # Send command to Unity
        resp = send_command_with_retry("manage_prefab", params_dict)
        return resp if isinstance(resp, dict) else {"success": False, "message": str(resp)}
    
    print("[DEBUG] manage_prefab tool registered successfully!")