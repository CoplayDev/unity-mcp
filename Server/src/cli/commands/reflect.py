"""Unity API reflection CLI commands."""

import click

from cli.utils.config import get_config
from cli.utils.output import format_output
from cli.utils.connection import run_command, handle_unity_errors


@click.group()
def reflect():
    """Inspect Unity C# APIs via reflection."""
    pass


@reflect.command("type")
@click.argument("class_name")
@handle_unity_errors
def get_type(class_name: str):
    """Get member summary for a Unity type.

    \b
    Examples:
        unity-mcp reflect type NavMeshAgent
        unity-mcp reflect type UnityEngine.Physics
    """
    config = get_config()
    result = run_command("unity_reflect", {"action": "get_type", "class_name": class_name}, config)
    click.echo(format_output(result, config.format))


@reflect.command("member")
@click.argument("class_name")
@click.argument("member_name")
@handle_unity_errors
def get_member(class_name: str, member_name: str):
    """Get detailed info for a specific member.

    \b
    Examples:
        unity-mcp reflect member Physics Raycast
        unity-mcp reflect member NavMeshAgent SetDestination
    """
    config = get_config()
    result = run_command("unity_reflect", {
        "action": "get_member",
        "class_name": class_name,
        "member_name": member_name,
    }, config)
    click.echo(format_output(result, config.format))


@reflect.command("search")
@click.argument("query")
@click.option("--scope", "-s", default="unity", type=click.Choice(["unity", "packages", "project", "all"]),
              help="Assembly scope to search.")
@handle_unity_errors
def search(query: str, scope: str):
    """Search for Unity types by name.

    \b
    Examples:
        unity-mcp reflect search NavMesh
        unity-mcp reflect search Camera --scope all
    """
    config = get_config()
    result = run_command("unity_reflect", {
        "action": "search",
        "query": query,
        "scope": scope,
    }, config)
    click.echo(format_output(result, config.format))
