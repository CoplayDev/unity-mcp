"""Blender Bridge CLI commands (drive a running Blender from the Unity Editor).

Thin pass-through to Unity over HTTP: Unity opens the BlenderMCP addon socket itself and
runs the export/import/placement. These commands carry NO API keys and NO file bytes.
"""

from typing import Any, Optional

import click

from cli.utils.config import get_config
from cli.utils.connection import handle_unity_errors, run_command
from cli.utils.output import format_output


@click.group(name="blender")
def blender():
    """Blender Bridge - talk to a running Blender (BlenderMCP addon) from Unity."""
    pass


def _run(action: str, params: Optional[dict[str, Any]] = None) -> None:
    """Send one blender_bridge action to Unity (None-valued params dropped) and print the result."""
    config = get_config()
    payload: dict[str, Any] = {"action": action}
    payload.update({k: v for k, v in (params or {}).items() if v is not None})
    result = run_command("blender_bridge", payload, config)
    click.echo(format_output(result, config.format))


@blender.command("status")
@handle_unity_errors
def status():
    """Report whether Blender is reachable and whether the installed addon matches the checkout."""
    _run("status")


@blender.command("scene-info")
@handle_unity_errors
def scene_info():
    """Print Blender's current scene summary."""
    _run("scene_info")


@blender.command("object-info")
@click.option("--object-name", "object_name", required=True, help="Blender object name.")
@handle_unity_errors
def object_info(object_name: str):
    """Print details about one Blender object."""
    _run("object_info", {"objectName": object_name})


@blender.command("screenshot")
@click.option("--max-size", "max_size", default=None, type=int, help="Max pixels on the longest side.")
@click.option("--output-folder", "output_folder", default=None, help="Copy the PNG under this Assets/ folder.")
@handle_unity_errors
def screenshot(max_size: Optional[int], output_folder: Optional[str]):
    """Capture Blender's viewport to a PNG."""
    _run("screenshot", {"maxSize": max_size, "outputFolder": output_folder})


@blender.command("run-python")
@click.option("--code", default=None, help="Python source to run inside Blender.")
@click.option("--file", "file_path", default=None, type=click.Path(exists=True, dir_okay=False),
              help="Read the Python source from this file instead of --code.")
@handle_unity_errors
def run_python(code: Optional[str], file_path: Optional[str]):
    """Execute Python inside Blender and print its stdout."""
    if not code and not file_path:
        raise click.UsageError("Pass --code or --file.")
    if file_path:
        with open(file_path, encoding="utf-8") as fh:
            code = fh.read()
    _run("run_python", {"code": code})


@blender.command("import-model")
@click.option("--object", "object_names", multiple=True, help="Blender object to export (repeatable). Children included.")
@click.option("--selection-only", "selection_only", is_flag=True, default=False,
              help="Export only what is selected in Blender.")
@click.option("--format", "fmt", default=None, type=click.Choice(["glb", "fbx"]), help="Export format (default glb).")
@click.option("--name", default=None, help="Asset and GameObject name.")
@click.option("--target-size", "target_size", default=None, type=float,
              help="Final size in meters of the largest dimension (0 keeps the imported scale).")
@click.option("--position", default=None, nargs=3, type=float, metavar="X Y Z", help="World position of the placed instance.")
@click.option("--no-place", "no_place", is_flag=True, default=False, help="Import only; do not place in the scene.")
@click.option("--keep-modifiers", "keep_modifiers", is_flag=True, default=False,
              help="Do not bake modifiers (use for rigs / shape keys).")
@click.option("--output-folder", "output_folder", default=None, help="Destination folder under Assets/.")
@click.option("--animation-type", "animation_type", default=None,
              type=click.Choice(["none", "generic", "humanoid", "legacy"]), help="FBX only: rig/animation import mode.")
@click.option("--no-animate", "no_animate", is_flag=True, default=False,
              help="Do not create a looping AnimatorController for imported clips.")
@click.option("--save-prefab", "save_prefab", is_flag=True, default=False, help="Save the placed instance as a prefab.")
@click.option("--ensure-bloom", "ensure_bloom", is_flag=True, default=False,
              help="Add a Bloom volume when the model has emissive materials.")
@click.option("--timeout", "timeout_seconds", default=None, type=int, help="Seconds to wait for Blender.")
@handle_unity_errors
def import_model(
    object_names: tuple[str, ...],
    selection_only: bool,
    fmt: Optional[str],
    name: Optional[str],
    target_size: Optional[float],
    position: Optional[tuple[float, float, float]],
    no_place: bool,
    keep_modifiers: bool,
    output_folder: Optional[str],
    animation_type: Optional[str],
    no_animate: bool,
    save_prefab: bool,
    ensure_bloom: bool,
    timeout_seconds: Optional[int],
):
    """Export from Blender, import into the project, and place the model in the open scene.

    \b
    Examples:
        unity-mcp blender import-model --selection-only --target-size 2
        unity-mcp blender import-model --object House --format glb --position 0 0 0
    """
    _run("import_model", {
        "objectNames": list(object_names) or None,
        "selectionOnly": True if selection_only else None,
        "format": fmt,
        "name": name,
        "targetSize": target_size,
        "position": list(position) if position else None,
        "placeInScene": False if no_place else None,
        "applyModifiers": False if keep_modifiers else None,
        "outputFolder": output_folder,
        "animationType": animation_type,
        "autoAnimate": False if no_animate else None,
        "savePrefab": True if save_prefab else None,
        "ensureBloom": True if ensure_bloom else None,
        "timeoutSeconds": timeout_seconds,
    })


@blender.command("compare-screenshot")
@click.option("--game-object", "game_object", required=True, help="Placed GameObject to frame in Unity.")
@click.option("--max-size", "max_size", default=None, type=int, help="Max pixels on the longest side of each half.")
@click.option("--output-folder", "output_folder", default=None, help="Copy the PNG under this Assets/ folder.")
@handle_unity_errors
def compare_screenshot(game_object: str, max_size: Optional[int], output_folder: Optional[str]):
    """Composite Blender's viewport and a Unity capture of the placed object side by side."""
    _run("compare_screenshot", {"gameObject": game_object, "maxSize": max_size, "outputFolder": output_folder})


@blender.command("setup-bloom")
@handle_unity_errors
def setup_bloom():
    """Enable camera post-processing and add a Bloom override to the global volume."""
    _run("setup_bloom")


@blender.command("check-updates")
@handle_unity_errors
def check_updates():
    """git fetch the blender-mcp checkout and report how far behind its remotes it is."""
    _run("check_updates")


@blender.command("sync-addon")
@click.option("--force", is_flag=True, default=False, help="Overwrite even when the installed addon already matches.")
@handle_unity_errors
def sync_addon(force: bool):
    """Copy the checkout's addon.py into Blender's addons folder."""
    _run("sync_addon", {"force": True if force else None})
