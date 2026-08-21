"""
2D sprite animation tool.
Automates: sprite sheet slicing, AnimationClip creation from sliced frames,
and AnimatorController generation.
"""
from typing import Annotated, Any, Literal

from fastmcp import Context
from mcp.types import ToolAnnotations

from services.registry import mcp_for_unity_tool
from services.tools import get_unity_instance_from_context
from transport.unity_transport import send_with_unity_instance
from transport.legacy.unity_connection import async_send_command_with_retry

VALID_ACTIONS = [
    "get_info",
    "slice_sheet",
    "setup_clips",
    "setup_controller",
    "full_setup",
]


@mcp_for_unity_tool(
    group="animation",
    description=(
        "2D sprite animation tool. "
        "get_info: read sprite import settings + return image for vision analysis. "
        "slice_sheet: apply grid slicing to a sprite sheet. "
        "setup_clips: create AnimationClips from sliced sprites. "
        "setup_controller: build AnimatorController with smart complexity (1D blend tree for locomotion, "
        "trigger states for combat, simple state for single animations). "
        "full_setup: one command — slice → clips → controller."
    ),
    annotations=ToolAnnotations(
        title="Manage Sprite",
        destructiveHint=True,
    ),
)
async def manage_sprite(
    ctx: Context,
    action: Annotated[
        Literal["get_info", "slice_sheet", "setup_clips", "setup_controller", "full_setup"],
        "Action to perform.",
    ],
    path: Annotated[
        str | None,
        "Sprite texture asset path (e.g. 'Assets/Sprites/hero_walk.png'). Required for get_info, slice_sheet, setup_clips, full_setup.",
    ] = None,
    cols: Annotated[
        int | None,
        "Number of columns in the sprite sheet grid. Used by slice_sheet and full_setup.",
    ] = None,
    rows: Annotated[
        int | None,
        "Number of rows in the sprite sheet grid. Default: 1.",
    ] = None,
    frame_width: Annotated[
        int | None,
        "Frame width in pixels. Alternative to cols.",
    ] = None,
    frame_height: Annotated[
        int | None,
        "Frame height in pixels. Alternative to rows.",
    ] = None,
    base_name: Annotated[
        str | None,
        "Base name for sliced sprite frames (default: texture filename).",
    ] = None,
    clips: Annotated[
        list[dict[str, Any]] | None,
        "Clip definitions: [{name, start_frame, end_frame, fps (default 12), loop (auto-detect if omitted)}]. "
        "For setup_controller: [{name, path}] where path is the .anim asset path.",
    ] = None,
    animation_name: Annotated[
        str | None,
        "Animation name for full_setup when clips are not specified (all frames = one clip).",
    ] = None,
    output_dir: Annotated[
        str | None,
        "Output directory for .anim and .controller assets (default: same folder as sprite).",
    ] = None,
    controller_path: Annotated[
        str | None,
        "Path for the .controller asset (e.g. 'Assets/Animators/Hero.controller').",
    ] = None,
    overwrite: Annotated[bool, "Overwrite existing controller if it already exists."] = False,
    add_to_scene: Annotated[bool, "Attach Animator + controller to a scene GameObject."] = False,
    scene_target: Annotated[
        str | None,
        "Existing GameObject name to attach Animator to.",
    ] = None,
) -> dict[str, Any]:
    """2D sprite animation tool."""

    action_lower = action.lower() if action else ""

    if action_lower not in VALID_ACTIONS:
        return {
            "success": False,
            "message": f"Unknown action '{action}'. Valid: {', '.join(VALID_ACTIONS)}",
        }

    # Python-side validation
    if action_lower in ("get_info", "slice_sheet", "setup_clips", "full_setup") and not path:
        return {"success": False, "message": f"'path' is required for action '{action}'."}

    if action_lower in ("slice_sheet", "full_setup") and not cols and not frame_width:
        return {"success": False, "message": f"'cols' or 'frame_width' is required for '{action}'. "
                "Use get_info first to retrieve image_base64, analyze the grid visually, then call full_setup with cols/rows."}

    # The Unity side is the authority here - it composes the asset path and refuses the
    # name again. Checking it up front turns a round-trip into an immediate answer, and a
    # separator in a clip name is wrong under every configuration.
    for clip in clips or []:
        name = clip.get("name") if isinstance(clip, dict) else None
        if name and ("/" in name or "\\" in name):
            return {"success": False,
                    "message": f"Clip name '{name}' cannot contain a path separator; "
                               "use 'output_dir' to choose where clips are written."}

    if action_lower == "setup_controller" and not controller_path:
        return {"success": False, "message": "'controller_path' is required for setup_controller (e.g. 'Assets/Animators/Hero.controller')."}

    unity_instance = await get_unity_instance_from_context(ctx)

    params: dict[str, Any] = {"action": action_lower}

    if path is not None:            params["path"]           = path
    if cols is not None:            params["cols"]           = cols
    if rows is not None:            params["rows"]           = rows
    if frame_width is not None:     params["frame_width"]    = frame_width
    if frame_height is not None:    params["frame_height"]   = frame_height
    if base_name is not None:       params["base_name"]      = base_name
    if clips is not None:           params["clips"]          = clips
    if animation_name is not None:  params["animation_name"] = animation_name
    if output_dir is not None:      params["output_dir"]     = output_dir
    if controller_path is not None: params["controller_path"]= controller_path
    if overwrite:                   params["overwrite"]      = True
    if add_to_scene:                params["add_to_scene"]   = True
    if scene_target is not None:    params["scene_target"]   = scene_target

    result = await send_with_unity_instance(
        async_send_command_with_retry,
        unity_instance,
        "manage_sprite",
        params,
    )
    return result if isinstance(result, dict) else {"success": False, "message": str(result)}
