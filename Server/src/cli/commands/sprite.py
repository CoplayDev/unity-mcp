"""Sprite CLI commands - slice sprite sheets and build 2D animation from them."""

import click
from typing import Optional, Any

from cli.utils.config import get_config
from cli.utils.output import format_output
from cli.utils.connection import run_command, handle_unity_errors
from cli.utils.parsers import parse_json_list_or_exit


def _params(action: str, **values: Any) -> dict[str, Any]:
    # Unity reads a missing key as the default, so an option that was not given stays off the wire.
    return {"action": action, **{k: v for k, v in values.items() if v is not None}}


def _clips(value: Optional[str]) -> Optional[list[Any]]:
    return parse_json_list_or_exit(value, "clips") if value is not None else None


@click.group()
def sprite():
    """Sprite sheet operations - slice sheets, build clips and Animator controllers."""
    pass


@sprite.command("info")
@click.argument("path")
@click.option("--page-size", type=int, default=None, help="Slices to list (1-4096, default 512).")
@click.option("--cursor", type=int, default=None, help="Slice index to start at; pass back next_cursor.")
@handle_unity_errors
def info(path: str, page_size: Optional[int], cursor: Optional[int]):
    """Show a sheet's import settings, size and slices.

    The tool also sends the sheet as a base64 image for a model to look at.
    The CLI leaves it out: the image is the file at PATH.

    \b
    Examples:
        unity-mcp sprite info Assets/Sprites/hero.png
        unity-mcp sprite info Assets/Sprites/atlas.png --cursor 512
    """
    config = get_config()
    result = run_command("manage_sprite", _params(
        "get_info", path=path, page_size=page_size, cursor=cursor), config)
    # Unity wraps the tool's reply in {"status", "result"}, and /api/command passes that on as is.
    reply = result.get("result", result)
    if reply.get("image_base64"):
        reply["image_base64"] = None
        reply["image_omitted_reason"] = f"The CLI does not print the image; it is the file at {reply.get('path', path)}."
    click.echo(format_output(result, config.format))


@sprite.command("slice")
@click.argument("path")
@click.option("--cols", type=int, default=None, help="Columns in the grid.")
@click.option("--rows", type=int, default=None, help="Rows in the grid (default 1, or from --frame-height).")
@click.option("--frame-width", type=int, default=None, help="Frame width in pixels; alternative to --cols.")
@click.option("--frame-height", type=int, default=None, help="Frame height in pixels; alternative to --rows.")
@click.option("--base-name", default=None, help="Base name for the frames (default: texture file name).")
@click.option("--filter-mode", type=click.Choice(["point", "bilinear", "trilinear"]), default=None,
              help="Texture filter for the sheet (default point, for pixel art).")
@handle_unity_errors
def slice_sheet(path: str, cols: Optional[int], rows: Optional[int], frame_width: Optional[int],
                frame_height: Optional[int], base_name: Optional[str], filter_mode: Optional[str]):
    """Slice a sprite sheet into a grid of frames.

    Replaces the sheet's existing slices and sets it to Sprite (Multiple) import,
    with NPOT scaling off and the --filter-mode filter.

    \b
    Examples:
        unity-mcp sprite slice Assets/Sprites/hero.png --cols 6 --rows 4
        unity-mcp sprite slice Assets/Sprites/hero.png --frame-width 32 --frame-height 32
    """
    config = get_config()
    result = run_command("manage_sprite", _params(
        "slice_sheet", path=path, cols=cols, rows=rows, frame_width=frame_width,
        frame_height=frame_height, base_name=base_name, filter_mode=filter_mode), config)
    click.echo(format_output(result, config.format))


@sprite.command("setup-clips")
@click.argument("path")
@click.option("--clips", "clips", required=True,
              help='JSON list: [{"name","start_frame","end_frame","fps","loop"}].')
@click.option("--output-dir", default=None, help="Folder for the .anim assets (default: the sheet's folder).")
@click.option("--overwrite", is_flag=True, help="Replace .anim assets that already exist.")
@handle_unity_errors
def setup_clips(path: str, clips: str, output_dir: Optional[str], overwrite: bool):
    """Create AnimationClips from a sliced sheet.

    \b
    Examples:
        unity-mcp sprite setup-clips Assets/Sprites/hero.png \\
            --clips '[{"name": "walk", "start_frame": 0, "end_frame": 5}]'
    """
    config = get_config()
    result = run_command("manage_sprite", _params(
        "setup_clips", path=path, clips=_clips(clips), output_dir=output_dir,
        overwrite=overwrite or None), config)
    click.echo(format_output(result, config.format))


