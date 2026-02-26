"""
Defines the manage_ui tool for creating and managing Unity UI Toolkit elements.

Supports creating UXML documents and USS stylesheets, attaching UIDocument
components to GameObjects, and inspecting visual trees.
"""
import base64
import os
from typing import Annotated, Any, Literal

from fastmcp import Context
from mcp.types import ToolAnnotations

from services.registry import mcp_for_unity_tool
from services.tools import get_unity_instance_from_context
from services.tools.refresh_unity import send_mutation
from transport.unity_transport import send_with_unity_instance
from transport.legacy.unity_connection import async_send_command_with_retry


_VALID_EXTENSIONS = {".uxml", ".uss"}


@mcp_for_unity_tool(
    description=(
        "Manages Unity UI Toolkit elements (UXML documents, USS stylesheets, UIDocument components). "
        "Read-only actions: ping, read, get_visual_tree, list. "
        "Modifying actions: create, update, delete, attach_ui_document, detach_ui_document, create_panel_settings, update_panel_settings, modify_visual_element.\n"
        "Visual actions: render_ui (captures UI panel to a PNG screenshot for self-evaluation).\n"
        "Structural actions: link_stylesheet (adds a Style src reference to a UXML file).\n\n"
        "UI Toolkit workflow:\n"
        "1. Use list to discover existing UI assets\n"
        "2. Create a UXML file (structure, like HTML)\n"
        "3. Create a USS file (styling, like CSS)\n"
        "4. Link stylesheet to UXML via link_stylesheet\n"
        "5. Attach UIDocument to a GameObject with the UXML source\n"
        "6. Use get_visual_tree to inspect the result\n"
        "7. Use modify_visual_element to change text, classes, or inline styles on live elements\n"
        "8. Use render_ui to capture a visual preview for self-evaluation\n"
        "   - In play mode: first call queues a WaitForEndOfFrame screen capture and returns pending=true;\n"
        "     call render_ui a second time to retrieve the saved PNG (hasContent will be true).\n"
        "   - In editor mode: assigns a RenderTexture to PanelSettings (best-effort; may stay blank).\n"
        "9. Use detach_ui_document to remove UIDocument from a GameObject\n"
        "10. Use delete to remove .uxml/.uss files"
    ),
    annotations=ToolAnnotations(
        title="Manage UI",
        destructiveHint=True,
    ),
)
async def manage_ui(
    ctx: Context,
    action: Annotated[Literal[
        "ping",
        "create",
        "read",
        "update",
        "delete",
        "attach_ui_document",
        "detach_ui_document",
        "create_panel_settings",
        "update_panel_settings",
        "get_visual_tree",
        "render_ui",
        "link_stylesheet",
        "list",
        "modify_visual_element",
    ], "Action to perform."],

    # File operations (create/read/update/link_stylesheet)
    path: Annotated[str,
                     "Assets-relative path (e.g., 'Assets/UI/MainMenu.uxml' or 'Assets/UI/Styles.uss'). "
                     "For render_ui: optional UXML path to render directly without a scene GameObject."] | None = None,
    contents: Annotated[str,
                         "File content (UXML or USS markup). Plain text - encoding handled automatically."] | None = None,

    # attach_ui_document / get_visual_tree / render_ui
    target: Annotated[str,
                       "Target GameObject name or path for attach_ui_document / get_visual_tree / render_ui."] | None = None,
    source_asset: Annotated[str,
                             "Path to UXML VisualTreeAsset (e.g., 'Assets/UI/MainMenu.uxml')."] | None = None,
    panel_settings: Annotated[str,
                               "Path to PanelSettings asset. Auto-creates default if omitted."] | None = None,
    sort_order: Annotated[int,
                           "UIDocument sort order (default 0)."] | None = None,

    # create_panel_settings
    scale_mode: Annotated[Literal[
        "ConstantPixelSize",
        "ConstantPhysicalSize",
        "ScaleWithScreenSize",
    ], "Panel scale mode. Legacy shorthand; prefer using 'settings' dict."] | None = None,
    reference_resolution: Annotated[dict[str, int],
                                     "Reference resolution as {width, height}. Legacy shorthand; prefer using 'settings' dict."] | None = None,
    settings: Annotated[dict[str, Any],
                         "Generic PanelSettings properties dict for create_panel_settings. "
                         "Keys: scaleMode (ConstantPixelSize|ConstantPhysicalSize|ScaleWithScreenSize), "
                         "referenceResolution ({width,height}), screenMatchMode (MatchWidthOrHeight|ShrinkToFit|ExpandToFill), "
                         "match (0-1 float), referenceDpi, fallbackDpi, sortingOrder, targetDisplay, "
                         "clearColor (bool), colorClearValue (#RRGGBB or {r,g,b,a}), clearDepthStencil, "
                         "themeStyleSheet (asset path), dynamicAtlasSettings ({minAtlasSize,maxAtlasSize,maxSubTextureSize,activeFilters})."
                         ] | None = None,

    # get_visual_tree
    max_depth: Annotated[int,
                          "Max depth to traverse visual tree (default 10)."] | None = None,

    # render_ui
    width: Annotated[int,
                      "Render width in pixels (default 1920). For render_ui."] | None = None,
    height: Annotated[int,
                       "Render height in pixels (default 1080). For render_ui."] | None = None,
    include_image: Annotated[bool,
                              "Return inline base64 PNG in the response (default false). For render_ui."] | None = None,
    max_resolution: Annotated[int,
                               "Max resolution for inline base64 image (default 640). For render_ui."] | None = None,
    screenshot_file_name: Annotated[str,
                                     "Custom file name for the render output (default: auto-generated). "
                                     "For render_ui."] | None = None,

    # link_stylesheet
    stylesheet: Annotated[str,
                           "Path to USS stylesheet to link (e.g., 'Assets/UI/Styles.uss'). "
                           "For link_stylesheet."] | None = None,

    # list
    filter_type: Annotated[str,
                            "Filter UI assets by type: 'uxml', 'uss', 'PanelSettings', or omit for all. "
                            "For list."] | None = None,
    page_size: Annotated[int,
                          "Number of results per page (default 50). For list."] | None = None,
    page_number: Annotated[int,
                            "Page number, 1-based (default 1). For list."] | None = None,

    # modify_visual_element
    element_name: Annotated[str,
                             "Name of the visual element to modify (the 'name' attribute in UXML). "
                             "For modify_visual_element."] | None = None,
    text: Annotated[str,
                     "New text content for Label/Button elements. For modify_visual_element."] | None = None,
    add_classes: Annotated[list[str],
                            "USS class names to add to the element. For modify_visual_element."] | None = None,
    remove_classes: Annotated[list[str],
                               "USS class names to remove from the element. For modify_visual_element."] | None = None,
    toggle_classes: Annotated[list[str],
                               "USS class names to toggle on the element. For modify_visual_element."] | None = None,
    style: Annotated[dict[str, Any],
                      "Inline styles to set (e.g., {'backgroundColor': '#FF0000', 'fontSize': 24}). "
                      "For modify_visual_element."] | None = None,
    enabled: Annotated[bool,
                        "Set element enabled/disabled state. For modify_visual_element."] | None = None,
    visible: Annotated[bool,
                        "Set element visibility (display: flex/none). For modify_visual_element."] | None = None,
    tooltip: Annotated[str,
                        "Set element tooltip text. For modify_visual_element."] | None = None,

) -> dict[str, Any]:
    unity_instance = get_unity_instance_from_context(ctx)

    action_lower = action.lower()

    # --- Path validation for file operations ---
    if action_lower in ("create", "read", "update", "delete") and path:
        norm_path = os.path.normpath(
            (path or "").replace("\\", "/")).replace("\\", "/")
        if ".." in norm_path.split("/"):
            return {"success": False, "message": "path must not contain traversal sequences."}
        parts = norm_path.split("/")
        if not parts or parts[0].lower() != "assets":
            return {"success": False, "message": f"path must be under 'Assets/'; got '{path}'."}
        ext = os.path.splitext(path)[1].lower()
        if ext not in _VALID_EXTENSIONS:
            return {"success": False, "message": f"Invalid file extension '{ext}'. Must be .uxml or .uss."}

    # --- Build params dict ---
    params_dict: dict[str, Any] = {
        "action": action_lower,
    }

    # File operations: base64-encode contents for transport
    if action_lower in ("create", "update") and contents:
        params_dict["encodedContents"] = base64.b64encode(
            contents.encode("utf-8")).decode("utf-8")
        params_dict["contentsEncoded"] = True
    elif action_lower in ("create", "update") and not contents:
        # Let Unity-side validate and return the error
        pass

    if path is not None:
        params_dict["path"] = path
    if target is not None:
        params_dict["target"] = target
    if source_asset is not None:
        params_dict["sourceAsset"] = source_asset
    if panel_settings is not None:
        params_dict["panelSettings"] = panel_settings
    if sort_order is not None:
        params_dict["sortOrder"] = sort_order
    if scale_mode is not None:
        params_dict["scaleMode"] = scale_mode
    if reference_resolution is not None:
        params_dict["referenceResolution"] = reference_resolution
    if settings is not None:
        params_dict["settings"] = settings
    if max_depth is not None:
        params_dict["maxDepth"] = max_depth

    # render_ui params
    if width is not None:
        params_dict["width"] = width
    if height is not None:
        params_dict["height"] = height
    if include_image is not None:
        params_dict["include_image"] = include_image
    if max_resolution is not None:
        params_dict["max_resolution"] = max_resolution
    if screenshot_file_name is not None:
        params_dict["file_name"] = screenshot_file_name

    # link_stylesheet params
    if stylesheet is not None:
        params_dict["stylesheet"] = stylesheet

    # list params
    if filter_type is not None:
        params_dict["filterType"] = filter_type
    if page_size is not None:
        params_dict["pageSize"] = page_size
    if page_number is not None:
        params_dict["pageNumber"] = page_number

    # modify_visual_element params
    if element_name is not None:
        params_dict["elementName"] = element_name
    if text is not None:
        params_dict["text"] = text
    if add_classes is not None:
        params_dict["addClasses"] = add_classes
    if remove_classes is not None:
        params_dict["removeClasses"] = remove_classes
    if toggle_classes is not None:
        params_dict["toggleClasses"] = toggle_classes
    if style is not None:
        params_dict["style"] = style
    if enabled is not None:
        params_dict["enabled"] = enabled
    if visible is not None:
        params_dict["visible"] = str(visible).lower()
    if tooltip is not None:
        params_dict["tooltip"] = tooltip

    # --- Route to Unity ---
    is_mutation = action_lower in (
        "create", "update", "delete", "attach_ui_document", "detach_ui_document",
        "create_panel_settings", "update_panel_settings", "render_ui", "link_stylesheet", "modify_visual_element",
    )

    if is_mutation:
        result = await send_mutation(
            ctx, unity_instance, "manage_ui", params_dict,
        )
    else:
        result = await send_with_unity_instance(
            async_send_command_with_retry,
            unity_instance,
            "manage_ui",
            params_dict,
        )

    if isinstance(result, dict):
        # Decode base64 contents in read responses
        if action_lower == "read" and result.get("success"):
            data = result.get("data", {})
            if data.get("contentsEncoded") and data.get("encodedContents"):
                try:
                    decoded = base64.b64decode(
                        data["encodedContents"]).decode("utf-8")
                    data["contents"] = decoded
                    del data["encodedContents"]
                    del data["contentsEncoded"]
                except Exception:
                    pass
        return result

    return {"success": False, "message": str(result)}
