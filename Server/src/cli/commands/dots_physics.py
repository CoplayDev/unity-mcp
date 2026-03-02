"""DOTS Physics CLI commands for debugging collision queries and body inspection."""

import click
from typing import Optional, Any

from cli.utils.config import get_config
from cli.utils.output import format_output
from cli.utils.connection import run_command, handle_unity_errors


@click.group()
def dots_physics():
    """DOTS Physics operations — raycasts, overlaps, bodies."""
    pass


@dots_physics.command("world")
@click.option("--world", "-w", default=None, help="Target world name.")
@handle_unity_errors
def physics_world(world: Optional[str]):
    """Get physics world stats (body counts, joints).

    \b
    Examples:
        unity-mcp dots-physics world
    """
    config = get_config()

    params: dict[str, Any] = {"action": "get_physics_world"}
    if world:
        params["world"] = world

    result = run_command("manage_dots_physics", params, config)
    click.echo(format_output(result, config.format))


@dots_physics.command("raycast")
@click.argument("origin")
@click.argument("direction")
@click.option("--distance", "-d", type=float, default=None, help="Max ray distance (default 100).")
@click.option("--world", "-w", default=None, help="Target world name.")
@handle_unity_errors
def raycast(origin: str, direction: str, distance: Optional[float], world: Optional[str]):
    """Cast a ray and get hit entities.

    \b
    Examples:
        unity-mcp dots-physics raycast "0,1,0" "0,-1,0"
        unity-mcp dots-physics raycast "0,10,0" "0,-1,0" --distance 50
    """
    config = get_config()

    params: dict[str, Any] = {
        "action": "raycast",
        "origin": origin,
        "direction": direction,
    }
    if distance:
        params["max_distance"] = distance
    if world:
        params["world"] = world

    result = run_command("manage_dots_physics", params, config)
    click.echo(format_output(result, config.format))


@dots_physics.command("overlap")
@click.argument("min_corner")
@click.argument("max_corner")
@click.option("--world", "-w", default=None, help="Target world name.")
@click.option("--page-size", "-n", type=int, default=None, help="Max results (default 20).")
@handle_unity_errors
def overlap(min_corner: str, max_corner: str, world: Optional[str], page_size: Optional[int]):
    """Find bodies in an axis-aligned bounding box.

    \b
    Examples:
        unity-mcp dots-physics overlap "-5,-5,-5" "5,5,5"
        unity-mcp dots-physics overlap "0,0,0" "10,10,10" --page-size 50
    """
    config = get_config()

    params: dict[str, Any] = {
        "action": "overlap_aabb",
        "min": min_corner,
        "max": max_corner,
    }
    if world:
        params["world"] = world
    if page_size:
        params["page_size"] = page_size

    result = run_command("manage_dots_physics", params, config)
    click.echo(format_output(result, config.format))


@dots_physics.command("colliders")
@click.option("--world", "-w", default=None, help="Target world name.")
@click.option("--page-size", "-n", type=int, default=None, help="Max results (default 20).")
@handle_unity_errors
def colliders(world: Optional[str], page_size: Optional[int]):
    """List entities with PhysicsCollider.

    \b
    Examples:
        unity-mcp dots-physics colliders
        unity-mcp dots-physics colliders --page-size 50
    """
    config = get_config()

    params: dict[str, Any] = {"action": "list_colliders"}
    if world:
        params["world"] = world
    if page_size:
        params["page_size"] = page_size

    result = run_command("manage_dots_physics", params, config)
    click.echo(format_output(result, config.format))


@dots_physics.command("body")
@click.argument("body_index", type=int)
@click.option("--world", "-w", default=None, help="Target world name.")
@handle_unity_errors
def body(body_index: int, world: Optional[str]):
    """Inspect a physics body by index.

    \b
    Examples:
        unity-mcp dots-physics body 0
        unity-mcp dots-physics body 5 --world "Server World"
    """
    config = get_config()

    params: dict[str, Any] = {
        "action": "get_body",
        "body_index": body_index,
    }
    if world:
        params["world"] = world

    result = run_command("manage_dots_physics", params, config)
    click.echo(format_output(result, config.format))
