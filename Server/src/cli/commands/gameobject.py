"""GameObject CLI commands."""

import sys
import json
import click
from typing import Optional, Tuple, Any

from cli.utils.config import get_config
from cli.utils.output import format_output, print_error, print_success, print_warning
from cli.utils.connection import run_command, handle_unity_errors, UnityConnectionError
from cli.utils.constants import SEARCH_METHOD_CHOICE_FULL, SEARCH_METHOD_CHOICE_TAGGED
from cli.utils.confirmation import confirm_destructive_action


@click.group()
def gameobject():
    """GameObject operations - create, find, modify, delete GameObjects."""
    pass


@gameobject.command("find")
@click.argument("search_term")
@click.option(
    "--method", "-m",
    type=SEARCH_METHOD_CHOICE_FULL,
    default="by_name",
    help="Search method."
)
@click.option(
    "--include-inactive", "-i",
    is_flag=True,
    help="Include inactive GameObjects."
)
@click.option(
    "--limit", "-l",
    default=50,
    type=int,
    help="Maximum results to return."
)
@click.option(
    "--cursor", "-c",
    default=0,
    type=int,
    help="Pagination cursor (offset)."
)
@handle_unity_errors
def find(search_term: str, method: str, include_inactive: bool, limit: int, cursor: int):
    """Find GameObjects by search criteria.

    \b
    Examples:
        unity-mcp gameobject find "Player"
        unity-mcp gameobject find "Enemy" --method by_tag
        unity-mcp gameobject find "-81840" --method by_id
        unity-mcp gameobject find "Rigidbody" --method by_component
        unity-mcp gameobject find "/Canvas/Panel" --method by_path
    """
    config = get_config()
    result = run_command("find_gameobjects", {
        "searchMethod": method,
        "searchTerm": search_term,
        "includeInactive": include_inactive,
        "pageSize": limit,
        "cursor": cursor,
    }, config)
    click.echo(format_output(result, config.format))


@gameobject.command("create")
@click.argument("name")
@click.option(
    "--primitive", "-p",
    type=click.Choice(["Cube", "Sphere", "Cylinder",
                      "Plane", "Capsule", "Quad"]),
    help="Create a primitive type."
)
@click.option(
    "--position", "-pos",
    nargs=3,
    type=float,
    default=None,
    help="Position as X Y Z."
)
@click.option(
    "--rotation", "-rot",
    nargs=3,
    type=float,
    default=None,
    help="Rotation as X Y Z (euler angles)."
)
@click.option(
    "--scale", "-s",
    nargs=3,
    type=float,
    default=None,
    help="Scale as X Y Z."
)
@click.option(
    "--parent",
    default=None,
    help="Parent GameObject name or path."
)
@click.option(
    "--tag", "-t",
    default=None,
    help="Tag to assign."
)
@click.option(
    "--layer",
    default=None,
    help="Layer to assign."
)
@click.option(
    "--components",
    default=None,
    help="Comma-separated list of components to add."
)
@click.option(
    "--save-prefab",
    is_flag=True,
    help="Save as prefab after creation."
)
@click.option(
    "--prefab-path",
    default=None,
    help="Path for prefab (e.g., Assets/Prefabs/MyPrefab.prefab)."
)
@handle_unity_errors
def create(
    name: str,
    primitive: Optional[str],
    position: Optional[Tuple[float, float, float]],
    rotation: Optional[Tuple[float, float, float]],
    scale: Optional[Tuple[float, float, float]],
    parent: Optional[str],
    tag: Optional[str],
    layer: Optional[str],
    components: Optional[str],
    save_prefab: bool,
    prefab_path: Optional[str],
):
    """Create a new GameObject.

    \b
    Examples:
        unity-mcp gameobject create "MyCube" --primitive Cube
        unity-mcp gameobject create "Player" --position 0 1 0
        unity-mcp gameobject create "Enemy" --primitive Sphere --tag Enemy
        unity-mcp gameobject create "Child" --parent "ParentObject"
        unity-mcp gameobject create "Item" --components "Rigidbody,BoxCollider"
    """
    config = get_config()

    params: dict[str, Any] = {
        "action": "create",
        "name": name,
    }

    if primitive:
        params["primitiveType"] = primitive
    if position:
        params["position"] = list(position)
    if rotation:
        params["rotation"] = list(rotation)
    if scale:
        params["scale"] = list(scale)
    if parent:
        params["parent"] = parent
    if tag:
        params["tag"] = tag
    if layer:
        params["layer"] = layer
    if save_prefab:
        params["saveAsPrefab"] = True
    if prefab_path:
        params["prefabPath"] = prefab_path

    result = run_command("manage_gameobject", params, config)

    # Add components separately since componentsToAdd doesn't work
    if components and (result.get("success") or result.get("data") or result.get("result")):
        component_list = [c.strip() for c in components.split(",")]
        failed_components = []
        for component in component_list:
            try:
                run_command("manage_components", {
                    "action": "add",
                    "target": name,
                    "componentType": component,
                }, config)
            except UnityConnectionError:
                failed_components.append(component)
        if failed_components:
            print_warning(f"Failed to add components: {', '.join(failed_components)}")

    click.echo(format_output(result, config.format))
    if result.get("success") or result.get("result"):
        print_success(f"Created GameObject '{name}'")


