"""Material CLI commands."""

import sys
import json
import click
from typing import Optional, Any, Tuple

from cli.utils.config import get_config
from cli.utils.output import format_output, print_error, print_success
from cli.utils.connection import run_command, handle_unity_errors
from cli.utils.parsers import parse_value_safe, parse_json_dict_or_exit
from cli.utils.constants import SEARCH_METHOD_CHOICE_RENDERER


@click.group()
def material():
    """Material operations - create, modify, assign materials."""
    pass


@material.command("info")
@click.argument("path")
@handle_unity_errors
def info(path: str):
    """Get information about a material.

    \b
    Examples:
        unity-mcp material info "Assets/Materials/Red.mat"
    """
    config = get_config()

    result = run_command("manage_material", {
        "action": "get_material_info",
        "materialPath": path,
    }, config)
    click.echo(format_output(result, config.format))


@material.command("create")
@click.argument("path")
@click.option(
    "--shader", "-s",
    default="Standard",
    help="Shader to use (default: Standard)."
)
@click.option(
    "--properties", "-p",
    default=None,
    help='Initial properties as JSON.'
)
@handle_unity_errors
def create(path: str, shader: str, properties: Optional[str]):
    """Create a new material.

    \b
    Examples:
        unity-mcp material create "Assets/Materials/NewMat.mat"
        unity-mcp material create "Assets/Materials/Red.mat" --shader "Universal Render Pipeline/Lit"
        unity-mcp material create "Assets/Materials/Blue.mat" --properties '{"_Color": [0,0,1,1]}'
    """
    config = get_config()

    params: dict[str, Any] = {
        "action": "create",
        "materialPath": path,
        "shader": shader,
    }

    if properties:
        params["properties"] = parse_json_dict_or_exit(properties, "properties")

    result = run_command("manage_material", params, config)
    click.echo(format_output(result, config.format))
    if result.get("success"):
        print_success(f"Created material: {path}")


@material.command("set-color")
@click.argument("path")
@click.argument("r", type=float)
@click.argument("g", type=float)
@click.argument("b", type=float)
@click.argument("a", type=float, default=1.0)
@click.option(
    "--property", "-p",
    default="_Color",
    help="Color property name (default: _Color)."
)
@handle_unity_errors
def set_color(path: str, r: float, g: float, b: float, a: float, property: str):
    """Set a material's color.

    \b
    Examples:
        unity-mcp material set-color "Assets/Materials/Red.mat" 1 0 0
        unity-mcp material set-color "Assets/Materials/Blue.mat" 0 0 1 0.5
        unity-mcp material set-color "Assets/Materials/Mat.mat" 1 1 0 --property "_BaseColor"
    """
    config = get_config()

    params: dict[str, Any] = {
        "action": "set_material_color",
        "materialPath": path,
        "property": property,
        "color": [r, g, b, a],
    }

    result = run_command("manage_material", params, config)
    click.echo(format_output(result, config.format))
    if result.get("success"):
        print_success(f"Set color on: {path}")


@material.command("set-property")
@click.argument("path")
@click.argument("property_name")
@click.argument("value")
@handle_unity_errors
def set_property(path: str, property_name: str, value: str):
    """Set a shader property on a material.

    \b
    Examples:
        unity-mcp material set-property "Assets/Materials/Mat.mat" _Metallic 0.5
        unity-mcp material set-property "Assets/Materials/Mat.mat" _Smoothness 0.8
        unity-mcp material set-property "Assets/Materials/Mat.mat" _MainTex "Assets/Textures/Tex.png"
    """
    config = get_config()

    # Try to parse value as JSON for complex types
    parsed_value = parse_value_safe(value)

    params: dict[str, Any] = {
        "action": "set_material_shader_property",
        "materialPath": path,
        "property": property_name,
        "value": parsed_value,
    }

    result = run_command("manage_material", params, config)
    click.echo(format_output(result, config.format))
    if result.get("success"):
        print_success(f"Set {property_name} on: {path}")


@material.command("assign")
@click.argument("material_path")
@click.argument("target")
@click.option(
    "--search-method",
    type=SEARCH_METHOD_CHOICE_RENDERER,
    default=None,
    help="How to find the target GameObject."
)
@click.option(
    "--slot", "-s",
    default=0,
    type=int,
    help="Material slot index (default: 0)."
)
@click.option(
    "--mode", "-m",
    type=click.Choice(["shared", "instance", "property_block"]),
    default="shared",
    help="Assignment mode."
)
@handle_unity_errors
def assign(material_path: str, target: str, search_method: Optional[str], slot: int, mode: str):
    """Assign a material to a GameObject's renderer.

    \b
    Examples:
        unity-mcp material assign "Assets/Materials/Red.mat" "Cube"
        unity-mcp material assign "Assets/Materials/Blue.mat" "Player" --mode instance
        unity-mcp material assign "Assets/Materials/Mat.mat" "-81840" --search-method by_id --slot 1
    """
    config = get_config()

    params: dict[str, Any] = {
        "action": "assign_material_to_renderer",
        "materialPath": material_path,
        "target": target,
        "slot": slot,
        "mode": mode,
    }

    if search_method:
        params["searchMethod"] = search_method

    result = run_command("manage_material", params, config)
    click.echo(format_output(result, config.format))
    if result.get("success"):
        print_success(f"Assigned material to: {target}")


@material.command("set-renderer-color")
@click.argument("target")
@click.argument("r", type=float)
@click.argument("g", type=float)
@click.argument("b", type=float)
@click.argument("a", type=float, default=1.0)
@click.option(
    "--search-method",
    type=SEARCH_METHOD_CHOICE_RENDERER,
    default=None,
    help="How to find the target GameObject."
)
@click.option(
    "--mode", "-m",
    type=click.Choice(["shared", "instance", "property_block"]),
    default="property_block",
    help="Modification mode."
)
@handle_unity_errors
def set_renderer_color(target: str, r: float, g: float, b: float, a: float, search_method: Optional[str], mode: str):
    """Set a renderer's material color directly.

    \b
    Examples:
        unity-mcp material set-renderer-color "Cube" 1 0 0
        unity-mcp material set-renderer-color "Player" 0 1 0 --mode instance
    """
    config = get_config()

    params: dict[str, Any] = {
        "action": "set_renderer_color",
        "target": target,
        "color": [r, g, b, a],
        "mode": mode,
    }

    if search_method:
        params["searchMethod"] = search_method

    result = run_command("manage_material", params, config)
    click.echo(format_output(result, config.format))
    if result.get("success"):
        print_success(f"Set renderer color on: {target}")
