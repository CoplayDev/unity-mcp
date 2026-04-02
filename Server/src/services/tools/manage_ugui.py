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
    group="core",
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
        "modify_element",
        "ensure_canvas",
    ], "Action to perform."],
    
    # create_element
    type: Annotated[Literal[
        "Button", "Image", "Text", "ScrollView", "Slider", 
        "Toggle", "Panel", "Dropdown", "InputField", "Scrollbar", "RawImage", "Empty"
    ], "Type of UI element to create."] | None = None,
    name: Annotated[str, "Name of the GameObject."] | None = None,
    parent: Annotated[str, "Parent GameObject (name, path, or instance ID)."] | None = None,

    # set_layout / modify_element
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

    # Visual Properties
    sprite: Annotated[str, "Asset path for the Image sprite."] | None = None,
    texture: Annotated[str, "Asset path for the RawImage texture."] | None = None,
    color: Annotated[str, "Color in Hex (#RRGGBB) or RGBA format."] | None = None,
    text: Annotated[str, "Text content for Text-based elements."] | None = None,
    fontSize: Annotated[int, "Font size for Text elements."] | None = None,
    font: Annotated[str, "Asset path for the Font."] | None = None,
    alignment: Annotated[str, "Text alignment (e.g., UpperLeft, MiddleCenter, LowerRight)."] | None = None,
    raycastTarget: Annotated[bool, "Whether the element blocks raycasts."] | None = None,
    preserveAspect: Annotated[bool, "Whether the image preserves its aspect ratio."] | None = None,

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

    if sprite: params_dict["sprite"] = sprite
    if texture: params_dict["texture"] = texture
    if color: params_dict["color"] = color
    if text: params_dict["text"] = text
    if fontSize: params_dict["fontSize"] = fontSize
    if font: params_dict["font"] = font
    if alignment: params_dict["alignment"] = alignment
    if raycastTarget is not None: params_dict["raycastTarget"] = raycastTarget
    if preserveAspect is not None: params_dict["preserveAspect"] = preserveAspect

    return await send_mutation(
        ctx, unity_instance, "manage_ugui", params_dict,
    )
