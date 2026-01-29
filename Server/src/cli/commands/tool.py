"""Tool CLI commands for listing custom tools."""

import click

from cli.utils.config import get_config
from cli.utils.output import format_output, print_error
from cli.utils.connection import run_list_custom_tools, handle_unity_errors


def _list_custom_tools() -> None:
    config = get_config()
    result = run_list_custom_tools(config)
    if config.format != "text":
        click.echo(format_output(result, config.format))
        return

    if not isinstance(result, dict) or not result.get("success", True):
        click.echo(format_output(result, config.format))
        return

    tools = result.get("tools")
    if tools is None:
        data = result.get("data", {})
        tools = data.get("tools") if isinstance(data, dict) else None
    if not isinstance(tools, list):
        click.echo(format_output(result, config.format))
        return

    click.echo(f"Custom tools ({len(tools)}):")
    for i, t in enumerate(tools):
        name = t.get("name") if isinstance(t, dict) else str(t)
        click.echo(f"  [{i}] {name}")


@click.group("tool")
def tool():
    """Tool management - list custom tools for the active Unity project."""
    pass


@tool.command("list")
@handle_unity_errors
def list_tools():
    """List custom tools registered for the active Unity project."""
    _list_custom_tools()


@click.group("custom_tool")
def custom_tool():
    """Alias for tool management (custom tools)."""
    pass


@custom_tool.command("list")
@handle_unity_errors
def list_custom_tools():
    """List custom tools registered for the active Unity project."""
    _list_custom_tools()
