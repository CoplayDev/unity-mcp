"""Prefab CLI commands."""

import sys
import click
from typing import Optional, Any

from cli.utils.config import get_config
from cli.utils.output import format_output, print_error, print_success
from cli.utils.connection import run_command, UnityConnectionError


@click.group()
def prefab():
    """Prefab operations - info, hierarchy, open, save, close, create prefabs."""
    pass


@prefab.command("open")
@click.argument("path")
def open_stage(path: str):
    """Open a prefab in the prefab stage for editing.

    \b
    Examples:
        unity-mcp prefab open "Assets/Prefabs/Player.prefab"
    """
    config = get_config()

    params: dict[str, Any] = {
        "action": "open_stage",
        "prefabPath": path,
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
@click.option(
    "--force", "-f",
    is_flag=True,
    help="Force save even if no changes detected. Useful for automated workflows."
)
def save_stage(force: bool):
    """Save the currently open prefab stage.

    \b
    Examples:
        unity-mcp prefab save
        unity-mcp prefab save --force
    """
    config = get_config()

    params: dict[str, Any] = {
        "action": "save_open_stage",
    }
    if force:
        params["force"] = True

    try:
        result = run_command("manage_prefabs", params, config)
        click.echo(format_output(result, config.format))
        if result.get("success"):
            print_success("Saved prefab")
    except UnityConnectionError as e:
        print_error(str(e))
        sys.exit(1)


@prefab.command("info")
@click.argument("path")
@click.option(
    "--compact", "-c",
    is_flag=True,
    help="Show compact output (key values only)."
)
def info(path: str, compact: bool):
    """Get information about a prefab asset.

    \b
    Examples:
        unity-mcp prefab info "Assets/Prefabs/Player.prefab"
        unity-mcp prefab info "Assets/Prefabs/UI.prefab" --compact
    """
    config = get_config()

    params: dict[str, Any] = {
        "action": "get_info",
        "prefabPath": path,
    }

    try:
        result = run_command("manage_prefabs", params, config)
        # Get the actual response data from the wrapped result structure
        response_data = result.get("result", result)
        if compact and response_data.get("success") and response_data.get("data"):
            data = response_data["data"]
            click.echo(f"Prefab: {data.get('assetPath', path)}")
            click.echo(f"  Type: {data.get('prefabType', 'Unknown')}")
            click.echo(f"  Root: {data.get('rootObjectName', 'N/A')}")
            click.echo(f"  GUID: {data.get('guid', 'N/A')}")
            click.echo(
                f"  Components: {len(data.get('rootComponentTypes', []))}")
            click.echo(f"  Children: {data.get('childCount', 0)}")
            if data.get('isVariant'):
                click.echo(f"  Variant of: {data.get('parentPrefab', 'N/A')}")
        else:
            click.echo(format_output(result, config.format))
    except UnityConnectionError as e:
        print_error(str(e))
        sys.exit(1)


@prefab.command("hierarchy")
@click.argument("path")
@click.option(
    "--compact", "-c",
    is_flag=True,
    help="Show compact output (names and paths only)."
)
@click.option(
    "--show-prefab-info", "-p",
    is_flag=True,
    help="Show prefab nesting information."
)
def hierarchy(path: str, compact: bool, show_prefab_info: bool):
    """Get the hierarchical structure of a prefab.

    \b
    Examples:
        unity-mcp prefab hierarchy "Assets/Prefabs/Player.prefab"
        unity-mcp prefab hierarchy "Assets/Prefabs/UI.prefab" --compact
        unity-mcp prefab hierarchy "Assets/Prefabs/Complex.prefab" --show-prefab-info
    """
    config = get_config()

    params: dict[str, Any] = {
        "action": "get_hierarchy",
        "prefabPath": path,
    }

    try:
        result = run_command("manage_prefabs", params, config)
        # Get the actual response data from the wrapped result structure
        response_data = result.get("result", result)
        if compact and response_data.get("success") and response_data.get("data"):
            data = response_data["data"]
            items = data.get("items", [])
            for item in items:
                indent = "  " * item.get("path", "").count("/")
                prefab_info = ""
                if show_prefab_info and item.get("prefab", {}).get("isNestedRoot"):
                    prefab_info = f" [nested: {item['prefab']['assetPath']}]"
                click.echo(f"{indent}{item.get('name')}{prefab_info}")
            click.echo(f"\nTotal: {data.get('total', 0)} objects")
        elif show_prefab_info:
            # Show prefab info in readable format
            if response_data.get("success") and response_data.get("data"):
                data = response_data["data"]
                items = data.get("items", [])
                for item in items:
                    prefab = item.get("prefab", {})
                    prefab_info = ""
                    if prefab.get("isRoot"):
                        prefab_info = " [root]"
                    elif prefab.get("isNestedRoot"):
                        prefab_info = f" [nested: {prefab.get('nestingDepth', 0)}]"
                    click.echo(f"{item.get('path')}{prefab_info}")
                click.echo(f"\nTotal: {data.get('total', 0)} objects")
            else:
                click.echo(format_output(result, config.format))
        else:
            click.echo(format_output(result, config.format))
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
@click.option(
    "--unlink-if-instance",
    is_flag=True,
    help="Unlink from existing prefab before creating new one."
)
def create(target: str, path: str, overwrite: bool, include_inactive: bool, unlink_if_instance: bool):
    """Create a prefab from a scene GameObject.

    \b
    Examples:
        unity-mcp prefab create "Player" "Assets/Prefabs/Player.prefab"
        unity-mcp prefab create "Enemy" "Assets/Prefabs/Enemy.prefab" --overwrite
        unity-mcp prefab create "EnemyInstance" "Assets/Prefabs/BossEnemy.prefab" --unlink-if-instance
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
    if unlink_if_instance:
        params["unlinkIfInstance"] = True

    try:
        result = run_command("manage_prefabs", params, config)
        click.echo(format_output(result, config.format))
        if result.get("success"):
            print_success(f"Created prefab: {path}")
    except UnityConnectionError as e:
        print_error(str(e))
        sys.exit(1)
