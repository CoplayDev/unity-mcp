"""
Defines the manage_material tool for interacting with Unity materials.
"""
import json
from typing import Annotated, Any, Literal, Union

from fastmcp import Context
from services.registry import mcp_for_unity_tool
from services.tools import get_unity_instance_from_context
from services.tools.utils import parse_json_payload
from transport.unity_transport import send_with_unity_instance
from transport.legacy.unity_connection import async_send_command_with_retry


@mcp_for_unity_tool(
    description="Manages Unity materials (set properties, colors, shaders, etc)."
)
async def manage_material(
    ctx: Context,
    action: Annotated[Literal[
        "ping",
        "create",
        "set_material_shader_property",
        "set_material_color",
        "assign_material_to_renderer",
        "set_renderer_color",
        "get_material_info"
    ], "Action to perform."],
    
    # Common / Shared
    material_path: Annotated[str, "Path to material asset (Assets/...)"] | None = None,
    property: Annotated[str, "Shader property name (e.g., _BaseColor, _MainTex)"] | None = None,

    # create
    shader: Annotated[str, "Shader name (default: Standard)"] | None = None,
    properties: Annotated[Union[dict[str, Any], str], "Initial properties to set {name: value}."] | None = None,
    
    # set_material_shader_property
    value: Annotated[Union[list, float, int, str, bool, None], "Value to set (color array, float, texture path/instruction)"] | None = None,
    
    # set_material_color / set_renderer_color
    color: Annotated[Union[list[float], list[int], str], "Color as [r,g,b] or [r,g,b,a]."] | None = None,
    
    # assign_material_to_renderer / set_renderer_color
    target: Annotated[str, "Target GameObject (name, path, or find instruction)"] | None = None,
    search_method: Annotated[Literal["by_name", "by_path", "by_tag", "by_layer", "by_component"], "Search method for target"] | None = None,
    slot: Annotated[int | str, "Material slot index"] | None = None,
    mode: Annotated[Literal["shared", "instance", "property_block"], "Assignment/modification mode"] | None = None,
    
) -> dict[str, Any]:
    unity_instance = get_unity_instance_from_context(ctx)

    # Parse inputs that might be stringified JSON
    color = parse_json_payload(color)
    properties = parse_json_payload(properties)
    value = parse_json_payload(value)

    # Coerce slot to int if it's a string
    if slot is not None:
        if isinstance(slot, str):
            try:
                slot = int(slot)
            except ValueError:
                return {
                    "success": False,
                    "message": f"Invalid slot value: '{slot}' must be a valid integer"
                }

    # Prepare parameters for the C# handler
    params_dict = {
        "action": action.lower(),
        "materialPath": material_path,
        "shader": shader,
        "properties": properties,
        "property": property,
        "value": value,
        "color": color,
        "target": target,
        "searchMethod": search_method,
        "slot": slot,
        "mode": mode
    }

    # Remove None values
    params_dict = {k: v for k, v in params_dict.items() if v is not None}

    # Use centralized async retry helper with instance routing
    result = await send_with_unity_instance(
        async_send_command_with_retry,
        unity_instance,
        "manage_material",
        params_dict,
    )
    
    return result if isinstance(result, dict) else {"success": False, "message": str(result)}
