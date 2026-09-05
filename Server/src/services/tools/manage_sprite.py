"""
2D sprite animation tool.
Automates: sprite sheet slicing, AnimationClip creation from sliced frames,
and AnimatorController generation.
"""
from typing import Annotated, Any, Literal, get_args

from fastmcp import Context
from mcp.types import ToolAnnotations

from services.registry import mcp_for_unity_tool
from services.tools import get_unity_instance_from_context
from transport.unity_transport import send_with_unity_instance
from transport.legacy.unity_connection import async_send_command_with_retry

SpriteAction = Literal["get_info", "slice_sheet", "setup_clips", "setup_controller", "full_setup"]

VALID_ACTIONS: list[str] = list(get_args(SpriteAction))


@mcp_for_unity_tool(
    group="animation",
    description=(
        "2D sprite animation tool. "
        "get_info: read sprite import settings + return image for vision analysis; "
        "the slice list is paged (page_size / cursor). "
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
    action: Annotated[SpriteAction, "Action to perform."],
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
    overwrite: Annotated[
        bool,
        "Replace an existing .anim or .controller at the target path. Off by default: "
        "without it an existing asset is kept and reported back, not silently replaced.",
    ] = False,
    add_to_scene: Annotated[bool, "Attach Animator + controller to a scene GameObject."] = False,
    scene_target: Annotated[
        str | None,
        "Existing GameObject name to attach Animator to.",
    ] = None,
    # The numbers below are documentation, not enforcement: SpriteParams and
    # SpriteImportSetup.GetInfo are what actually refuse an out-of-range page_size, and
    # this text is what the generated reference publishes to callers. Two copies, so
    # changing the C# bounds means changing this line in the same commit.
    page_size: Annotated[
        int | None,
        "get_info: how many entries of the 'slices' list to return (1-4096, default 512). "
        "A sheet sliced by hand can hold more slices than one response should carry.",
    ] = None,
    cursor: Annotated[
        int | None,
        "get_info: index to start the 'slices' page at. Pass back the 'next_cursor' from "
        "the previous response; absent next_cursor means the list is finished. The image "
        "is returned only on the first page.",
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

    if action_lower in ("slice_sheet", "full_setup") and cols is None and frame_width is None:
        return {"success": False, "message": f"'cols' or 'frame_width' is required for '{action}'. "
                "Use get_info first to retrieve image_base64, analyze the grid visually, then call full_setup with cols/rows."}

    if action_lower == "setup_controller" and not controller_path:
        return {"success": False, "message": "'controller_path' is required for setup_controller (e.g. 'Assets/Animators/Hero.controller')."}

    unity_instance = await get_unity_instance_from_context(ctx)

    # `or None` on the two flags, so a False is dropped rather than sent: the C# side
    # reads a missing key as the default, and forwarding every argument buries the real
    # ones in nulls on the wire.
    optional = {
        "path": path, "cols": cols, "rows": rows,
        "frame_width": frame_width, "frame_height": frame_height,
        "base_name": base_name, "clips": clips,
        "animation_name": animation_name, "output_dir": output_dir,
        "controller_path": controller_path, "page_size": page_size,
        "cursor": cursor, "scene_target": scene_target,
        "overwrite": overwrite or None, "add_to_scene": add_to_scene or None,
    }

    params: dict[str, Any] = {"action": action_lower}
    params.update({k: v for k, v in optional.items() if v is not None})

    result = await send_with_unity_instance(
        async_send_command_with_retry,
        unity_instance,
        "manage_sprite",
        params,
    )
    return result if isinstance(result, dict) else {"success": False, "message": str(result)}
