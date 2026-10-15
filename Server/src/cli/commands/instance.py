"""Instance CLI commands for managing Unity instances."""

import click
from typing import Optional

from cli.utils.config import get_config
from cli.utils.output import print_info
from cli.utils.connection import run_list_instances, handle_unity_errors


@click.group()
def instance():
    """Unity instance management - list the connected instances and show the one in use."""
    pass


@instance.command("list")
@handle_unity_errors
def list_instances():
    """List available Unity instances.

    \b
    Examples:
        unity-mcp instance list
    """
    config = get_config()

    result = run_list_instances(config)
    instances = result.get("instances", []) if isinstance(
        result, dict) else []

    if not instances:
        print_info("No Unity instances currently connected")
        return

    click.echo("Available Unity instances:")
    for inst in instances:
        project = inst.get("project", "Unknown")
        version = inst.get("unity_version", "Unknown")
        hash_id = inst.get("hash", "")
        session_id = inst.get("session_id", "")

        # Format: ProjectName@hash (Unity version)
        display_id = f"{project}@{hash_id}" if hash_id else project
        click.echo(f"  • {display_id} (Unity {version})")
        if session_id:
            click.echo(f"    Session: {session_id[:8]}...")


@instance.command("current")
def current_instance():
    """Show the currently selected Unity instance.

    \b
    Examples:
        unity-mcp instance current
    """
    config = get_config()

    # The current instance is typically shown in telemetry or needs to be tracked
    # For now, we can show the configured instance from CLI options
    if config.unity_instance:
        click.echo(f"Configured instance: {config.unity_instance}")
    else:
        print_info(
            "No instance explicitly set. Using default (auto-select single instance).")
        print_info("Use 'unity-mcp instance list' to see available instances.")
        print_info("Choose one per call with --instance <Name@hash>, or for a whole shell with UNITY_MCP_INSTANCE.")
