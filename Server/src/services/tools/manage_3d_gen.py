"""
Defines the manage_3d_gen tool for 3D model generation and object transformation.
Supports generating new objects via Trellis or transforming existing scene objects.
"""
import asyncio
from typing import Annotated, Any, Literal

from fastmcp import Context
from services.registry import mcp_for_unity_tool
from services.tools import get_unity_instance_from_context
from transport.unity_transport import send_with_unity_instance
from transport.legacy.unity_connection import async_send_command_with_retry


def _coerce_vec3(value, default=None):
    """Coerce various formats to [x, y, z] list."""
    if value is None:
        return default
    
    # Already a list
    if isinstance(value, list) and len(value) >= 3:
        try:
            return [float(value[0]), float(value[1]), float(value[2])]
        except (ValueError, TypeError):
            return default
    
    # String format: "[x, y, z]" or "x, y, z"
    if isinstance(value, str):
        s = value.strip()
        if s.startswith("[") and s.endswith("]"):
            s = s[1:-1]
        parts = [p.strip() for p in s.split(",")]
        if len(parts) >= 3:
            try:
                return [float(parts[0]), float(parts[1]), float(parts[2])]
            except (ValueError, TypeError):
                return default
    
    # Dict format: {x: 0, y: 0, z: 0}
    if isinstance(value, dict):
        try:
            return [float(value.get("x", 0)), float(value.get("y", 0)), float(value.get("z", 0))]
        except (ValueError, TypeError):
            return default
    
    return default


@mcp_for_unity_tool(
    description="""Manages 3D model generation and object transformation using Trellis AI.
    
    Actions:
    - generate: Create a NEW 3D object from a text prompt at a specified position
    - transform: Replace an EXISTING scene object with a new model
    - status: Check status of ongoing generation (polling)
    - revert: Revert a transformed object to its previous state
    - revert_original: Revert to the original object (full chain)
    - list_history: List all objects with transform history
    
    IMPORTANT: Position, rotation, and scale MUST be passed as arrays [x, y, z], not as separate values.
    
    Examples:
    - manage_3d_gen(action="generate", target_name="sprinkler", position=[0, 0, 5])
    - manage_3d_gen(action="generate", target_name="wooden chair", position=[2, 0, 3], rotation=[0, 45, 0])
    - manage_3d_gen(action="transform", source_object="Beehive", target_name="fountain")
    - manage_3d_gen(action="revert", target="sprinkler")
    - manage_3d_gen(action="list_history")"""
)
async def manage_3d_gen(
    ctx: Context,
    action: Annotated[
        Literal["generate", "transform", "status", "revert", "revert_original", "list_history"],
        """Action to perform:
        - generate: Create a NEW 3D object from target_name prompt at specified position
        - transform: Replace source_object with target_name model
        - status: Check status of ongoing generation (polling)
        - revert: Revert target object to previous state
        - revert_original: Revert target to original state (full chain)
        - list_history: List all objects with transform history"""
    ] = "generate",
    source_object: Annotated[
        str,
        "Name or path of the scene object to transform/replace (required for 'transform' action)"
    ] | None = None,
    target_name: Annotated[
        str,
        "Name/prompt of the 3D model to generate (e.g., 'sprinkler', 'medieval chair'). Used to search existing assets and as Trellis prompt."
    ] | None = None,
    position: Annotated[
        list[float] | str,
        "World position [x, y, z] for the generated object (for 'generate' action). Defaults to [0, 0, 0]."
    ] | None = None,
    rotation: Annotated[
        list[float] | str,
        "Euler rotation [x, y, z] for the generated object (for 'generate' action). Defaults to [0, 0, 0]."
    ] | None = None,
    scale: Annotated[
        list[float] | str,
        "Scale [x, y, z] for the generated object (for 'generate' action). Defaults to [1, 1, 1]."
    ] | None = None,
    parent: Annotated[
        str,
        "Name or path of the parent object (for 'generate' action)"
    ] | None = None,
    search_existing: Annotated[
        bool,
        "Whether to search for existing assets matching target_name before generating"
    ] = True,
    generate_if_missing: Annotated[
        bool,
        "Whether to generate a new model via Trellis if no existing asset found"
    ] = True,
    target: Annotated[
        str,
        "Target object name/path for revert actions"
    ] | None = None,
) -> dict[str, Any]:
    """Manage 3D model generation and object transformation."""
    
    unity_instance = get_unity_instance_from_context(ctx)
    
    # Coerce vector parameters to proper [x, y, z] format
    position = _coerce_vec3(position)
    rotation = _coerce_vec3(rotation)
    scale = _coerce_vec3(scale)
    
    # Validate parameters based on action
    if action == "generate":
        if not target_name:
            return {
                "success": False,
                "message": "For 'generate' action, 'target_name' parameter is required."
            }
    elif action == "transform":
        if not source_object:
            return {
                "success": False,
                "message": "For 'transform' action, 'source_object' parameter is required."
            }
        if not target_name:
            return {
                "success": False,
                "message": "For 'transform' action, 'target_name' parameter is required."
            }
    elif action in ["revert", "revert_original"]:
        if not target:
            return {
                "success": False,
                "message": f"For '{action}' action, 'target' parameter is required."
            }

    # Prepare parameters for the C# handler
    params_dict = {
        "action": action,
        "source_object": source_object,
        "target_name": target_name,
        "position": position,
        "rotation": rotation,
        "scale": scale,
        "parent": parent,
        "search_existing": search_existing,
        "generate_if_missing": generate_if_missing,
        "target": target,
    }

    # Remove None values
    params_dict = {k: v for k, v in params_dict.items() if v is not None}

    # Get the current asyncio event loop
    loop = asyncio.get_running_loop()

    # Send command to Unity
    result = await send_with_unity_instance(
        async_send_command_with_retry,
        unity_instance,
        "manage_3d_gen",
        params_dict,
        loop=loop
    )
    
    return result if isinstance(result, dict) else {"success": False, "message": str(result)}