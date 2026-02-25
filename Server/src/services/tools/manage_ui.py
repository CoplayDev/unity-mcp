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
        "Read-only actions: ping, read, get_visual_tree. "
        "Modifying actions: create, update, attach_ui_document, create_panel_settings.\n\n"
        "UI Toolkit workflow:\n"
        "1. Create a UXML file (structure, like HTML)\n"
        "2. Create a USS file (styling, like CSS)\n"
        "3. Attach UIDocument to a GameObject with the UXML source\n"
        "4. Use get_visual_tree to inspect the result"
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
        "attach_ui_document",
        "create_panel_settings",
        "get_visual_tree",
    ], "Action to perform."],

    # File operations (create/read/update)
    path: Annotated[str,
                     "Assets-relative path (e.g., 'Assets/UI/MainMenu.uxml' or 'Assets/UI/Styles.uss')"] | None = None,
    contents: Annotated[str,
                         "File content (UXML or USS markup). Plain text - encoding handled automatically."] | None = None,

    # attach_ui_document
    target: Annotated[str,
                       "Target GameObject name or path for attach_ui_document / get_visual_tree."] | None = None,
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
    ], "Panel scale mode."] | None = None,
    reference_resolution: Annotated[dict[str, int],
                                     "Reference resolution as {width, height} for ScaleWithScreenSize."] | None = None,

    # get_visual_tree
    max_depth: Annotated[int,
                          "Max depth to traverse visual tree (default 10)."] | None = None,

) -> dict[str, Any]:
    unity_instance = get_unity_instance_from_context(ctx)

    action_lower = action.lower()

    # --- Path validation for file operations ---
    if action_lower in ("create", "read", "update") and path:
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
    if max_depth is not None:
        params_dict["maxDepth"] = max_depth

    # --- Route to Unity ---
    is_mutation = action_lower in ("create", "update", "attach_ui_document", "create_panel_settings")

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