@gameobject.command("modify")
@click.argument("target")
@click.option(
    "--name", "-n",
    default=None,
    help="New name for the GameObject."
)
@click.option(
    "--position", "-pos",
    nargs=3,
    type=float,
    default=None,
    help="New position as X Y Z."
)
@click.option(
    "--rotation", "-rot",
    nargs=3,
    type=float,
    default=None,
    help="New rotation as X Y Z (euler angles)."
)
@click.option(
    "--scale", "-s",
    nargs=3,
    type=float,
    default=None,
    help="New scale as X Y Z."
)
@click.option(
    "--parent",
    default=None,
    help="New parent GameObject."
)
@click.option(
    "--tag", "-t",
    default=None,
    help="New tag."
)
@click.option(
    "--layer",
    default=None,
    help="New layer."
)
@click.option(
    "--active/--inactive",
    default=None,
    help="Set active state."
)
@click.option(
    "--add-components",
    default=None,
    help="Comma-separated list of components to add."
)
@click.option(
    "--remove-components",
    default=None,
    help="Comma-separated list of components to remove."
)
@click.option(
    "--search-method",
    type=SEARCH_METHOD_CHOICE_TAGGED,
    default=None,
    help="How to find the target GameObject."
)
@handle_unity_errors
def modify(
    target: str,
    name: Optional[str],
    position: Optional[Tuple[float, float, float]],
    rotation: Optional[Tuple[float, float, float]],
    scale: Optional[Tuple[float, float, float]],
    parent: Optional[str],
    tag: Optional[str],
    layer: Optional[str],
    active: Optional[bool],
    add_components: Optional[str],
    remove_components: Optional[str],
    search_method: Optional[str],
):
    """Modify an existing GameObject.

    TARGET can be a name, path, instance ID, or tag depending on --search-method.

    \b
    Examples:
        unity-mcp gameobject modify "Player" --position 0 5 0
        unity-mcp gameobject modify "Enemy" --name "Boss" --tag "Boss"
        unity-mcp gameobject modify "-81840" --search-method by_id --active
        unity-mcp gameobject modify "/Canvas/Panel" --search-method by_path --inactive
        unity-mcp gameobject modify "Cube" --add-components "Rigidbody,BoxCollider"
    """
    config = get_config()

    params: dict[str, Any] = {
        "action": "modify",
        "target": target,
    }

    if name:
        params["name"] = name
    if position:
        params["position"] = list(position)
    if rotation:
        params["rotation"] = list(rotation)
    if scale:
        params["scale"] = list(scale)
    if parent:
        params["parent"] = parent
    if tag:
        params["tag"] = tag
    if layer:
        params["layer"] = layer
    if active is not None:
        params["setActive"] = active
    if add_components:
        params["componentsToAdd"] = [c.strip() for c in add_components.split(",")]
    if remove_components:
        params["componentsToRemove"] = [c.strip() for c in remove_components.split(",")]
    if search_method:
        params["searchMethod"] = search_method

    result = run_command("manage_gameobject", params, config)
    click.echo(format_output(result, config.format))


