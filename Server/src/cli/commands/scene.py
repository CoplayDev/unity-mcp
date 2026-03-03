"""Scene CLI commands."""

import base64
import os
import sys
from datetime import datetime

import click
from typing import Optional, Any

from cli.utils.config import get_config
from cli.utils.output import format_output, print_error, print_success, print_info
from cli.utils.connection import run_command, handle_unity_errors


@click.group()
def scene():
    """Scene operations - hierarchy, load, save, create scenes."""
    pass


@scene.command("hierarchy")
@click.option(
    "--parent",
    default=None,
    help="Parent GameObject to list children of (name, path, or instance ID)."
)
@click.option(
    "--max-depth", "-d",
    default=None,
    type=int,
    help="Maximum depth to traverse."
)
@click.option(
    "--include-transform", "-t",
    is_flag=True,
    help="Include transform data for each node."
)
@click.option(
    "--limit", "-l",
    default=50,
    type=int,
    help="Maximum nodes to return."
)
@click.option(
    "--cursor", "-c",
    default=0,
    type=int,
    help="Pagination cursor."
)
@handle_unity_errors
def hierarchy(
    parent: Optional[str],
    max_depth: Optional[int],
    include_transform: bool,
    limit: int,
    cursor: int,
):
    """Get the scene hierarchy.

    \b
    Examples:
        unity-mcp scene hierarchy
        unity-mcp scene hierarchy --max-depth 3
        unity-mcp scene hierarchy --parent "Canvas" --include-transform
        unity-mcp scene hierarchy --format json
    """
    config = get_config()

    params: dict[str, Any] = {
        "action": "get_hierarchy",
        "pageSize": limit,
        "cursor": cursor,
    }

    if parent:
        params["parent"] = parent
    if max_depth is not None:
        params["maxDepth"] = max_depth
    if include_transform:
        params["includeTransform"] = True

    result = run_command("manage_scene", params, config)
    click.echo(format_output(result, config.format))


@scene.command("active")
@handle_unity_errors
def active():
    """Get information about the active scene."""
    config = get_config()
    result = run_command("manage_scene", {"action": "get_active"}, config)
    click.echo(format_output(result, config.format))


@scene.command("load")
@click.argument("scene")
@click.option(
    "--by-index", "-i",
    is_flag=True,
    help="Load by build index instead of path/name."
)
@handle_unity_errors
def load(scene: str, by_index: bool):
    """Load a scene.

    \b
    Examples:
        unity-mcp scene load "Assets/Scenes/Main.unity"
        unity-mcp scene load "MainScene"
        unity-mcp scene load 0 --by-index
    """
    config = get_config()

    params: dict[str, Any] = {"action": "load"}

    if by_index:
        try:
            params["buildIndex"] = int(scene)
        except ValueError:
            print_error(f"Invalid build index: {scene}")
            sys.exit(1)
    else:
        if scene.endswith(".unity"):
            params["path"] = scene
        else:
            params["name"] = scene

    result = run_command("manage_scene", params, config)
    click.echo(format_output(result, config.format))
    if result.get("success"):
        print_success(f"Loaded scene: {scene}")


@scene.command("save")
@click.option(
    "--path",
    default=None,
    help="Path to save the scene to (for new scenes)."
)
@handle_unity_errors
def save(path: Optional[str]):
    """Save the current scene.

    \b
    Examples:
        unity-mcp scene save
        unity-mcp scene save --path "Assets/Scenes/NewScene.unity"
    """
    config = get_config()

    params: dict[str, Any] = {"action": "save"}
    if path:
        params["path"] = path

    result = run_command("manage_scene", params, config)
    click.echo(format_output(result, config.format))
    if result.get("success"):
        print_success("Scene saved")


@scene.command("create")
@click.argument("name")
@click.option(
    "--path",
    default=None,
    help="Path to create the scene at."
)
@handle_unity_errors
def create(name: str, path: Optional[str]):
    """Create a new scene.

    \b
    Examples:
        unity-mcp scene create "NewLevel"
        unity-mcp scene create "TestScene" --path "Assets/Scenes/Test"
    """
    config = get_config()

    params: dict[str, Any] = {
        "action": "create",
        "name": name,
    }
    if path:
        params["path"] = path

    result = run_command("manage_scene", params, config)
    click.echo(format_output(result, config.format))
    if result.get("success"):
        print_success(f"Created scene: {name}")


