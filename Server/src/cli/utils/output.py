"""Output formatting utilities for CLI."""

import json
from typing import Any

import click


def format_output(data: Any, format_type: str = "text") -> str:
    """Format output based on requested format type.

    Args:
        data: Data to format
        format_type: One of 'text', 'json', 'table'

    Returns:
        Formatted string
    """
    if format_type == "json":
        return format_as_json(data)
    elif format_type == "table":
        return format_as_table(data)
    else:
        return format_as_text(data)


def format_as_json(data: Any) -> str:
    """Format data as pretty-printed JSON."""
    try:
        return json.dumps(data, indent=2, default=str)
    except (TypeError, ValueError) as e:
        return json.dumps({"error": f"JSON serialization failed: {e}", "raw": str(data)})


def format_as_text(data: Any, indent: int = 0) -> str:
    """Format data as human-readable text."""
    prefix = "  " * indent

    if data is None:
        return f"{prefix}(none)"

    if isinstance(data, dict):
        # Check for error response
        if "success" in data and not data.get("success"):
            error = data.get("error") or data.get("message") or "Unknown error"
            return f"{prefix}❌ Error: {error}"

        # Check for success response with data
        if "success" in data and data.get("success"):
            result = data.get("data") or data.get("result") or data
            if result != data:
                return format_as_text(result, indent)

        lines = []
        for key, value in data.items():
            if key in ("success", "error", "message") and "success" in data:
                continue  # Skip meta fields
            if isinstance(value, dict):
                lines.append(f"{prefix}{key}:")
                lines.append(format_as_text(value, indent + 1))
            elif isinstance(value, list):
                lines.append(f"{prefix}{key}: [{len(value)} items]")
                if len(value) <= 10:
                    for i, item in enumerate(value):
                        lines.append(
                            f"{prefix}  [{i}] {_format_list_item(item)}")
                else:
                    for i, item in enumerate(value[:5]):
                        lines.append(
                            f"{prefix}  [{i}] {_format_list_item(item)}")
                    lines.append(f"{prefix}  ... ({len(value) - 10} more)")
                    for i, item in enumerate(value[-5:], len(value) - 5):
                        lines.append(
                            f"{prefix}  [{i}] {_format_list_item(item)}")
            else:
                lines.append(f"{prefix}{key}: {value}")
        return "\n".join(lines)

    if isinstance(data, list):
        if not data:
            return f"{prefix}(empty list)"
        lines = [f"{prefix}[{len(data)} items]"]
        for i, item in enumerate(data[:20]):
            lines.append(f"{prefix}  [{i}] {_format_list_item(item)}")
        if len(data) > 20:
            lines.append(f"{prefix}  ... ({len(data) - 20} more)")
        return "\n".join(lines)

    return f"{prefix}{data}"


def _format_list_item(item: Any) -> str:
    """Format a single list item."""
    if isinstance(item, dict):
        # Try to find a name/id field for display
        name = item.get("name") or item.get(
            "Name") or item.get("id") or item.get("Id")
        if name:
            extra = ""
            if "instanceID" in item:
                extra = f" (ID: {item['instanceID']})"
            elif "path" in item:
                extra = f" ({item['path']})"
            return f"{name}{extra}"
        # Fallback to compact representation
        return json.dumps(item, default=str)[:80]
    return str(item)[:80]


def format_as_table(data: Any) -> str:
    """Format data as an ASCII table."""
    if isinstance(data, dict):
        # Check for success response with data
        if "success" in data and data.get("success"):
            result = data.get("data") or data.get(
                "result") or data.get("items")
            if isinstance(result, list):
                return _build_table(result)

        # Single dict as key-value table
        rows = [[str(k), str(v)[:60]] for k, v in data.items()]
        return _build_table(rows, headers=["Key", "Value"])

    if isinstance(data, list):
        return _build_table(data)

    return str(data)


def _build_table(data: list[Any], headers: list[str] | None = None) -> str:
    """Build an ASCII table from list data."""
    if not data:
        return "(no data)"

    # Convert list of dicts to rows
    if isinstance(data[0], dict):
        if headers is None:
            headers = list(data[0].keys())
        rows = [[str(item.get(h, ""))[:40] for h in headers] for item in data]
    elif isinstance(data[0], (list, tuple)):
        rows = [[str(cell)[:40] for cell in row] for row in data]
        if headers is None:
            headers = [f"Col{i}" for i in range(len(data[0]))]
    else:
        rows = [[str(item)[:60]] for item in data]
        headers = headers or ["Value"]

    # Calculate column widths
    col_widths = [len(h) for h in headers]
    for row in rows:
        for i, cell in enumerate(row):
            if i < len(col_widths):
                col_widths[i] = max(col_widths[i], len(cell))

    # Build table
    lines = []

    # Header
    header_line = " | ".join(
        h.ljust(col_widths[i]) for i, h in enumerate(headers))
    lines.append(header_line)
    lines.append("-+-".join("-" * w for w in col_widths))

    # Rows
    for row in rows[:50]:  # Limit rows
        row_line = " | ".join(
            (row[i] if i < len(row) else "").ljust(col_widths[i])
            for i in range(len(headers))
        )
        lines.append(row_line)

    if len(rows) > 50:
        lines.append(f"... ({len(rows) - 50} more rows)")

    return "\n".join(lines)


def print_success(message: str) -> None:
    """Print a success message."""
    click.echo(f"✅ {message}")


def print_error(message: str) -> None:
    """Print an error message to stderr."""
    click.echo(f"❌ {message}", err=True)


def print_warning(message: str) -> None:
    """Print a warning message."""
    click.echo(f"⚠️  {message}")


def print_info(message: str) -> None:
    """Print an info message."""
    click.echo(f"ℹ️  {message}")
