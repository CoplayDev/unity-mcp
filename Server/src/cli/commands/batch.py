"""Batch CLI commands for executing multiple Unity operations efficiently."""

import sys
import json
import click
from typing import Optional, Any

from cli.utils.config import get_config
from cli.utils.output import format_output, print_error, print_success, print_info
from cli.utils.connection import run_command, UnityConnectionError


@click.group()
def batch():
    """Batch operations - execute multiple commands efficiently."""
    pass


@batch.command("run")
@click.argument("file", type=click.Path(exists=True))
@click.option("--parallel", is_flag=True, help="Execute read-only commands in parallel.")
@click.option("--fail-fast", is_flag=True, help="Stop on first failure.")
def batch_run(file: str, parallel: bool, fail_fast: bool):
    """Execute commands from a JSON file.

    The JSON file should contain an array of command objects with 'tool' and 'params' keys.

    \\b
    File format:
        [
            {"tool": "manage_gameobject", "params": {"action": "create", "name": "Cube1"}},
            {"tool": "manage_gameobject", "params": {"action": "create", "name": "Cube2"}},
            {"tool": "manage_components", "params": {"action": "add", "target": "Cube1", "componentType": "Rigidbody"}}
        ]

    \\b
    Examples:
        unity-mcp batch run commands.json
        unity-mcp batch run setup.json --parallel
        unity-mcp batch run critical.json --fail-fast
    """
    config = get_config()

    try:
        with open(file, 'r') as f:
            commands = json.load(f)
    except json.JSONDecodeError as e:
        print_error(f"Invalid JSON in file: {e}")
        sys.exit(1)
    except IOError as e:
        print_error(f"Error reading file: {e}")
        sys.exit(1)

    if not isinstance(commands, list):
        print_error("JSON file must contain an array of commands")
        sys.exit(1)

    if len(commands) > 25:
        print_error(f"Maximum 25 commands per batch, got {len(commands)}")
        sys.exit(1)

    params: dict[str, Any] = {"commands": commands}
    if parallel:
        params["parallel"] = True
    if fail_fast:
        params["failFast"] = True

    click.echo(f"Executing {len(commands)} commands...")

    try:
        result = run_command("batch_execute", params, config)
        click.echo(format_output(result, config.format))

        if isinstance(result, dict):
            results = result.get("data", {}).get("results", [])
            succeeded = sum(1 for r in results if r.get("success"))
            failed = len(results) - succeeded

            if failed == 0:
                print_success(
                    f"All {succeeded} commands completed successfully")
            else:
                print_info(f"{succeeded} succeeded, {failed} failed")
    except UnityConnectionError as e:
        print_error(str(e))
        sys.exit(1)


@batch.command("inline")
@click.argument("commands_json")
@click.option("--parallel", is_flag=True, help="Execute read-only commands in parallel.")
@click.option("--fail-fast", is_flag=True, help="Stop on first failure.")
def batch_inline(commands_json: str, parallel: bool, fail_fast: bool):
    """Execute commands from inline JSON.

    \\b
    Examples:
        unity-mcp batch inline '[{"tool": "manage_scene", "params": {"action": "get_active"}}]'

        unity-mcp batch inline '[
            {"tool": "manage_gameobject", "params": {"action": "create", "name": "A", "primitiveType": "Cube"}},
            {"tool": "manage_gameobject", "params": {"action": "create", "name": "B", "primitiveType": "Sphere"}}
        ]'
    """
    config = get_config()

    try:
        commands = json.loads(commands_json)
    except json.JSONDecodeError as e:
        print_error(f"Invalid JSON: {e}")
        sys.exit(1)

    if not isinstance(commands, list):
        print_error("Commands must be an array")
        sys.exit(1)

    if len(commands) > 25:
        print_error(f"Maximum 25 commands per batch, got {len(commands)}")
        sys.exit(1)

    params: dict[str, Any] = {"commands": commands}
    if parallel:
        params["parallel"] = True
    if fail_fast:
        params["failFast"] = True

    try:
        result = run_command("batch_execute", params, config)
        click.echo(format_output(result, config.format))
    except UnityConnectionError as e:
        print_error(str(e))
        sys.exit(1)


@batch.command("template")
@click.option("--output", "-o", type=click.Path(), help="Output file (default: stdout)")
def batch_template(output: Optional[str]):
    """Generate a sample batch commands file.

    \\b
    Examples:
        unity-mcp batch template > commands.json
        unity-mcp batch template -o my_batch.json
    """
    template = [
        {
            "tool": "manage_scene",
            "params": {"action": "get_active"}
        },
        {
            "tool": "manage_gameobject",
            "params": {
                "action": "create",
                "name": "BatchCube",
                "primitiveType": "Cube",
                "position": [0, 1, 0]
            }
        },
        {
            "tool": "manage_components",
            "params": {
                "action": "add",
                "target": "BatchCube",
                "componentType": "Rigidbody"
            }
        },
        {
            "tool": "manage_gameobject",
            "params": {
                "action": "modify",
                "target": "BatchCube",
                "position": [0, 5, 0]
            }
        }
    ]

    json_output = json.dumps(template, indent=2)

    if output:
        with open(output, 'w') as f:
            f.write(json_output)
        print_success(f"Template written to: {output}")
    else:
        click.echo(json_output)
