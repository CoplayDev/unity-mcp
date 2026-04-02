"""
Defines the manage_ugui tool for professional UGUI (Canvas-based) element management.
Addresses RectTransform issues, hierarchical templates (ScrollView), and scale safety.
"""
from typing import Annotated, Any, Literal

from fastmcp import Context
from mcp.types import ToolAnnotations

from services.registry import mcp_for_unity_tool
from services.tools import get_unity_instance_from_context
from services.tools.refresh_unity import send_mutation


@mcp_for_unity_tool(
    group="ui",
    description=(
        "Manages Unity UGUI (Canvas-based) elements. Preferred over manage_gameobject for UI.\n"
        "Actions: create_element, set_layout, ensure_canvas.\n\n"
        "Key Benefits:\n"
        "- Guarantees RectTransform creation for all elements.\n"
        "- Automatically builds complex hierarchies for ScrollViews, Sliders, and Dropdowns.\n"
        "- Enforces scale (1,1,1) safety for all UI elements.\n"
        "- Provides semantic anchor presets (e.g., stretch_stretch, top_center)."
    ),
    annotations=ToolAnnotations(
        title="Manage UGUI",
        destructiveHint=True,
    ),
)
async def manage_ugui(
    ctx: Context,
    action: Annotated[Literal[
        "create_element",
        "set_layout",
        "ensure_canvas",
    ], "Action to perform."],
    
    # create_element
    type: Annotated[Literal[
        "Button", "Image", "Text", "ScrollView", "Slider", 
        "Toggle", "Panel", "Dropdown", "InputField", "Scrollbar", "RawImage"
    ], "Type of UI element to create."] | None = None,
    name: Annotated[str, "Name of the GameObject."] | None = None,
    parent: Annotated[str, "Parent GameObject (name, path, or instance ID)."] | None = None,

    # set_layout
    target: Annotated[str, "Target UI element to modify."] | None = None,
    anchor_preset: Annotated[Literal[
        "stretch_stretch", "top_left", "top_center", "top_right",
        "middle_left", "middle_center", "middle_right",
        "bottom_left", "bottom_center", "bottom_right",
        "horiz_stretch_top", "horiz_stretch_middle", "horiz_stretch_bottom",
        "vert_stretch_left", "vert_stretch_center", "vert_stretch_right"
    ], "Predefined anchor and pivot settings."] | None = None,
    size_delta: Annotated[dict[str, float], "Size delta as {x, y}."] | None = None,
    anchored_position: Annotated[dict[str, float], "Anchored position as {x, y}."] | None = None,
    pivot_x: Annotated[float, "Pivot X coordinate (0-1)."] | None = None,
    pivot_y: Annotated[float, "Pivot Y coordinate (0-1)."] | None = None,

) -> dict[str, Any]:
    unity_instance = await get_unity_instance_from_context(ctx)

    params_dict: dict[str, Any] = {
        "action": action,
    }

    if type: params_dict["type"] = type
    if name: params_dict["name"] = name
    if parent: params_dict["parent"] = parent
    if target: params_dict["target"] = target
    if anchor_preset: params_dict["anchorPreset"] = anchor_preset
    if size_delta: params_dict["sizeDelta"] = size_delta
    if anchored_position: params_dict["anchoredPosition"] = anchored_position
    if pivot_x is not None: params_dict["pivotX"] = pivot_x
    if pivot_y is not None: params_dict["pivotY"] = pivot_y

    return await send_mutation(
        ctx, unity_instance, "manage_ugui", params_dict,
    )