@scene.command("build-settings")
@handle_unity_errors
def build_settings():
    """Get scenes in build settings."""
    config = get_config()
    result = run_command("manage_scene", {"action": "get_build_settings"}, config)
    click.echo(format_output(result, config.format))


@scene.command("screenshot")
@click.option(
    "--filename", "-f",
    default=None,
    help="Output filename (default: timestamp)."
)
@click.option(
    "--supersize", "-s",
    default=1,
    type=int,
    help="Supersize multiplier (1-4)."
)
@click.option(
    "--camera", "-c",
    default=None,
    help="Camera to capture from (name, path, or instance ID). Defaults to Camera.main."
)
@click.option(
    "--include-image", is_flag=True,
    help="Return screenshot as inline base64 PNG in the response."
)
@click.option(
    "--max-resolution", "-r",
    default=None,
    type=int,
    help="Max resolution (longest edge) for inline image. Default 640."
)
@click.option(
    "--batch", "-b",
    default=None,
    help="Batch capture mode: 'surround' for 6 angles, 'orbit' for configurable grid."
)
@click.option(
    "--look-at",
    default=None,
    help="Target to aim at (GO name/path/ID or 'x,y,z' position)."
)
@click.option(
    "--view-position",
    default=None,
    help="Camera position as 'x,y,z'."
)
@click.option(
    "--view-rotation",
    default=None,
    help="Camera euler rotation as 'x,y,z'."
)
@click.option(
    "--orbit-angles",
    default=None,
    type=int,
    help="Number of azimuth samples for batch='orbit' (default 8)."
)
@click.option(
    "--orbit-elevations",
    default=None,
    help="Elevation angles in degrees as JSON array, e.g. '[0,30,-15]'."
)
@click.option(
    "--orbit-distance",
    default=None,
    type=float,
    help="Camera distance from target for batch='orbit'."
)
@click.option(
    "--orbit-fov",
    default=None,
    type=float,
    help="Camera FOV in degrees for batch='orbit' (default 60)."
)
@click.option(
    "--output-dir", "-o",
    default=None,
    help="Directory to save batch screenshots to (default: Unity project's Assets/Screenshots)."
)
@handle_unity_errors
def screenshot(filename: Optional[str], supersize: int, camera: Optional[str],
               include_image: bool, max_resolution: Optional[int],
               batch: Optional[str], look_at: Optional[str],
               view_position: Optional[str], view_rotation: Optional[str],
               orbit_angles: Optional[int], orbit_elevations: Optional[str],
               orbit_distance: Optional[float], orbit_fov: Optional[float],
               output_dir: Optional[str]):
    """Capture a screenshot of the scene.

    \b
    Examples:
        unity-mcp scene screenshot
        unity-mcp scene screenshot --filename "level_preview"
        unity-mcp scene screenshot --supersize 2
        unity-mcp scene screenshot --camera "SecondCamera" --include-image
        unity-mcp scene screenshot --include-image --max-resolution 512
        unity-mcp scene screenshot --batch surround --max-resolution 256
        unity-mcp scene screenshot --look-at "Player" --max-resolution 512
        unity-mcp scene screenshot --view-position "0,10,-10" --look-at "0,0,0"
    """
    config = get_config()

    params: dict[str, Any] = {"action": "screenshot"}
    if filename:
        params["fileName"] = filename
    if supersize > 1:
        params["superSize"] = supersize
    if camera:
        params["camera"] = camera
    if include_image:
        params["includeImage"] = True
    if max_resolution:
        params["maxResolution"] = max_resolution
    if batch:
        params["batch"] = batch
    if look_at:
        # Try parsing as x,y,z coordinates
        parts = look_at.split(",")
        if len(parts) == 3:
            try:
                params["lookAt"] = [float(p.strip()) for p in parts]
            except ValueError:
                params["lookAt"] = look_at
        else:
            params["lookAt"] = look_at
    if view_position:
        parts = view_position.split(",")
        if len(parts) == 3:
            params["viewPosition"] = [float(p.strip()) for p in parts]
    if view_rotation:
        parts = view_rotation.split(",")
        if len(parts) == 3:
            params["viewRotation"] = [float(p.strip()) for p in parts]
    if orbit_angles:
        params["orbitAngles"] = orbit_angles
    if orbit_elevations:
        import json
        try:
            params["orbitElevations"] = json.loads(orbit_elevations)
        except (json.JSONDecodeError, ValueError):
            print_error(f"Invalid orbit-elevations JSON: {orbit_elevations}")
            sys.exit(1)
    if orbit_distance:
        params["orbitDistance"] = orbit_distance
    if orbit_fov:
        params["orbitFov"] = orbit_fov

    result = run_command("manage_scene", params, config)

    # Unwrap the response: {"status":"success","result":{"success":true,"data":{...}}}
    inner = result.get("result", result)  # fallback to result if no nesting
    is_success = (result.get("status") == "success"
                  or inner.get("success", False)
                  or result.get("success", False))
    data = inner.get("data", inner) if isinstance(inner, dict) else {}

    if batch and is_success:
        composite_b64 = data.get("imageBase64")
        shots = data.get("shots", [])

        if composite_b64:
            # Determine output directory
            if not output_dir:
                output_dir = data.get("screenshotsFolder")
            if not output_dir:
                output_dir = os.getcwd()
            output_dir = os.path.abspath(output_dir)
            os.makedirs(output_dir, exist_ok=True)

            mode_label = batch  # "orbit" or "surround"
            timestamp = datetime.now().strftime("%Y%m%d_%H%M%S")
            fname = filename or f"{mode_label}_contact_{timestamp}.png"
            if not fname.lower().endswith(".png"):
                fname += ".png"
            file_path = os.path.join(output_dir, fname)
            try:
                with open(file_path, "wb") as f:
                    f.write(base64.b64decode(composite_b64))
                w = data.get("imageWidth", "?")
                h = data.get("imageHeight", "?")
                print_success(f"Contact sheet ({w}x{h}, {len(shots)} shots) saved to {file_path}")
            except Exception as e:
                print_error(f"Failed to save {file_path}: {e}")
        else:
            print_success(f"Batch completed ({len(shots)} shots, no composite image returned)")

        # Print metadata
        meta = {k: v for k, v in data.items()
                if k not in ("imageBase64", "screenshotsFolder", "shots")}
        if meta:
            print_info(f"  center={meta.get('sceneCenter')}, radius={meta.get('orbitRadius', meta.get('sceneRadius'))}, "
                       f"fov={meta.get('orbitFov')}, size={meta.get('imageWidth')}x{meta.get('imageHeight')}")
    else:
        # Handle positioned single-shot (returns base64 inline, no disk save from Unity)
        if is_success:
            b64 = data.get("imageBase64")
            if b64 and not data.get("path"):
                # Positioned screenshot — save to disk from base64
                if not output_dir:
                    output_dir = data.get("screenshotsFolder")
                if not output_dir:
                    output_dir = os.getcwd()
                output_dir = os.path.abspath(output_dir)
                os.makedirs(output_dir, exist_ok=True)

                fname = filename or f"screenshot_{datetime.now().strftime('%Y%m%d_%H%M%S')}"
                if not fname.lower().endswith(".png"):
                    fname += ".png"
                file_path = os.path.join(output_dir, fname)
                try:
                    with open(file_path, "wb") as f:
                        f.write(base64.b64decode(b64))
                    print_success(f"Screenshot saved to {file_path}")
                except Exception as e:
                    print_error(f"Failed to save {file_path}: {e}")
            else:
                # Standard screenshot (already saved by Unity to Assets/Screenshots/)
                click.echo(format_output(result, config.format))
                print_success("Screenshot captured")
        else:
            click.echo(format_output(result, config.format))
