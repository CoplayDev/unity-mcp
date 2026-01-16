"""Code CLI commands - read source code. search might be implemented later (but can be totally achievable with AI)."""

import sys
import os
import click
from typing import Optional, Any

from cli.utils.config import get_config
from cli.utils.output import format_output, print_error, print_info
from cli.utils.connection import run_command, UnityConnectionError


@click.group()
def code():
    """Code operations - read source files."""
    pass


@code.command("read")
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
    """Read a source file.
    
    \b
    Examples:
        unity-mcp code read "Assets/Scripts/Player.cs"
        unity-mcp code read "Assets/Scripts/Player.cs" --start-line 10 --line-count 20
    """
    config = get_config()
    
    # Extract name and directory from path
    parts = path.replace("\\", "/").split("/")
    filename = os.path.splitext(parts[-1])[0]
    directory = "/".join(parts[:-1]) or "Assets"
    
    params: dict[str, Any] = {
        "action": "read",
        "name": filename,
        "path": directory,
    }
    
    if start_line:
        params["startLine"] = start_line
    if line_count:
        params["lineCount"] = line_count
    
    try:
        result = run_command("manage_script", params, config)
        # For read, output content directly if available
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
