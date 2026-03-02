"""DOTS SubScene CLI commands for managing scene loading and streaming."""

import click
from typing import Optional, Any

from cli.utils.config import get_config
from cli.utils.output import format_output
from cli.utils.connection import run_command, handle_unity_errors


@click.group()
def dots_subscene():
    """DOTS SubScene operations — load, unload, status, sections."""
    pass


@dots_subscene.command("list")
@handle_unity_errors
def list_subscenes():
    """List all SubScenes in the current hierarchy.

    \b
    Examples:
        unity-mcp dots-subscene list
    """
    config = get_config()

    params: dict[str, Any] = {"action": "list_subscenes"}

    result = run_command("manage_dots_subscene", params, config)
    click.echo(format_output(result, config.format))


@dots_subscene.command("load")
@click.argument("scene_name")
@handle_unity_errors
def load_subscene(scene_name: str):
    """Load a SubScene by name.

    \b
    Examples:
        unity-mcp dots-subscene load "Environment"
    """
    config = get_config()

    params: dict[str, Any] = {
        "action": "load_subscene",
        "scene_name": scene_name,
    }

    result = run_command("manage_dots_subscene", params, config)
    click.echo(format_output(result, config.format))


@dots_subscene.command("unload")
@click.argument("scene_name")
@handle_unity_errors
def unload_subscene(scene_name: str):
    """Unload a SubScene by name.

    \b
    Examples:
        unity-mcp dots-subscene unload "Environment"
    """
    config = get_config()

    params: dict[str, Any] = {
        "action": "unload_subscene",
        "scene_name": scene_name,
    }

    result = run_command("manage_dots_subscene", params, config)
    click.echo(format_output(result, config.format))


@dots_subscene.command("status")
@click.argument("scene_name")
@handle_unity_errors
def subscene_status(scene_name: str):
    """Get detailed status for a SubScene.

    \b
    Examples:
        unity-mcp dots-subscene status "Environment"
    """
    config = get_config()

    params: dict[str, Any] = {
        "action": "get_subscene_status",
        "scene_name": scene_name,
    }

    result = run_command("manage_dots_subscene", params, config)
    click.echo(format_output(result, config.format))


@dots_subscene.command("sections")
@click.argument("scene_name")
@handle_unity_errors
def list_sections(scene_name: str):
    """List sections of a SubScene with their load state.

    \b
    Examples:
        unity-mcp dots-subscene sections "Environment"
    """
    config = get_config()

    params: dict[str, Any] = {
        "action": "list_sections",
        "scene_name": scene_name,
    }

    result = run_command("manage_dots_subscene", params, config)
    click.echo(format_output(result, config.format))