@gameobject.command("delete")
@click.argument("target")
@click.option(
    "--search-method",
    type=SEARCH_METHOD_CHOICE_TAGGED,
    default=None,
    help="How to find the target GameObject."
)
@click.option(
    "--force", "-f",
    is_flag=True,
    help="Skip confirmation prompt."
)
@handle_unity_errors
def delete(target: str, search_method: Optional[str], force: bool):
    """Delete a GameObject.

    \b
    Examples:
        unity-mcp gameobject delete "OldObject"
        unity-mcp gameobject delete "-81840" --search-method by_id
        unity-mcp gameobject delete "TempObjects" --search-method by_tag --force
    """
    config = get_config()

    confirm_destructive_action("Delete", "GameObject", target, force)

    params = {
        "action": "delete",
        "target": target,
    }

    if search_method:
        params["searchMethod"] = search_method

    result = run_command("manage_gameobject", params, config)
    click.echo(format_output(result, config.format))
    if result.get("success"):
        print_success(f"Deleted GameObject '{target}'")


@gameobject.command("duplicate")
@click.argument("target")
@click.option(
    "--name", "-n",
    default=None,
    help="Name for the duplicate (default: OriginalName_Copy)."
)
@click.option(
    "--offset",
    nargs=3,
    type=float,
    default=None,
    help="Position offset from original as X Y Z."
)
@click.option(
    "--search-method",
    type=SEARCH_METHOD_CHOICE_TAGGED,
    default=None,
    help="How to find the target GameObject."
)
@handle_unity_errors
def duplicate(
    target: str,
    name: Optional[str],
    offset: Optional[Tuple[float, float, float]],
    search_method: Optional[str],
):
    """Duplicate a GameObject.

    \b
    Examples:
        unity-mcp gameobject duplicate "Player"
        unity-mcp gameobject duplicate "Enemy" --name "Enemy2" --offset 5 0 0
        unity-mcp gameobject duplicate "-81840" --search-method by_id
    """
    config = get_config()

    params: dict[str, Any] = {
        "action": "duplicate",
        "target": target,
    }

    if name:
        params["new_name"] = name
    if offset:
        params["offset"] = list(offset)
    if search_method:
        params["searchMethod"] = search_method

    result = run_command("manage_gameobject", params, config)
    click.echo(format_output(result, config.format))
    if result.get("success"):
        print_success(f"Duplicated GameObject '{target}'")


@gameobject.command("move")
@click.argument("target")
@click.option(
    "--reference", "-r",
    required=True,
    help="Reference object for relative movement."
)
@click.option(
    "--direction", "-d",
    type=click.Choice(["left", "right", "up", "down", "forward",
                      "back", "front", "backward", "behind"]),
    required=True,
    help="Direction to move."
)
@click.option(
    "--distance",
    type=float,
    default=1.0,
    help="Distance to move (default: 1.0)."
)
@click.option(
    "--local",
    is_flag=True,
    help="Use reference object's local space instead of world space."
)
@click.option(
    "--search-method",
    type=SEARCH_METHOD_CHOICE_TAGGED,
    default=None,
    help="How to find the target GameObject."
)
@handle_unity_errors
def move(
    target: str,
    reference: str,
    direction: str,
    distance: float,
    local: bool,
    search_method: Optional[str],
):
    """Move a GameObject relative to another object.

    \b
    Examples:
        unity-mcp gameobject move "Chair" --reference "Table" --direction right --distance 2
        unity-mcp gameobject move "Light" --reference "Player" --direction up --distance 3
        unity-mcp gameobject move "NPC" --reference "Player" --direction forward --local
    """
    config = get_config()

    params: dict[str, Any] = {
        "action": "move_relative",
        "target": target,
        "reference_object": reference,
        "direction": direction,
        "distance": distance,
        "world_space": not local,
    }

    if search_method:
        params["searchMethod"] = search_method

    result = run_command("manage_gameobject", params, config)
    click.echo(format_output(result, config.format))
    if result.get("success"):
        print_success(f"Moved '{target}' {direction} of '{reference}' by {distance} units")
