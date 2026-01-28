"""Animation CLI commands - placeholder for future implementation."""

import click
from typing import Optional, Any

from cli.utils.config import get_config
from cli.utils.output import format_output, print_error, print_info
from cli.utils.connection import run_command, handle_unity_errors
from cli.utils.constants import SEARCH_METHOD_CHOICE_BASIC


@click.group()
def animation():
    """Animation operations - control Animator, play animations."""
    pass


@animation.command("play")
@click.argument("target")
@click.argument("state_name")
@click.option(
    "--layer", "-l",
    default=0,
    type=int,
    help="Animator layer(TODO)."
)
@click.option(
    "--search-method",
    type=SEARCH_METHOD_CHOICE_BASIC,
    default=None,
    help="How to find the target."
)
@handle_unity_errors
def play(target: str, state_name: str, layer: int, search_method: Optional[str]):
    """Play an animation state on a target's Animator.

    \b
    Examples:
        unity-mcp animation play "Player" "Walk"
        unity-mcp animation play "Enemy" "Attack" --layer 1
    """
    config = get_config()

    # Set Animator parameter to trigger state
    params: dict[str, Any] = {
        "action": "set_property",
        "target": target,
        "componentType": "Animator",
        "property": "Play",
        "value": state_name,
        "layer": layer,
    }

    if search_method:
        params["searchMethod"] = search_method

    result = run_command("manage_components", params, config)
    click.echo(format_output(result, config.format))


@animation.command("set-parameter")
@click.argument("target")
@click.argument("param_name")
@click.argument("value")
@click.option(
    "--type", "-t",
    "param_type",
    type=click.Choice(["float", "int", "bool", "trigger"]),
    default="float",
    help="Parameter type."
)
def set_parameter(target: str, param_name: str, value: str, param_type: str):
    """Set an Animator parameter.

    \b
    Examples:
        unity-mcp animation set-parameter "Player" "Speed" 5.0
        unity-mcp animation set-parameter "Player" "IsRunning" true --type bool
        unity-mcp animation set-parameter "Player" "Jump" "" --type trigger
    """
    config = get_config()
    print_info(
        "Animation parameter command - requires custom Unity implementation")
    click.echo(f"Would set {param_name}={value} ({param_type}) on {target}")
