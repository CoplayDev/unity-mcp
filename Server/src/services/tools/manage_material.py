"""
Defines the manage_material tool for interacting with Unity materials.
"""
import json
from typing import Annotated, Any, Literal

from fastmcp import Context
from mcp.types import ToolAnnotations

from services.registry import mcp_for_unity_tool
from services.tools import get_unity_instance_from_context
from services.tools.utils import parse_json_payload, coerce_int, normalize_properties
from transport.unity_transport import send_with_unity_instance
from transport.legacy.unity_connection import async_send_command_with_retry


def _normalize_color(value: Any) -> tuple[list[float] | None, str | None]:
    """
    Normalize color parameter to [r, g, b] or [r, g, b, a] format.
    Returns (parsed_color, error_message).
    """
    if value is None:
        return None, None

    # Already a list - validate
    if isinstance(value, (list, tuple)):
        if len(value) in (3, 4):
            try:
                return [float(c) for c in value], None
            except (ValueError, TypeError):
                return None, f"color values must be numbers, got {value}"
        return None, f"color must have 3 or 4 components, got {len(value)}"

    # Try parsing as string
    if isinstance(value, str):
        if value in ("[object Object]", "undefined", "null", ""):
            return None, f"color received invalid value: '{value}'. Expected [r, g, b] or [r, g, b, a]"

        parsed = parse_json_payload(value)
        if isinstance(parsed, (list, tuple)) and len(parsed) in (3, 4):
            try:
                return [float(c) for c in parsed], None
            except (ValueError, TypeError):
                return None, f"color values must be numbers, got {parsed}"
        return None, f"Failed to parse color string: {value}"

    return None, f"color must be a list or JSON string, got {type(value).__name__}"


@mcp_for_unity_tool(
    description="Manages Unity materials (set properties, colors, shaders, etc). Read-only actions: ping, get_material_info. Modifying actions: create, set_material_shader_property, set_material_color, assign_material_to_renderer, set_renderer_color.",
    annotations=ToolAnnotations(
        title="Manage Material",
        destructiveHint=True,
    ),
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
    material_path: Annotated[str,
                             "Path to material asset (Assets/...)"] | None = None,
    property: Annotated[str,
                        "Shader property name (e.g., _BaseColor, _MainTex)"] | None = None,

    # create
    shader: Annotated[str, "Shader name (default: Standard)"] | None = None,
    properties: Annotated[dict[str, Any],
                          "Initial properties to set as {name: value} dict."] | None = None,

    # set_material_shader_property
    value: Annotated[list | float | int | str | bool | None,
                     "Value to set (color array, float, texture path/instruction)"] | None = None,

    # set_material_color / set_renderer_color
    color: Annotated[list[float],
                     "Color as [r, g, b] or [r, g, b, a] array."] | None = None,

    # assign_material_to_renderer / set_renderer_color
    target: Annotated[str,
                      "Target GameObject (name, path, or find instruction)"] | None = None,
    search_method: Annotated[Literal["by_name", "by_path", "by_tag",
                                     "by_layer", "by_component"], "Search method for target"] | None = None,
    slot: Annotated[int, "Material slot index (0-based)"] | None = None,
    mode: Annotated[Literal["shared", "instance", "property_block"],
                    "Assignment/modification mode"] | None = None,

) -> dict[str, Any]:
    unity_instance = get_unity_instance_from_context(ctx)

    # --- Normalize color with validation ---
    color, color_error = _normalize_color(color)
    if color_error:
        return {"success": False, "message": color_error}

    # --- Normalize properties with validation ---
    properties, props_error = normalize_properties(properties)
    if props_error:
        return {"success": False, "message": props_error}

    # --- Normalize value (parse JSON if string) ---
    value = parse_json_payload(value)
    if isinstance(value, str) and value in ("[object Object]", "undefined"):
        return {"success": False, "message": f"value received invalid input: '{value}'"}

    # --- Normalize slot to int ---
    slot = coerce_int(slot)

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
