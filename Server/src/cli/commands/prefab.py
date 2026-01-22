"""Prefab CLI commands."""

import sys
import click
from typing import Optional, Any

from cli.utils.config import get_config
from cli.utils.output import format_output, print_error, print_success
from cli.utils.connection import run_command, UnityConnectionError


@click.group()
def prefab():
    """Prefab operations - open, save, create prefabs."""
    pass


@prefab.command("open")
@click.argument("path")
@click.option(
    "--mode", "-m",
    default="InIsolation",
    help="Prefab stage mode (InIsolation)."
)
def open_stage(path: str, mode: str):
    """Open a prefab in the prefab stage for editing.

    \b
    Examples:
        unity-mcp prefab open "Assets/Prefabs/Player.prefab"
    """
    config = get_config()

    params: dict[str, Any] = {
        "action": "open_stage",
        "prefabPath": path,
        "mode": mode,
    }

    try:
        result = run_command("manage_prefabs", params, config)
        click.echo(format_output(result, config.format))
        if result.get("success"):
            print_success(f"Opened prefab: {path}")
    except UnityConnectionError as e:
        print_error(str(e))
        sys.exit(1)


@prefab.command("close")
@click.option(
    "--save", "-s",
    is_flag=True,
    help="Save the prefab before closing."
)
def close_stage(save: bool):
    """Close the current prefab stage.

    \b
    Examples:
        unity-mcp prefab close
        unity-mcp prefab close --save
    """
    config = get_config()

    params: dict[str, Any] = {
        "action": "close_stage",
    }
    if save:
        params["saveBeforeClose"] = True

    try:
        result = run_command("manage_prefabs", params, config)
        click.echo(format_output(result, config.format))
        if result.get("success"):
            print_success("Closed prefab stage")
    except UnityConnectionError as e:
        print_error(str(e))
        sys.exit(1)


@prefab.command("save")
def save_stage():
    """Save the currently open prefab stage.

    \b
    Examples:
        unity-mcp prefab save
    """
    config = get_config()

    try:
        result = run_command("manage_prefabs", {
                             "action": "save_open_stage"}, config)
        click.echo(format_output(result, config.format))
        if result.get("success"):
            print_success("Saved prefab")
    except UnityConnectionError as e:
        print_error(str(e))
        sys.exit(1)


@prefab.command("create")
@click.argument("target")
@click.argument("path")
@click.option(
    "--overwrite",
    is_flag=True,
    help="Overwrite existing prefab at path."
)
@click.option(
    "--include-inactive",
    is_flag=True,
    help="Include inactive objects when finding target."
)
def create(target: str, path: str, overwrite: bool, include_inactive: bool):
    """Create a prefab from a scene GameObject.

    \b
    Examples:
        unity-mcp prefab create "Player" "Assets/Prefabs/Player.prefab"
        unity-mcp prefab create "Enemy" "Assets/Prefabs/Enemy.prefab" --overwrite
    """
    config = get_config()

    params: dict[str, Any] = {
        "action": "create_from_gameobject",
        "target": target,
        "prefabPath": path,
    }

    if overwrite:
        params["allowOverwrite"] = True
    if include_inactive:
        params["searchInactive"] = True

    try:
        result = run_command("manage_prefabs", params, config)
        click.echo(format_output(result, config.format))
        if result.get("success"):
            print_success(f"Created prefab: {path}")
    except UnityConnectionError as e:
        print_error(str(e))
        sys.exit(1)
