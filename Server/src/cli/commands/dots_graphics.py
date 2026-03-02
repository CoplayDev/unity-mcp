"""DOTS Graphics CLI commands for inspecting ECS rendering state."""

import click
from typing import Optional, Any

from cli.utils.config import get_config
from cli.utils.output import format_output
from cli.utils.connection import run_command, handle_unity_errors


@click.group()
def dots_graphics():
    """DOTS Graphics operations — render stats, materials, meshes."""
    pass


@dots_graphics.command("stats")
@click.option("--world", "-w", default=None, help="Target world name.")
@handle_unity_errors
def render_stats(world: Optional[str]):
    """Get rendering statistics (rendered entity counts, LOD groups).

    \b
    Examples:
        unity-mcp dots-graphics stats
    """
    config = get_config()

    params: dict[str, Any] = {"action": "get_render_stats"}
    if world:
        params["world"] = world

    result = run_command("manage_dots_graphics", params, config)
    click.echo(format_output(result, config.format))


@dots_graphics.command("entities")
@click.option("--world", "-w", default=None, help="Target world name.")
@click.option("--page-size", "-n", type=int, default=None, help="Max results (default 20).")
@handle_unity_errors
def rendered_entities(world: Optional[str], page_size: Optional[int]):
    """List entities with rendering components (MaterialMeshInfo).

    \b
    Examples:
        unity-mcp dots-graphics entities
        unity-mcp dots-graphics entities --page-size 50
    """
    config = get_config()

    params: dict[str, Any] = {"action": "list_rendered_entities"}
    if world:
        params["world"] = world
    if page_size:
        params["page_size"] = page_size

    result = run_command("manage_dots_graphics", params, config)
    click.echo(format_output(result, config.format))


@dots_graphics.command("entity")
@click.argument("entity_index", type=int)
@click.option("--world", "-w", default=None, help="Target world name.")
@handle_unity_errors
def entity_rendering(entity_index: int, world: Optional[str]):
    """Inspect rendering details for a specific entity.

    \b
    Examples:
        unity-mcp dots-graphics entity 42
    """
    config = get_config()

    params: dict[str, Any] = {
        "action": "get_entity_rendering",
        "entity_index": entity_index,
    }
    if world:
        params["world"] = world

    result = run_command("manage_dots_graphics", params, config)
    click.echo(format_output(result, config.format))


@dots_graphics.command("materials")
@click.option("--world", "-w", default=None, help="Target world name.")
@click.option("--page-size", "-n", type=int, default=None, help="Max results (default 20).")
@handle_unity_errors
def materials(world: Optional[str], page_size: Optional[int]):
    """List unique materials from RenderMeshArrays.

    \b
    Examples:
        unity-mcp dots-graphics materials
    """
    config = get_config()

    params: dict[str, Any] = {"action": "list_registered_materials"}
    if world:
        params["world"] = world
    if page_size:
        params["page_size"] = page_size

    result = run_command("manage_dots_graphics", params, config)
    click.echo(format_output(result, config.format))


@dots_graphics.command("meshes")
@click.option("--world", "-w", default=None, help="Target world name.")
@click.option("--page-size", "-n", type=int, default=None, help="Max results (default 20).")
@handle_unity_errors
def meshes(world: Optional[str], page_size: Optional[int]):
    """List unique meshes with vertex counts.

    \b
    Examples:
        unity-mcp dots-graphics meshes
    """
    config = get_config()

    params: dict[str, Any] = {"action": "list_registered_meshes"}
    if world:
        params["world"] = world
    if page_size:
        params["page_size"] = page_size

    result = run_command("manage_dots_graphics", params, config)
    click.echo(format_output(result, config.format))
