"""Script CLI commands."""

import sys
import json
import click
from typing import Optional, Any

from cli.utils.config import get_config
from cli.utils.output import format_output, print_error, print_success
from cli.utils.connection import run_command, handle_unity_errors
from cli.utils.parsers import parse_json_list_or_exit
from cli.utils.confirmation import confirm_destructive_action


def _name_and_folder(path: str) -> tuple[str, str]:
    """Split 'Assets/Scripts/Player.cs' into ('Player', 'Assets/Scripts'), the form manage_script takes."""
    parts = path.replace("\\", "/").rsplit("/", 1)
    filename = parts[-1]
    directory = parts[0] if len(parts) > 1 else "Assets"
    return (filename[:-3] if filename.endswith(".cs") else filename), directory


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
@handle_unity_errors
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

    result = run_command("manage_script", params, config)
    click.echo(format_output(result, config.format))
    if result.get("success"):
        print_success(f"Created script: {name}.cs")


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
@handle_unity_errors
def read(path: str, start_line: Optional[int], line_count: Optional[int]):
    """Read a C# script file.

    \b
    Examples:
        unity-mcp script read "Assets/Scripts/Player.cs"
        unity-mcp script read "Assets/Scripts/Player.cs" --start-line 10 --line-count 20
    """
    config = get_config()

    name, directory = _name_and_folder(path)

    params: dict[str, Any] = {
        "action": "read",
        "name": name,
        "path": directory,
    }

    if start_line:
        params["startLine"] = start_line
    if line_count:
        params["lineCount"] = line_count

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


@script.command("delete")
@click.argument("path")
@click.option(
    "--force", "-f",
    is_flag=True,
    help="Skip confirmation prompt."
)
@handle_unity_errors
def delete(path: str, force: bool):
    """Delete a C# script.

    \b
    Examples:
        unity-mcp script delete "Assets/Scripts/OldScript.cs"
    """
    config = get_config()

    confirm_destructive_action("Delete", "script", path, force)

    name, directory = _name_and_folder(path)

    params: dict[str, Any] = {
        "action": "delete",
        "name": name,
        "path": directory,
    }

    result = run_command("manage_script", params, config)
    click.echo(format_output(result, config.format))
    if result.get("success"):
        print_success(f"Deleted: {path}")


@script.command("edit")
@click.argument("path")
@click.option(
    "--edits", "-e",
    required=True,
    help='Edits as JSON array of {startLine, startCol, endLine, endCol, newText}.'
)
@handle_unity_errors
def edit(path: str, edits: str):
    """Apply text edits to a script.

    \b
    Examples:
        unity-mcp script edit "Assets/Scripts/Player.cs" --edits '[{"startLine": 10, "startCol": 1, "endLine": 10, "endCol": 20, "newText": "// Modified"}]'
    """
    config = get_config()

    edits_list = parse_json_list_or_exit(edits, "edits")
    name, directory = _name_and_folder(path)

    # Unity refuses an edit that does not name the version of the file it changes.
    sha_result = run_command("manage_script", {"action": "get_sha", "name": name, "path": directory}, config)
    sha = (sha_result.get("result", sha_result).get("data") or {}).get("sha256")
    if not sha:
        click.echo(format_output(sha_result, config.format))
        sys.exit(1)

    result = run_command("manage_script", {
        "action": "apply_text_edits",
        "name": name,
        "path": directory,
        "edits": edits_list,
        "precondition_sha256": sha,
    }, config)
    click.echo(format_output(result, config.format))
    if result.get("result", result).get("success"):
        print_success(f"Applied edits to: {path}")


@script.command("validate")
@click.argument("path")
@click.option(
    "--level", "-l",
    type=click.Choice(["basic", "standard"]),
    default="basic",
    help="Validation level."
)
@handle_unity_errors
def validate(path: str, level: str):
    """Validate a C# script for errors.

    \b
    Examples:
        unity-mcp script validate "Assets/Scripts/Player.cs"
        unity-mcp script validate "Assets/Scripts/Player.cs" --level standard
    """
    config = get_config()

    name, directory = _name_and_folder(path)
    result = run_command("manage_script", {
        "action": "validate",
        "name": name,
        "path": directory,
        "level": level,
    }, config)
    click.echo(format_output(result, config.format))
