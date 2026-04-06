"""Scene CLI commands."""

import sys

import click
from typing import Optional, Any

from cli.utils.config import get_config
from cli.utils.output import format_output, print_error, print_success, print_warning
from cli.utils.connection import run_command, handle_unity_errors


@click.group()
def scene():
    """Scene operations - hierarchy, load, save, create, multi-scene, validation."""
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
@click.option(
    "--template", "-t",
    default=None,
    type=click.Choice(["empty", "default", "3d_basic", "2d_basic"]),
    help="Scene template (omit for empty scene)."
)
@handle_unity_errors
def create(name: str, path: Optional[str], template: Optional[str]):
    """Create a new scene, optionally from a template.

    \b
    Templates:
        empty     - Empty scene, no default objects
        default   - Camera + Directional Light (Unity default)
        3d_basic  - Default + ground plane
        2d_basic  - Default + orthographic camera

    \b
    Examples:
        unity-mcp scene create "NewLevel"
        unity-mcp scene create "Level1" --template 3d_basic
        unity-mcp scene create "Level1" --template 2d_basic --path "Assets/Scenes"
    """
    config = get_config()

    params: dict[str, Any] = {
        "action": "create",
        "name": name,
    }
    if path:
        params["path"] = path
    if template:
        params["template"] = template

    result = run_command("manage_scene", params, config)
    click.echo(format_output(result, config.format))
    if result.get("success"):
        label = f" from template '{template}'" if template else ""
        print_success(f"Created scene{label}: {name}")


@scene.command("build-settings")
@handle_unity_errors
def build_settings():
    """Get scenes in build settings."""
    config = get_config()
    result = run_command("manage_scene", {"action": "get_build_settings"}, config)
    click.echo(format_output(result, config.format))


# ── Multi-scene editing ──────────────────────────────────────────────


@scene.command("loaded")
@handle_unity_errors
def loaded():
    """List all currently loaded scenes.

    \b
    Examples:
        unity-mcp scene loaded
    """
    config = get_config()
    result = run_command("manage_scene", {"action": "get_loaded_scenes"}, config)
    click.echo(format_output(result, config.format))


@scene.command("open-additive")
@click.argument("scene_path")
@handle_unity_errors
def open_additive(scene_path: str):
    """Open a scene additively (keeps current scene loaded).

    \b
    Examples:
        unity-mcp scene open-additive "Assets/Scenes/Level2.unity"
    """
    config = get_config()
    result = run_command("manage_scene", {
        "action": "load",
        "path": scene_path,
        "additive": True,
    }, config)
    click.echo(format_output(result, config.format))
    if result.get("success"):
        print_success(f"Opened additively: {scene_path}")


@scene.command("close")
@click.argument("scene_name")
@click.option("--remove", is_flag=True, help="Fully remove scene instead of just unloading.")
@handle_unity_errors
def close(scene_name: str, remove: bool):
    """Close/unload a loaded scene.

    \b
    Examples:
        unity-mcp scene close "Level2"
        unity-mcp scene close "Level2" --remove
    """
    config = get_config()
    params: dict[str, Any] = {
        "action": "close_scene",
        "sceneName": scene_name,
    }
    if remove:
        params["removeScene"] = True
    result = run_command("manage_scene", params, config)
    click.echo(format_output(result, config.format))
    if result.get("success"):
        print_success(f"Closed scene: {scene_name}")


@scene.command("set-active")
@click.argument("scene_name")
@handle_unity_errors
def set_active(scene_name: str):
    """Set a loaded scene as the active scene.

    \b
    Examples:
        unity-mcp scene set-active "Level2"
    """
    config = get_config()
    result = run_command("manage_scene", {
        "action": "set_active_scene",
        "sceneName": scene_name,
    }, config)
    click.echo(format_output(result, config.format))
    if result.get("success"):
        print_success(f"Set active: {scene_name}")


@scene.command("move-to")
@click.argument("target")
@click.argument("scene_name")
@handle_unity_errors
def move_to(target: str, scene_name: str):
    """Move a root GameObject to another loaded scene.

    \b
    Examples:
        unity-mcp scene move-to "Player" "Level2"
    """
    config = get_config()
    result = run_command("manage_scene", {
        "action": "move_to_scene",
        "target": target,
        "sceneName": scene_name,
    }, config)
    click.echo(format_output(result, config.format))
    if result.get("success"):
        print_success(f"Moved '{target}' to scene '{scene_name}'")


# ── Scene validation ─────────────────────────────────────────────────


@scene.command("validate")
@click.option("--repair", is_flag=True, help="Auto-fix missing scripts (undoable).")
@handle_unity_errors
def validate(repair: bool):
    """Validate the active scene for issues (missing scripts, broken prefabs).

    \b
    Examples:
        unity-mcp scene validate
        unity-mcp scene validate --repair
    """
    config = get_config()
    params: dict[str, Any] = {"action": "validate"}
    if repair:
        params["autoRepair"] = True
    result = run_command("manage_scene", params, config)
    click.echo(format_output(result, config.format))
    if result.get("success"):
        data = result.get("data", {})
        total = data.get("totalIssues", 0)
        repaired = data.get("repaired", 0)
        if total == 0:
            print_success("Scene is clean")
        elif repaired > 0:
            print_success(f"Found {total} issue(s), repaired {repaired}")
        else:
            print_warning(f"Found {total} issue(s), none repaired")


