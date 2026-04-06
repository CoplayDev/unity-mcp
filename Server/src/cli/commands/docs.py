"""Unity documentation lookup CLI commands."""

import asyncio
import click

from cli.utils.config import get_config
from cli.utils.output import format_output


@click.group()
def docs():
    """Fetch Unity API documentation."""
    pass


@docs.command("get")
@click.argument("class_name")
@click.argument("member_name", required=False)
@click.option("--version", "-v", default=None, help="Unity version (e.g., 6000.0).")
def get_doc(class_name: str, member_name: str | None, version: str | None):
    """Fetch documentation for a Unity class or member.

    \b
    Examples:
        unity-mcp docs get Physics
        unity-mcp docs get Physics Raycast
        unity-mcp docs get NavMeshAgent SetDestination --version 6000.0
    """
    from services.tools.unity_docs import _get_doc

    config = get_config()
    result = asyncio.run(_get_doc(class_name, member_name, version))
    click.echo(format_output(result, config.format))
