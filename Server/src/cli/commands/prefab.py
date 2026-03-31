"""Prefab CLI commands."""

import json
import sys
import click
from typing import Optional, Any

from cli.utils.config import get_config
from cli.utils.output import format_output, print_error, print_success
from cli.utils.connection import run_command, handle_unity_errors


@click.group()
def prefab():
    """Prefab operations - info, hierarchy, open, save, close, create prefabs."""
    pass


@prefab.command("open")
@click.argument("path")
@handle_unity_errors
def open_stage(path: str):
    """Open a prefab in the prefab stage for editing.

    \b
    Examples:
        unity-mcp prefab open "Assets/Prefabs/Player.prefab"
    """
    config = get_config()

    params: dict[str, Any] = {
        "action": "open_prefab_stage",
        "prefabPath": path,
    }

    result = run_command("manage_prefabs", params, config)
    click.echo(format_output(result, config.format))
    if result.get("success"):
        print_success(f"Opened prefab: {path}")


@prefab.command("close")
@click.option(
    "--save", "-s",
    is_flag=True,
    help="Save the prefab before closing."
)
@handle_unity_errors
def close_stage(save: bool):
    """Close the current prefab stage.

    \b
    Examples:
        unity-mcp prefab close
        unity-mcp prefab close --save
    """
    config = get_config()

    params: dict[str, Any] = {
        "action": "close_prefab_stage",
    }
    if save:
        params["saveBeforeClose"] = True

    result = run_command("manage_prefabs", params, config)
    click.echo(format_output(result, config.format))
    if result.get("success"):
        print_success("Closed prefab stage")


@prefab.command("save")
@handle_unity_errors
def save_stage():
    """Save the currently open prefab stage.

    \b
    Examples:
        unity-mcp prefab save
    """
    config = get_config()

    params: dict[str, Any] = {
        "action": "save_prefab_stage",
    }

    result = run_command("manage_prefabs", params, config)
    click.echo(format_output(result, config.format))
    if result.get("success"):
        print_success("Saved prefab stage")


@prefab.command("info")
@click.argument("path")
@click.option(
    "--compact", "-c",
    is_flag=True,
    help="Show compact output (key values only)."
)
@handle_unity_errors
def info(path: str, compact: bool):
    """Get information about a prefab asset.

    \b
    Examples:
        unity-mcp prefab info "Assets/Prefabs/Player.prefab"
        unity-mcp prefab info "Assets/Prefabs/UI.prefab" --compact
    """
    config = get_config()

    params: dict[str, Any] = {
        "action": "get_info",
        "prefabPath": path,
    }

    result = run_command("manage_prefabs", params, config)
    # Get the actual response data from the wrapped result structure
    response_data = result.get("result", result)
    if compact and response_data.get("success") and response_data.get("data"):
        data = response_data["data"]
        click.echo(f"Prefab: {data.get('assetPath', path)}")
        click.echo(f"  Type: {data.get('prefabType', 'Unknown')}")
        click.echo(f"  Root: {data.get('rootObjectName', 'N/A')}")
        click.echo(f"  GUID: {data.get('guid', 'N/A')}")
        click.echo(
            f"  Components: {len(data.get('rootComponentTypes', []))}")
        click.echo(f"  Children: {data.get('childCount', 0)}")
        if data.get('isVariant'):
            click.echo(f"  Variant of: {data.get('parentPrefab', 'N/A')}")
    else:
        click.echo(format_output(result, config.format))


