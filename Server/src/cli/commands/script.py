"""Script CLI commands."""

import sys
import json
import click
from typing import Optional, Any

from cli.utils.config import get_config
from cli.utils.output import format_output, print_error, print_success
from cli.utils.connection import run_command, UnityConnectionError


@click.group()
def script():
    """Script operations - create, read, edit C# scripts."""
    pass


@script.command("create")
@click.argument("name")
@click.option(
    "--path", "-p",
    default="Assets/Scripts",
    help="Directory to create the script in."
)
@click.option(
    "--type", "-t",
    "script_type",
    type=click.Choice(["MonoBehaviour", "ScriptableObject",
                      "Editor", "EditorWindow", "Plain"]),
    default="MonoBehaviour",
    help="Type of script to create."
)
@click.option(
    "--namespace", "-n",
    default=None,
    help="Namespace for the script."
)
@click.option(
    "--contents", "-c",
    default=None,
    help="Full script contents (overrides template)."
)
def create(name: str, path: str, script_type: str, namespace: Optional[str], contents: Optional[str]):
    """Create a new C# script.

    \b
    Examples:
        unity-mcp script create "PlayerController"
        unity-mcp script create "GameManager" --path "Assets/Scripts/Managers"
        unity-mcp script create "EnemyData" --type ScriptableObject
        unity-mcp script create "CustomEditor" --type Editor --namespace "MyGame.Editor"
    """
    config = get_config()

    params: dict[str, Any] = {
        "action": "create",
        "name": name,
        "path": path,
        "scriptType": script_type,
    }

    if namespace:
        params["namespace"] = namespace
    if contents:
        params["contents"] = contents

    try:
        result = run_command("manage_script", params, config)
        click.echo(format_output(result, config.format))
        if result.get("success"):
            print_success(f"Created script: {name}.cs")
    except UnityConnectionError as e:
        print_error(str(e))
        sys.exit(1)


@script.command("read")
@click.argument("path")
@click.option(
    "--start-line", "-s",
    default=None,
    type=int,
    help="Starting line number (1-based)."
)
@click.option(
    "--line-count", "-n",
    default=None,
    type=int,
    help="Number of lines to read."
)
def read(path: str, start_line: Optional[int], line_count: Optional[int]):
    """Read a C# script file.

    \b
    Examples:
        unity-mcp script read "Assets/Scripts/Player.cs"
        unity-mcp script read "Assets/Scripts/Player.cs" --start-line 10 --line-count 20
    """
    config = get_config()

    parts = path.rsplit("/", 1)
    filename = parts[-1]
    directory = parts[0] if len(parts) > 1 else "Assets"
    name = filename[:-3] if filename.endswith(".cs") else filename

    params: dict[str, Any] = {
        "action": "read",
        "name": name,
        "path": directory,
    }

    if start_line:
        params["startLine"] = start_line
    if line_count:
        params["lineCount"] = line_count

    try:
        result = run_command("manage_script", params, config)
        # For read, just output the content directly
        if result.get("success") and result.get("data"):
            data = result.get("data", {})
            if isinstance(data, dict) and "contents" in data:
                click.echo(data["contents"])
            else:
                click.echo(format_output(result, config.format))
        else:
            click.echo(format_output(result, config.format))
    except UnityConnectionError as e:
        print_error(str(e))
        sys.exit(1)


@script.command("delete")
@click.argument("path")
@click.option(
    "--force", "-f",
    is_flag=True,
    help="Skip confirmation prompt."
)
def delete(path: str, force: bool):
    """Delete a C# script.

    \b
    Examples:
        unity-mcp script delete "Assets/Scripts/OldScript.cs"
    """
    config = get_config()

    if not force:
        click.confirm(f"Delete script '{path}'?", abort=True)

    parts = path.rsplit("/", 1)
    filename = parts[-1]
    directory = parts[0] if len(parts) > 1 else "Assets"
    name = filename[:-3] if filename.endswith(".cs") else filename

    params: dict[str, Any] = {
        "action": "delete",
        "name": name,
        "path": directory,
    }

    try:
        result = run_command("manage_script", params, config)
        click.echo(format_output(result, config.format))
        if result.get("success"):
            print_success(f"Deleted: {path}")
    except UnityConnectionError as e:
        print_error(str(e))
        sys.exit(1)


@script.command("edit")
@click.argument("path")
@click.option(
    "--edits", "-e",
    required=True,
    help='Edits as JSON array of {startLine, startCol, endLine, endCol, newText}.'
)
def edit(path: str, edits: str):
    """Apply text edits to a script.

    \b
    Examples:
        unity-mcp script edit "Assets/Scripts/Player.cs" --edits '[{"startLine": 10, "startCol": 1, "endLine": 10, "endCol": 20, "newText": "// Modified"}]'
    """
    config = get_config()

    try:
        edits_list = json.loads(edits)
    except json.JSONDecodeError as e:
        print_error(f"Invalid JSON for edits: {e}")
        sys.exit(1)

    params: dict[str, Any] = {
        "uri": path,
        "edits": edits_list,
    }

    try:
        result = run_command("apply_text_edits", params, config)
        click.echo(format_output(result, config.format))
        if result.get("success"):
            print_success(f"Applied edits to: {path}")
    except UnityConnectionError as e:
        print_error(str(e))
        sys.exit(1)


@script.command("validate")
@click.argument("path")
@click.option(
    "--level", "-l",
    type=click.Choice(["basic", "standard"]),
    default="basic",
    help="Validation level."
)
def validate(path: str, level: str):
    """Validate a C# script for errors.

    \b
    Examples:
        unity-mcp script validate "Assets/Scripts/Player.cs"
        unity-mcp script validate "Assets/Scripts/Player.cs" --level standard
    """
    config = get_config()

    params: dict[str, Any] = {
        "uri": path,
        "level": level,
        "include_diagnostics": True,
    }

    try:
        result = run_command("validate_script", params, config)
        click.echo(format_output(result, config.format))
    except UnityConnectionError as e:
        print_error(str(e))
        sys.exit(1)
