"""Code CLI commands - read source code. search might be implemented later (but can be totally achievable with AI)."""

import sys
import os
import click
from typing import Optional, Any

from cli.utils.config import get_config
from cli.utils.output import format_output, print_error, print_info
from cli.utils.connection import run_command, handle_unity_errors


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
@handle_unity_errors
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


@code.command("search")
@click.argument("pattern")
@click.argument("path")
@click.option(
    "--max-results", "-n",
    default=50,
    type=int,
    help="Maximum number of results (default: 50)."
)
@click.option(
    "--case-sensitive", "-c",
    is_flag=True,
    help="Make search case-sensitive (default: case-insensitive)."
)
@handle_unity_errors
def search(pattern: str, path: str, max_results: int, case_sensitive: bool):
    """Search for patterns in Unity scripts using regex.

    PATTERN is a regex pattern to search for.
    PATH is the script path (e.g., Assets/Scripts/Player.cs).

    \\b
    Examples:
        unity-mcp code search "class.*Player" "Assets/Scripts/Player.cs"
        unity-mcp code search "private.*int" "Assets/Scripts/GameManager.cs"
        unity-mcp code search "TODO|FIXME" "Assets/Scripts/Utils.cs"
    """
    import re
    import base64

    config = get_config()

    # Extract name and directory from path
    parts = path.replace("\\", "/").split("/")
    filename = os.path.splitext(parts[-1])[0]
    directory = "/".join(parts[:-1]) or "Assets"

    # Step 1: Read the file via Unity's manage_script
    read_params: dict[str, Any] = {
        "action": "read",
        "name": filename,
        "path": directory,
    }

    result = run_command("manage_script", read_params, config)

    # Handle nested response structure: {status, result: {success, data}}
    inner_result = result.get("result", result)

    if not inner_result.get("success") and result.get("status") != "success":
        click.echo(format_output(result, config.format))
        return

    # Get file contents from nested data
    data = inner_result.get("data", {})
    contents = data.get("contents")

    # Handle base64 encoded content
    if not contents and data.get("contentsEncoded") and data.get("encodedContents"):
        try:
            contents = base64.b64decode(
                data["encodedContents"]).decode("utf-8", "replace")
        except (ValueError, TypeError):
            pass

    if not contents:
        print_error(f"Could not read file content from {path}")
        sys.exit(1)

    # Step 2: Perform regex search locally
    flags = re.MULTILINE
    if not case_sensitive:
        flags |= re.IGNORECASE

    try:
        regex = re.compile(pattern, flags)
    except re.error as e:
        print_error(f"Invalid regex pattern: {e}")
        sys.exit(1)

    found = list(regex.finditer(contents))

    if not found:
        print_info(f"No matches found for pattern: {pattern}")
        return

    results = []
    for m in found[:max_results]:
        start_idx = m.start()

        # Calculate line number
        line_num = contents.count('\n', 0, start_idx) + 1

        # Get line content
        line_start = contents.rfind('\n', 0, start_idx) + 1
        line_end = contents.find('\n', start_idx)
        if line_end == -1:
            line_end = len(contents)

        line_content = contents[line_start:line_end].strip()

        results.append({
            "line": line_num,
            "content": line_content,
            "match": m.group(0),
        })

    # Display results
    click.echo(f"Found {len(results)} matches (total: {len(found)}):\n")
    for match in results:
        click.echo(f"  Line {match['line']}: {match['content']}")
