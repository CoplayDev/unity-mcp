"""DOTS ECS CLI commands for debugging, inspection, and performance monitoring."""

import click
from typing import Optional, Any

from cli.utils.config import get_config
from cli.utils.output import format_output, print_error, print_success, print_info
from cli.utils.connection import run_command, handle_unity_errors


@click.group()
def dots():
    """DOTS ECS operations - worlds, entities, systems, performance."""
    pass


@dots.command("worlds")
@handle_unity_errors
def worlds():
    """List all ECS Worlds.

    \b
    Examples:
        unity-mcp dots worlds
    """
    config = get_config()
    result = run_command("manage_dots", {"action": "list_worlds"}, config)
    click.echo(format_output(result, config.format))


@dots.command("entities")
@click.argument("component_types")
@click.option("--world", "-w", default=None, help="Target world name.")
@click.option("--page-size", "-n", type=int, default=None, help="Max entities to return (default 20).")
@handle_unity_errors
def entities(component_types: str, world: Optional[str], page_size: Optional[int]):
    """Query entities by component types (comma-separated).

    \b
    Examples:
        unity-mcp dots entities "LocalTransform,Velocity"
        unity-mcp dots entities "Health" --world "Server World" --page-size 50
    """
    config = get_config()

    params: dict[str, Any] = {
        "action": "query_entities",
        "component_types": component_types,
    }
    if world:
        params["world"] = world
    if page_size:
        params["page_size"] = page_size

    result = run_command("manage_dots", params, config)
    click.echo(format_output(result, config.format))


@dots.command("entity")
@click.argument("entity_index", type=int)
@click.option("--version", "-v", type=int, default=None, help="Entity version (default 1).")
@click.option("--world", "-w", default=None, help="Target world name.")
@handle_unity_errors
def entity(entity_index: int, version: Optional[int], world: Optional[str]):
    """Inspect a single entity's components and field values.

    \b
    Examples:
        unity-mcp dots entity 42
        unity-mcp dots entity 42 --version 2 --world "Client World"
    """
    config = get_config()

    params: dict[str, Any] = {
        "action": "get_entity",
        "entity_index": entity_index,
    }
    if version:
        params["entity_version"] = version
    if world:
        params["world"] = world

    result = run_command("manage_dots", params, config)
    click.echo(format_output(result, config.format))


@dots.command("systems")
@click.option("--world", "-w", default=None, help="Target world name.")
@click.option("--group", "-g", default=None, help="Filter by system group name.")
@handle_unity_errors
def systems(world: Optional[str], group: Optional[str]):
    """List all systems in a world.

    \b
    Examples:
        unity-mcp dots systems
        unity-mcp dots systems --world "Server World" --group "Simulation"
    """
    config = get_config()

    params: dict[str, Any] = {"action": "list_systems"}
    if world:
        params["world"] = world
    if group:
        params["group"] = group

    result = run_command("manage_dots", params, config)
    click.echo(format_output(result, config.format))


@dots.command("system")
@click.argument("system_name")
@click.option("--world", "-w", default=None, help="Target world name.")
@handle_unity_errors
def system(system_name: str, world: Optional[str]):
    """Get details of a specific system.

    \b
    Examples:
        unity-mcp dots system "TransformSystemGroup"
        unity-mcp dots system "MyMovementSystem" --world "Client World"
    """
    config = get_config()

    params: dict[str, Any] = {
        "action": "get_system",
        "system_name": system_name,
    }
    if world:
        params["world"] = world

    result = run_command("manage_dots", params, config)
    click.echo(format_output(result, config.format))


@dots.command("perf")
@click.option("--world", "-w", default=None, help="Target world name.")
@click.option("--limit", "-n", type=int, default=None, help="Max archetypes to show (default 20).")
@handle_unity_errors
def perf(world: Optional[str], limit: Optional[int]):
    """Get performance snapshot - chunk utilization, archetypes, entity counts.

    \b
    Examples:
        unity-mcp dots perf
        unity-mcp dots perf --world "Server World" --limit 10
    """
    config = get_config()

    params: dict[str, Any] = {"action": "performance_snapshot"}
    if world:
        params["world"] = world
    if limit:
        params["limit"] = limit

    result = run_command("manage_dots", params, config)
    click.echo(format_output(result, config.format))


@dots.command("toggle")
@click.argument("system_name")
@click.argument("enabled", type=click.BOOL)
@click.option("--world", "-w", default=None, help="Target world name.")
@handle_unity_errors
def toggle(system_name: str, enabled: bool, world: Optional[str]):
    """Enable or disable a system for debugging.

    \b
    Examples:
        unity-mcp dots toggle "MyMovementSystem" false
        unity-mcp dots toggle "PhysicsSimulationGroup" true --world "Server World"
    """
    config = get_config()

    params: dict[str, Any] = {
        "action": "toggle_system",
        "system_name": system_name,
        "enabled": enabled,
    }
    if world:
        params["world"] = world

    result = run_command("manage_dots", params, config)
    click.echo(format_output(result, config.format))
    if result.get("success"):
        state = "enabled" if enabled else "disabled"
        print_success(f"System '{system_name}' {state}")


@dots.command("types")
@click.option("--filter", "-f", default=None, help="Filter by component name.")
@click.option("--category", "-c", default=None, help="Filter by category (ComponentData, BufferData, etc.).")
@click.option("--page-size", "-n", type=int, default=None, help="Max types to return (default 50).")
@handle_unity_errors
def types(filter: Optional[str], category: Optional[str], page_size: Optional[int]):
    """List all registered ECS component types.

    \b
    Examples:
        unity-mcp dots types
        unity-mcp dots types --filter "Transform"
        unity-mcp dots types --category "BufferData" --page-size 100
    """
    config = get_config()

    params: dict[str, Any] = {"action": "list_component_types"}
    if filter:
        params["filter"] = filter
    if category:
        params["category"] = category
    if page_size:
        params["page_size"] = page_size

    result = run_command("manage_dots", params, config)
    click.echo(format_output(result, config.format))


@dots.command("create")
@click.option("--components", "-c", default=None, help="Comma-separated component types (e.g. 'LocalTransform,Velocity').")
@click.option("--world", "-w", default=None, help="Target world name.")
@handle_unity_errors
def create(components: Optional[str], world: Optional[str]):
    """Create a debug entity with optional components.

    \b
    Examples:
        unity-mcp dots create
        unity-mcp dots create --components "LocalTransform,Velocity"
        unity-mcp dots create --components "Health" --world "Server World"
    """
    config = get_config()

    params: dict[str, Any] = {"action": "create_entity"}
    if components:
        params["component_types"] = components
    if world:
        params["world"] = world

    result = run_command("manage_dots", params, config)
    click.echo(format_output(result, config.format))


@dots.command("destroy")
@click.argument("entity_index", type=int)
@click.option("--version", "-v", type=int, default=None, help="Entity version (default 1).")
@click.option("--world", "-w", default=None, help="Target world name.")
@handle_unity_errors
def destroy(entity_index: int, version: Optional[int], world: Optional[str]):
    """Destroy an entity by index.

    \b
    Examples:
        unity-mcp dots destroy 42
        unity-mcp dots destroy 42 --version 2 --world "Server World"
    """
    config = get_config()

    params: dict[str, Any] = {
        "action": "destroy_entity",
        "entity_index": entity_index,
    }
    if version:
        params["entity_version"] = version
    if world:
        params["world"] = world

    result = run_command("manage_dots", params, config)
    click.echo(format_output(result, config.format))