@prefab.command("hierarchy")
@click.argument("path")
@click.option(
    "--compact", "-c",
    is_flag=True,
    help="Show compact output (names and paths only)."
)
@click.option(
    "--show-prefab-info", "-p",
    is_flag=True,
    help="Show prefab nesting information."
)
@handle_unity_errors
def hierarchy(path: str, compact: bool, show_prefab_info: bool):
    """Get the hierarchical structure of a prefab.

    \b
    Examples:
        unity-mcp prefab hierarchy "Assets/Prefabs/Player.prefab"
        unity-mcp prefab hierarchy "Assets/Prefabs/UI.prefab" --compact
        unity-mcp prefab hierarchy "Assets/Prefabs/Complex.prefab" --show-prefab-info
    """
    config = get_config()

    params: dict[str, Any] = {
        "action": "get_hierarchy",
        "prefabPath": path,
    }

    result = run_command("manage_prefabs", params, config)
    # Get the actual response data from the wrapped result structure
    response_data = result.get("result", result)
    if compact and response_data.get("success") and response_data.get("data"):
        data = response_data["data"]
        items = data.get("items", [])
        for item in items:
            indent = "  " * item.get("path", "").count("/")
            prefab_info = ""
            if show_prefab_info and item.get("prefab", {}).get("isNestedRoot"):
                prefab_info = f" [nested: {item['prefab']['assetPath']}]"
            click.echo(f"{indent}{item.get('name')}{prefab_info}")
        click.echo(f"\nTotal: {data.get('total', 0)} objects")
    elif show_prefab_info:
        # Show prefab info in readable format
        if response_data.get("success") and response_data.get("data"):
            data = response_data["data"]
            items = data.get("items", [])
            for item in items:
                prefab = item.get("prefab", {})
                prefab_info = ""
                if prefab.get("isRoot"):
                    prefab_info = " [root]"
                elif prefab.get("isNestedRoot"):
                    prefab_info = f" [nested: {prefab.get('nestingDepth', 0)}]"
                click.echo(f"{item.get('path')}{prefab_info}")
            click.echo(f"\nTotal: {data.get('total', 0)} objects")
        else:
            click.echo(format_output(result, config.format))
    else:
        click.echo(format_output(result, config.format))


@prefab.command("create")
@click.argument("target")
@click.argument("path")
@click.option(
    "--overwrite",
    is_flag=True,
    help="Overwrite existing prefab at path."
)
@click.option(
    "--include-inactive",
    is_flag=True,
    help="Include inactive objects when finding target."
)
@click.option(
    "--unlink-if-instance",
    is_flag=True,
    help="Unlink from existing prefab before creating new one."
)
@handle_unity_errors
def create(target: str, path: str, overwrite: bool, include_inactive: bool, unlink_if_instance: bool):
    """Create a prefab from a scene GameObject.

    \b
    Examples:
        unity-mcp prefab create "Player" "Assets/Prefabs/Player.prefab"
        unity-mcp prefab create "Enemy" "Assets/Prefabs/Enemy.prefab" --overwrite
        unity-mcp prefab create "EnemyInstance" "Assets/Prefabs/BossEnemy.prefab" --unlink-if-instance
    """
    config = get_config()

    params: dict[str, Any] = {
        "action": "create_from_gameobject",
        "target": target,
        "prefabPath": path,
    }

    if overwrite:
        params["allowOverwrite"] = True
    if include_inactive:
        params["searchInactive"] = True
    if unlink_if_instance:
        params["unlinkIfInstance"] = True

    result = run_command("manage_prefabs", params, config)
    click.echo(format_output(result, config.format))
    if result.get("success"):
        print_success(f"Created prefab: {path}")


def _parse_vector3(value: str) -> list[float]:
    """Parse 'x,y,z' string to list of floats."""
    parts = value.split(",")
    if len(parts) != 3:
        raise click.BadParameter("Must be 'x,y,z' format")
    try:
        return [float(p.strip()) for p in parts]
    except ValueError as e:
        raise click.BadParameter(f"All components must be numeric, got: '{value}'") from e


def _parse_property(prop_str: str) -> tuple[str, str, Any]:
    """Parse 'Component.prop=value' into (component, prop, value)."""
    if "=" not in prop_str:
        raise click.BadParameter("Must be 'Component.prop=value' format")
    comp_prop, val_str = prop_str.split("=", 1)
    if "." not in comp_prop:
        raise click.BadParameter("Must be 'Component.prop=value' format")
    component, prop = comp_prop.rsplit(".", 1)
    if not component.strip() or not prop.strip():
        raise click.BadParameter(f"Component and property must be non-empty in '{comp_prop}', expected 'Component.prop=value'")

    val_str = val_str.strip()
    
    # Parse booleans
    if val_str.lower() == "true":
        parsed_value: Any = True
    elif val_str.lower() == "false":
        parsed_value = False
    # Parse numbers
    elif "." in val_str:
        try:
            parsed_value = float(val_str)
        except ValueError:
            parsed_value = val_str
    else:
        try:
            parsed_value = int(val_str)
        except ValueError:
            parsed_value = val_str
    
    return component.strip(), prop.strip(), parsed_value


