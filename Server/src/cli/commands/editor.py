"""Editor CLI commands."""

import sys
import click
from typing import Optional, Any

from cli.utils.config import get_config
from cli.utils.output import format_output, print_error, print_success, print_info
from cli.utils.connection import run_command, UnityConnectionError


@click.group()
def editor():
    """Editor operations - play mode, console, tags, layers."""
    pass


@editor.command("play")
def play():
    """Enter play mode."""
    config = get_config()
    
    try:
        result = run_command("manage_editor", {"action": "play"}, config)
        click.echo(format_output(result, config.format))
        if result.get("success"):
            print_success("Entered play mode")
    except UnityConnectionError as e:
        print_error(str(e))
        sys.exit(1)


@editor.command("pause")
def pause():
    """Pause play mode."""
    config = get_config()
    
    try:
        result = run_command("manage_editor", {"action": "pause"}, config)
        click.echo(format_output(result, config.format))
        if result.get("success"):
            print_success("Paused play mode")
    except UnityConnectionError as e:
        print_error(str(e))
        sys.exit(1)


@editor.command("stop")
def stop():
    """Stop play mode."""
    config = get_config()
    
    try:
        result = run_command("manage_editor", {"action": "stop"}, config)
        click.echo(format_output(result, config.format))
        if result.get("success"):
            print_success("Stopped play mode")
    except UnityConnectionError as e:
        print_error(str(e))
        sys.exit(1)


@editor.command("console")
@click.option(
    "--type", "-t",
    "log_types",
    multiple=True,
    type=click.Choice(["error", "warning", "log", "all"]),
    default=["error", "warning", "log"],
    help="Message types to retrieve."
)
@click.option(
    "--count", "-n",
    default=10,
    type=int,
    help="Number of messages to retrieve."
)
@click.option(
    "--filter", "-f",
    "filter_text",
    default=None,
    help="Filter messages containing this text."
)
@click.option(
    "--stacktrace", "-s",
    is_flag=True,
    help="Include stack traces."
)
@click.option(
    "--clear",
    is_flag=True,
    help="Clear the console instead of reading."
)
def console(log_types: tuple, count: int, filter_text: Optional[str], stacktrace: bool, clear: bool):
    """Read or clear the Unity console.
    
    \b
    Examples:
        unity-mcp editor console
        unity-mcp editor console --type error --count 20
        unity-mcp editor console --filter "NullReference" --stacktrace
        unity-mcp editor console --clear
    """
    config = get_config()
    
    if clear:
        try:
            result = run_command("read_console", {"action": "clear"}, config)
            click.echo(format_output(result, config.format))
            if result.get("success"):
                print_success("Console cleared")
        except UnityConnectionError as e:
            print_error(str(e))
            sys.exit(1)
        return
    
    params: dict[str, Any] = {
        "action": "get",
        "types": list(log_types),
        "count": count,
        "include_stacktrace": stacktrace,
    }
    
    if filter_text:
        params["filter_text"] = filter_text
    
    try:
        result = run_command("read_console", params, config)
        click.echo(format_output(result, config.format))
    except UnityConnectionError as e:
        print_error(str(e))
        sys.exit(1)


@editor.command("add-tag")
@click.argument("tag_name")
def add_tag(tag_name: str):
    """Add a new tag.
    
    \b
    Examples:
        unity-mcp editor add-tag "Enemy"
        unity-mcp editor add-tag "Collectible"
    """
    config = get_config()
    
    try:
        result = run_command("manage_editor", {"action": "add_tag", "tagName": tag_name}, config)
        click.echo(format_output(result, config.format))
        if result.get("success"):
            print_success(f"Added tag: {tag_name}")
    except UnityConnectionError as e:
        print_error(str(e))
        sys.exit(1)


@editor.command("remove-tag")
@click.argument("tag_name")
def remove_tag(tag_name: str):
    """Remove a tag.
    
    \b
    Examples:
        unity-mcp editor remove-tag "OldTag"
    """
    config = get_config()
    
    try:
        result = run_command("manage_editor", {"action": "remove_tag", "tagName": tag_name}, config)
        click.echo(format_output(result, config.format))
        if result.get("success"):
            print_success(f"Removed tag: {tag_name}")
    except UnityConnectionError as e:
        print_error(str(e))
        sys.exit(1)


@editor.command("add-layer")
@click.argument("layer_name")
def add_layer(layer_name: str):
    """Add a new layer.
    
    \b
    Examples:
        unity-mcp editor add-layer "Interactable"
    """
    config = get_config()
    
    try:
        result = run_command("manage_editor", {"action": "add_layer", "layerName": layer_name}, config)
        click.echo(format_output(result, config.format))
        if result.get("success"):
            print_success(f"Added layer: {layer_name}")
    except UnityConnectionError as e:
        print_error(str(e))
        sys.exit(1)


@editor.command("remove-layer")
@click.argument("layer_name")
def remove_layer(layer_name: str):
    """Remove a layer.
    
    \b
    Examples:
        unity-mcp editor remove-layer "OldLayer"
    """
    config = get_config()
    
    try:
        result = run_command("manage_editor", {"action": "remove_layer", "layerName": layer_name}, config)
        click.echo(format_output(result, config.format))
        if result.get("success"):
            print_success(f"Removed layer: {layer_name}")
    except UnityConnectionError as e:
        print_error(str(e))
        sys.exit(1)


@editor.command("tool")
@click.argument("tool_name")
def set_tool(tool_name: str):
    """Set the active editor tool.
    
    \b
    Examples:
        unity-mcp editor tool "Move"
        unity-mcp editor tool "Rotate"
        unity-mcp editor tool "Scale"
    """
    config = get_config()
    
    try:
        result = run_command("manage_editor", {"action": "set_active_tool", "toolName": tool_name}, config)
        click.echo(format_output(result, config.format))
        if result.get("success"):
            print_success(f"Set active tool: {tool_name}")
    except UnityConnectionError as e:
        print_error(str(e))
        sys.exit(1)


@editor.command("menu")
@click.argument("menu_path")
def execute_menu(menu_path: str):
    """Execute a menu item.
    
    \b
    Examples:
        unity-mcp editor menu "File/Save"
        unity-mcp editor menu "Edit/Undo"
        unity-mcp editor menu "GameObject/Create Empty"
    """
    config = get_config()
    
    try:
        result = run_command("execute_menu_item", {"menu_path": menu_path}, config)
        click.echo(format_output(result, config.format))
        if result.get("success"):
            print_success(f"Executed: {menu_path}")
    except UnityConnectionError as e:
        print_error(str(e))
        sys.exit(1)


@editor.command("tests")
@click.option(
    "--mode", "-m",
    type=click.Choice(["EditMode", "PlayMode"]),
    default="EditMode",
    help="Test mode to run."
)
@click.option(
    "--timeout", "-t",
    default=None,
    type=int,
    help="Timeout in seconds."
)
def run_tests(mode: str, timeout: Optional[int]):
    """Run Unity tests.
    
    \b
    Examples:
        unity-mcp editor tests
        unity-mcp editor tests --mode PlayMode
        unity-mcp editor tests --timeout 60
    """
    config = get_config()
    
    params: dict[str, Any] = {"mode": mode}
    if timeout:
        params["timeout_seconds"] = timeout
    
    try:
        result = run_command("run_tests", params, config)
        click.echo(format_output(result, config.format))
    except UnityConnectionError as e:
        print_error(str(e))
        sys.exit(1)
