"""Tool CLI commands for listing custom tools."""

import sys
import click

from cli.utils.config import get_config
from cli.utils.output import format_output, print_error
from cli.utils.connection import run_list_custom_tools, UnityConnectionError


def _list_custom_tools() -> None:
    config = get_config()
    try:
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
        for i, tool in enumerate(tools):
            name = tool.get("name") if isinstance(tool, dict) else str(tool)
            click.echo(f"  [{i}] {name}")
    except UnityConnectionError as e:
        print_error(str(e))
        sys.exit(1)


@click.group("tool")
def tool():
    """Tool management - list custom tools for the active Unity project."""
    pass


@tool.command("list")
def list_tools():
    """List custom tools registered for the active Unity project."""
    _list_custom_tools()


@click.group("custom_tool")
def custom_tool():
    """Alias for tool management (custom tools)."""
    pass


@custom_tool.command("list")
def list_custom_tools():
    """List custom tools registered for the active Unity project."""
    _list_custom_tools()