@prefab.command("modify")
@click.argument("path")
@click.option("--target", "-t", help="Target object name/path within prefab (default: root)")
@click.option("--position", "-p", help="New local position as 'x,y,z'")
@click.option("--rotation", "-r", help="New local rotation as 'x,y,z'")
@click.option("--scale", "-s", help="New local scale as 'x,y,z'")
@click.option("--name", "-n", help="New name for target")
@click.option("--tag", help="New tag")
@click.option("--layer", help="New layer")
@click.option("--active/--inactive", default=None, help="Set active state")
@click.option("--parent", help="New parent object name/path")
@click.option("--add-component", multiple=True, help="Component type to add (repeatable)")
@click.option("--remove-component", multiple=True, help="Component type to remove (repeatable)")
@click.option("--set-property", multiple=True, help="Property as 'Component.prop=value' (repeatable)")
@click.option("--delete-child", multiple=True, help="Child name/path to remove (repeatable)")
@click.option("--create-child", help="JSON object for child creation")
@handle_unity_errors
def modify(path: str, target: Optional[str], position: Optional[str], rotation: Optional[str],
           scale: Optional[str], name: Optional[str], tag: Optional[str], layer: Optional[str],
           active: Optional[bool], parent: Optional[str], add_component: tuple, remove_component: tuple,
           set_property: tuple, delete_child: tuple, create_child: Optional[str]):
    """Modify a prefab's contents (headless, no UI).
    
    \b
    Examples:
        unity-mcp prefab modify "Assets/Prefabs/Player.prefab" --delete-child Child1
        unity-mcp prefab modify "Assets/Prefabs/Player.prefab" --delete-child "Turret/Barrel" --delete-child Bullet
        unity-mcp prefab modify "Assets/Prefabs/Player.prefab" --target Weapon --position "0,1,2"
        unity-mcp prefab modify "Assets/Prefabs/Player.prefab" --set-property "Rigidbody.mass=5"
        unity-mcp prefab modify "Assets/Prefabs/Player.prefab" --create-child '{"name":"Spawn","primitive_type":"Sphere"}'
    """
    config = get_config()

    params: dict[str, Any] = {
        "action": "modify_contents",
        "prefabPath": path,
    }

    if target:
        params["target"] = target
    if position:
        params["position"] = _parse_vector3(position)
    if rotation:
        params["rotation"] = _parse_vector3(rotation)
    if scale:
        params["scale"] = _parse_vector3(scale)
    if name:
        params["name"] = name
    if tag:
        params["tag"] = tag
    if layer:
        params["layer"] = layer
    if active is not None:
        params["setActive"] = active
    if parent:
        params["parent"] = parent
    if add_component:
        params["componentsToAdd"] = list(add_component)
    if remove_component:
        params["componentsToRemove"] = list(remove_component)
    if set_property:
        component_properties: dict[str, dict[str, Any]] = {}
        for prop in set_property:
            comp, name_p, val = _parse_property(prop)
            if comp not in component_properties:
                component_properties[comp] = {}
            component_properties[comp][name_p] = val
        params["componentProperties"] = component_properties
    if delete_child:
        params["deleteChild"] = list(delete_child)
    if create_child:
        try:
            parsed = json.loads(create_child)
        except json.JSONDecodeError as e:
            raise click.BadParameter(f"Invalid JSON for --create-child: {e}") from e
        if not isinstance(parsed, dict):
            raise click.BadParameter(f"--create-child must be a JSON object, got {type(parsed).__name__}")
        params["createChild"] = parsed

    result = run_command("manage_prefabs", params, config)
    click.echo(format_output(result, config.format))
    if result.get("success"):
        print_success(f"Modified prefab: {path}")
