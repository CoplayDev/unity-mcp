"""Component CLI commands."""

import sys
import json
import click
from typing import Optional, Any

from cli.utils.config import get_config
from cli.utils.output import format_output, print_error, print_success
from cli.utils.connection import run_command, UnityConnectionError


@click.group()
def component():
    """Component operations - add, remove, modify components on GameObjects."""
    pass


@component.command("add")
@click.argument("target")
@click.argument("component_type")
@click.option(
    "--search-method",
    type=click.Choice(["by_id", "by_name", "by_path"]),
    default=None,
    help="How to find the target GameObject."
)
@click.option(
    "--properties", "-p",
    default=None,
    help='Initial properties as JSON (e.g., \'{"mass": 5.0}\').'
)
def add(target: str, component_type: str, search_method: Optional[str], properties: Optional[str]):
    """Add a component to a GameObject.

    \b
    Examples:
        unity-mcp component add "Player" Rigidbody
        unity-mcp component add "-81840" BoxCollider --search-method by_id
        unity-mcp component add "Enemy" Rigidbody --properties '{"mass": 5.0, "useGravity": true}'
    """
    config = get_config()

    params: dict[str, Any] = {
        "action": "add",
        "target": target,
        "componentType": component_type,
    }

    if search_method:
        params["searchMethod"] = search_method
    if properties:
        try:
            params["properties"] = json.loads(properties)
        except json.JSONDecodeError as e:
            print_error(f"Invalid JSON for properties: {e}")
            sys.exit(1)

    try:
        result = run_command("manage_components", params, config)
        click.echo(format_output(result, config.format))
        if result.get("success"):
            print_success(f"Added {component_type} to '{target}'")
    except UnityConnectionError as e:
        print_error(str(e))
        sys.exit(1)


@component.command("remove")
@click.argument("target")
@click.argument("component_type")
@click.option(
    "--search-method",
    type=click.Choice(["by_id", "by_name", "by_path"]),
    default=None,
    help="How to find the target GameObject."
)
@click.option(
    "--force", "-f",
    is_flag=True,
    help="Skip confirmation prompt."
)
def remove(target: str, component_type: str, search_method: Optional[str], force: bool):
    """Remove a component from a GameObject.

    \b
    Examples:
        unity-mcp component remove "Player" Rigidbody
        unity-mcp component remove "-81840" BoxCollider --search-method by_id --force
    """
    config = get_config()

    if not force:
        click.confirm(f"Remove {component_type} from '{target}'?", abort=True)

    params: dict[str, Any] = {
        "action": "remove",
        "target": target,
        "componentType": component_type,
    }

    if search_method:
        params["searchMethod"] = search_method

    try:
        result = run_command("manage_components", params, config)
        click.echo(format_output(result, config.format))
        if result.get("success"):
            print_success(f"Removed {component_type} from '{target}'")
    except UnityConnectionError as e:
        print_error(str(e))
        sys.exit(1)


@component.command("set")
@click.argument("target")
@click.argument("component_type")
@click.argument("property_name")
@click.argument("value")
@click.option(
    "--search-method",
    type=click.Choice(["by_id", "by_name", "by_path"]),
    default=None,
    help="How to find the target GameObject."
)
def set_property(target: str, component_type: str, property_name: str, value: str, search_method: Optional[str]):
    """Set a single property on a component.

    \b
    Examples:
        unity-mcp component set "Player" Rigidbody mass 5.0
        unity-mcp component set "Enemy" Transform position "[0, 5, 0]"
        unity-mcp component set "-81840" Light intensity 2.5 --search-method by_id
    """
    config = get_config()

    # Try to parse value as JSON for complex types
    try:
        parsed_value = json.loads(value)
    except json.JSONDecodeError:
        # Keep as string if not valid JSON
        parsed_value = value

    params: dict[str, Any] = {
        "action": "set_property",
        "target": target,
        "componentType": component_type,
        "property": property_name,
        "value": parsed_value,
    }

    if search_method:
        params["searchMethod"] = search_method

    try:
        result = run_command("manage_components", params, config)
        click.echo(format_output(result, config.format))
        if result.get("success"):
            print_success(f"Set {component_type}.{property_name} = {value}")
    except UnityConnectionError as e:
        print_error(str(e))
        sys.exit(1)


@component.command("modify")
@click.argument("target")
@click.argument("component_type")
@click.option(
    "--properties", "-p",
    required=True,
    help='Properties to set as JSON (e.g., \'{"mass": 5.0, "useGravity": false}\').'
)
@click.option(
    "--search-method",
    type=click.Choice(["by_id", "by_name", "by_path"]),
    default=None,
    help="How to find the target GameObject."
)
def modify(target: str, component_type: str, properties: str, search_method: Optional[str]):
    """Set multiple properties on a component at once.

    \b
    Examples:
        unity-mcp component modify "Player" Rigidbody --properties '{"mass": 5.0, "useGravity": false}'
        unity-mcp component modify "Light" Light --properties '{"intensity": 2.0, "color": [1, 0, 0, 1]}'
    """
    config = get_config()

    try:
        props_dict = json.loads(properties)
    except json.JSONDecodeError as e:
        print_error(f"Invalid JSON for properties: {e}")
        sys.exit(1)

    params: dict[str, Any] = {
        "action": "set_property",
        "target": target,
        "componentType": component_type,
        "properties": props_dict,
    }

    if search_method:
        params["searchMethod"] = search_method

    try:
        result = run_command("manage_components", params, config)
        click.echo(format_output(result, config.format))
        if result.get("success"):
            print_success(f"Modified {component_type} on '{target}'")
    except UnityConnectionError as e:
        print_error(str(e))
        sys.exit(1)
