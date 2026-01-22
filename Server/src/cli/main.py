"""Unity MCP Command Line Interface - Main Entry Point."""

import sys
from importlib import import_module

import click
from typing import Optional

from cli import __version__
from cli.utils.config import CLIConfig, set_config, get_config
from cli.utils.output import format_output, print_error, print_success, print_info
from cli.utils.connection import (
    run_command,
    run_check_connection,
    run_list_instances,
    UnityConnectionError,
    warn_if_remote_host,
)


# Context object to pass configuration between commands
class Context:
    def __init__(self):
        self.config: Optional[CLIConfig] = None
        self.verbose: bool = False


pass_context = click.make_pass_decorator(Context, ensure=True)


@click.group()
@click.version_option(version=__version__, prog_name="unity-mcp")
@click.option(
    "--host", "-h",
    default="127.0.0.1",
    envvar="UNITY_MCP_HOST",
    help="MCP server host address."
)
@click.option(
    "--port", "-p",
    default=8080,
    type=int,
    envvar="UNITY_MCP_HTTP_PORT",
    help="MCP server port."
)
@click.option(
    "--timeout", "-t",
    default=30,
    type=int,
    envvar="UNITY_MCP_TIMEOUT",
    help="Command timeout in seconds."
)
@click.option(
    "--format", "-f",
    type=click.Choice(["text", "json", "table"]),
    default="text",
    envvar="UNITY_MCP_FORMAT",
    help="Output format."
)
@click.option(
    "--instance", "-i",
    default=None,
    envvar="UNITY_MCP_INSTANCE",
    help="Target Unity instance (hash or Name@hash)."
)
@click.option(
    "--verbose", "-v",
    is_flag=True,
    help="Enable verbose output."
)
@pass_context
def cli(ctx: Context, host: str, port: int, timeout: int, format: str, instance: Optional[str], verbose: bool):
    """Unity MCP Command Line Interface.

    Control Unity Editor directly from the command line using the Model Context Protocol.

    \b
    Examples:
        unity-mcp status
        unity-mcp gameobject find "Player"
        unity-mcp scene hierarchy --format json
        unity-mcp editor play

    \b
    Environment Variables:
        UNITY_MCP_HOST      Server host (default: 127.0.0.1)
        UNITY_MCP_HTTP_PORT Server port (default: 8080)
        UNITY_MCP_TIMEOUT   Timeout in seconds (default: 30)
        UNITY_MCP_FORMAT    Output format (default: text)
        UNITY_MCP_INSTANCE  Target Unity instance
    """
    config = CLIConfig(
        host=host,
        port=port,
        timeout=timeout,
        format=format,
        unity_instance=instance,
    )

    # Security warning for non-localhost connections
    warn_if_remote_host(config)

    set_config(config)
    ctx.config = config
    ctx.verbose = verbose


@cli.command("status")
@pass_context
def status(ctx: Context):
    """Check connection status to Unity MCP server."""
    config = ctx.config or get_config()

    click.echo(f"Checking connection to {config.host}:{config.port}...")

    if run_check_connection(config):
        print_success(
            f"Connected to Unity MCP server at {config.host}:{config.port}")

        # Try to get Unity instances
        try:
            result = run_list_instances(config)
            instances = result.get("instances", []) if isinstance(
                result, dict) else []
            if instances:
                click.echo("\nConnected Unity instances:")
                for inst in instances:
                    project = inst.get("project", "Unknown")
                    version = inst.get("unity_version", "Unknown")
                    hash_id = inst.get("hash", "")[:8]
                    click.echo(f"  • {project} (Unity {version}) [{hash_id}]")
            else:
                print_info("No Unity instances currently connected")
        except UnityConnectionError as e:
            print_info(f"Could not retrieve Unity instances: {e}")
    else:
        print_error(
            f"Cannot connect to Unity MCP server at {config.host}:{config.port}")
        sys.exit(1)


@cli.command("instances")
@pass_context
def list_instances(ctx: Context):
    """List available Unity instances."""
    config = ctx.config or get_config()

    try:
        instances = run_list_instances(config)
        click.echo(format_output(instances, config.format))
    except UnityConnectionError as e:
        print_error(str(e))
        sys.exit(1)


@cli.command("raw")
@click.argument("command_type")
@click.argument("params", required=False, default="{}")
@pass_context
def raw_command(ctx: Context, command_type: str, params: str):
    """Send a raw command to Unity.

    \b
    Examples:
        unity-mcp raw manage_scene '{"action": "get_hierarchy"}'
        unity-mcp raw read_console '{"count": 10}'
    """
    import json
    config = ctx.config or get_config()

    try:
        params_dict = json.loads(params)
    except json.JSONDecodeError as e:
        print_error(f"Invalid JSON params: {e}")
        sys.exit(1)

    try:
        result = run_command(command_type, params_dict, config)
        click.echo(format_output(result, config.format))
    except UnityConnectionError as e:
        print_error(str(e))
        sys.exit(1)


# Import and register command groups
# These will be implemented in subsequent TODOs
def register_commands():
    """Register all command groups."""
    def register_optional_command(module_name: str, command_name: str) -> None:
        try:
            module = import_module(module_name)
        except ModuleNotFoundError as e:
            if e.name == module_name:
                return
            print_error(
                f"Failed to load command module '{module_name}': {e}"
            )
            return
        except Exception as e:
            print_error(
                f"Failed to load command module '{module_name}': {e}"
            )
            return

        command = getattr(module, command_name, None)
        if command is None:
            print_error(
                f"Command '{command_name}' not found in '{module_name}'"
            )
            return

        cli.add_command(command)

    optional_commands = [
        ("cli.commands.gameobject", "gameobject"),
        ("cli.commands.component", "component"),
        ("cli.commands.scene", "scene"),
        ("cli.commands.asset", "asset"),
        ("cli.commands.script", "script"),
        ("cli.commands.code", "code"),
        ("cli.commands.editor", "editor"),
        ("cli.commands.prefab", "prefab"),
        ("cli.commands.material", "material"),
        ("cli.commands.lighting", "lighting"),
        ("cli.commands.animation", "animation"),
        ("cli.commands.audio", "audio"),
        ("cli.commands.ui", "ui"),
        ("cli.commands.instance", "instance"),
        ("cli.commands.shader", "shader"),
        ("cli.commands.vfx", "vfx"),
        ("cli.commands.batch", "batch"),
    ]

    for module_name, command_name in optional_commands:
        register_optional_command(module_name, command_name)


# Register commands on import
register_commands()


def main():
    """Main entry point for the CLI."""
    cli()


if __name__ == "__main__":
    main()
