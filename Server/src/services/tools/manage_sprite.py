"""
2D sprite animation tool.
Automates: sprite sheet slicing, AnimationClip creation from sliced frames,
and AnimatorController generation.
"""
import json
from typing import Annotated, Any, Literal, get_args

from fastmcp import Context
from fastmcp.server.server import ToolResult
from mcp.types import ImageContent, TextContent, ToolAnnotations

from services.registry import mcp_for_unity_tool
from services.tools import get_unity_instance_from_context
from transport.unity_transport import send_with_unity_instance
from transport.legacy.unity_connection import async_send_command_with_retry

SpriteAction = Literal["get_info", "slice_sheet", "setup_clips", "setup_controller", "full_setup"]

VALID_ACTIONS: list[str] = list(get_args(SpriteAction))


def _sprite_image_result(result: dict[str, Any], image_base64: str) -> ToolResult:
    mime = "image/png"
    payload = image_base64
    if image_base64.startswith("data:") and ";base64," in image_base64:
        prefix, payload = image_base64.split(";base64,", 1)
        mime = prefix[5:] or "image/png"

    meta = result.copy()
    meta.pop("image_base64")
    return ToolResult(content=[
        TextContent(type="text", text=json.dumps(meta)),
        ImageContent(type="image", data=payload, mimeType=mime),
    ])


@mcp_for_unity_tool(
    group="animation",
    description=(
        "Slice 2D sprite sheets and build AnimationClips and an AnimatorController from the frames. "
        "Actions: get_info returns a sheet's import settings and slices (paged with page_size / cursor) "
        "and, for a PNG or JPEG source, the sheet as an image block for vision analysis; "
        "slice_sheet applies a grid, replacing the sheet's existing slices; "
        "setup_clips creates AnimationClips from the slices; "
        "setup_controller builds a controller from clip names (idle = default state, one walk/run "
        "clip = a plain state and two or more = a Speed-driven 1D blend tree, jump/attack/hurt-type "
        "names = trigger states that fire from any state, other names = plain states); "
        "full_setup runs slice → clips → controller in one call."
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
        "Number of rows in the sprite sheet grid. Default: 1, or derived from frame_height when that is given.",
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
    filter_mode: Annotated[
        Literal["point", "bilinear", "trilinear"] | None,
        "slice_sheet and full_setup: texture filter the sliced sheet is imported with, in lowercase "
        "(get_info reports it as Point, Bilinear or Trilinear). Default: point, which keeps pixel art "
        "sharp; bilinear or trilinear suits high-resolution art. Every slice sets it, so a filter set "
        "by hand does not survive a re-slice.",
    ] = None,
    clips: Annotated[
        list[dict[str, Any]] | None,
        "Clip definitions: [{name, start_frame (default 0), end_frame (default the last frame), "
        "fps (default 12), loop (default from the name)}]. "
        "For setup_controller: [{name, path}] where path is the .anim asset path.",
    ] = None,
    animation_name: Annotated[
        str | None,
        "full_setup without clips: name of the one clip made from every frame at 12 fps "
        "(default: the sheet's file name). It loops only for an idle, walk or run-type name.",
    ] = None,
    output_dir: Annotated[
        str | None,
        "setup_clips and full_setup: folder for the .anim assets, and for full_setup's default "
        "controller (default: the sprite's folder).",
    ] = None,
    controller_path: Annotated[
        str | None,
        "Path for the .controller asset (e.g. 'Assets/Animators/Hero.controller'); '.controller' is "
        "appended if missing. Required for setup_controller; full_setup defaults to "
        "'<output_dir>/<sheet_name>_Controller.controller'.",
    ] = None,
    overwrite: Annotated[
        bool,
        "Replace an existing .anim or .controller at the target path. Off by default: an existing "
        "clip is skipped with a CLIP_EXISTS warning, and an existing controller fails the call with "
        "CONTROLLER_EXISTS. Slicing is not covered: it always replaces the sheet's slices.",
    ] = False,
    add_to_scene: Annotated[
        bool,
        "full_setup: give scene_target an Animator with the new controller, replacing any controller "
        "it had, and a SpriteRenderer if it has none (warning SCENE_SPRITE_RENDERER_ADDED).",
    ] = False,
    scene_target: Annotated[
        str | None,
        "full_setup with add_to_scene: name of exactly one existing GameObject (inactive ones count). "
        "A missing, unmatched or ambiguous target fails at step 'add_to_scene', after the clips and "
        "controller are written.",
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
        "the previous response; next_cursor is null on the last page. The image "
        "is returned only on the first page.",
    ] = None,
) -> dict[str, Any] | ToolResult:
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
                "Use get_info first to view the sheet image, count the grid visually, then call full_setup with cols/rows."}

    if action_lower == "setup_controller" and not controller_path:
        return {"success": False, "message": "'controller_path' is required for setup_controller (e.g. 'Assets/Animators/Hero.controller')."}

    unity_instance = await get_unity_instance_from_context(ctx)

    # `or None` on the two flags, so a False is dropped rather than sent: the C# side
    # reads a missing key as the default, and forwarding every argument buries the real
    # ones in nulls on the wire.
    optional = {
        "path": path, "cols": cols, "rows": rows,
        "frame_width": frame_width, "frame_height": frame_height,
        "base_name": base_name, "filter_mode": filter_mode, "clips": clips,
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
    if action_lower == "get_info" and isinstance(result, dict) and result.get("success") is True:
        image_base64 = result.get("image_base64")
        if isinstance(image_base64, str) and image_base64:
            return _sprite_image_result(result, image_base64)
    return result if isinstance(result, dict) else {"success": False, "message": str(result)}