@sprite.command("setup-controller")
@click.argument("controller_path")
@click.option("--clips", "clips", required=True,
              help='JSON list: [{"name", "path"}], where path is the .anim asset.')
@click.option("--overwrite", is_flag=True, help="Replace the .controller if it already exists.")
@handle_unity_errors
def setup_controller(controller_path: str, clips: str, overwrite: bool):
    """Build an AnimatorController from existing clips.

    \b
    Examples:
        unity-mcp sprite setup-controller Assets/Animators/Hero.controller \\
            --clips '[{"name": "idle", "path": "Assets/Sprites/idle.anim"}]'
    """
    config = get_config()
    result = run_command("manage_sprite", _params(
        "setup_controller", controller_path=controller_path, clips=_clips(clips),
        overwrite=overwrite or None), config)
    click.echo(format_output(result, config.format))


@sprite.command("full-setup")
@click.argument("path")
@click.option("--cols", type=int, default=None, help="Columns in the grid.")
@click.option("--rows", type=int, default=None, help="Rows in the grid (default 1, or from --frame-height).")
@click.option("--frame-width", type=int, default=None, help="Frame width in pixels; alternative to --cols.")
@click.option("--frame-height", type=int, default=None, help="Frame height in pixels; alternative to --rows.")
@click.option("--base-name", default=None, help="Base name for the frames (default: texture file name).")
@click.option("--filter-mode", type=click.Choice(["point", "bilinear", "trilinear"]), default=None,
              help="Texture filter for the sheet (default point, for pixel art).")
@click.option("--clips", "clips", default=None,
              help='JSON list: [{"name","start_frame","end_frame","fps","loop"}].')
@click.option("--animation-name", default=None,
              help="Name of the one clip made from all frames when --clips is not given (default: the "
                   "sheet's file name; 12 fps; loops only for idle, walk or run-type names).")
@click.option("--output-dir", default=None,
              help="Folder for the .anim assets and, without --controller-path, the controller "
                   "(default: the sheet's folder).")
@click.option("--controller-path", default=None,
              help="Path for the .controller asset (default: <output-dir>/<sheet>_Controller.controller).")
@click.option("--overwrite", is_flag=True,
              help="Replace .anim and .controller assets that already exist (the sheet is re-sliced either way).")
@click.option("--add-to-scene", is_flag=True,
              help="Give --scene-target an Animator with the controller, and a SpriteRenderer if it has none.")
@click.option("--scene-target", default=None,
              help="Name of exactly one existing GameObject (inactive ones count) for --add-to-scene.")
@handle_unity_errors
def full_setup(path: str, cols: Optional[int], rows: Optional[int], frame_width: Optional[int],
               frame_height: Optional[int], base_name: Optional[str], clips: Optional[str],
               animation_name: Optional[str], output_dir: Optional[str], controller_path: Optional[str],
               overwrite: bool, add_to_scene: bool, scene_target: Optional[str], filter_mode: Optional[str]):
    """Slice a sheet, then build its clips and controller in one step.

    \b
    Examples:
        unity-mcp sprite full-setup Assets/Sprites/coin.png --cols 8 \\
            --clips '[{"name": "spin", "start_frame": 0, "end_frame": 7, "loop": true}]'
        unity-mcp sprite full-setup Assets/Sprites/hero.png --cols 6 --rows 4 \\
            --clips '[{"name": "idle", "start_frame": 0, "end_frame": 5}, {"name": "walk", "start_frame": 6, "end_frame": 11}]' \\
            --controller-path Assets/Animators/Hero.controller --add-to-scene --scene-target Hero
    """
    config = get_config()
    result = run_command("manage_sprite", _params(
        "full_setup", path=path, cols=cols, rows=rows, frame_width=frame_width,
        frame_height=frame_height, base_name=base_name, filter_mode=filter_mode, clips=_clips(clips),
        animation_name=animation_name, output_dir=output_dir, controller_path=controller_path,
        overwrite=overwrite or None, add_to_scene=add_to_scene or None,
        scene_target=scene_target), config)
    click.echo(format_output(result, config.format))
