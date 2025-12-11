"""Shared helper utilities for MCP server tools."""

from __future__ import annotations

import json
from typing import Any

_TRUTHY = {"true", "1", "yes", "on"}
_FALSY = {"false", "0", "no", "off"}

def coerce_bool(value: Any, default: bool | None = None) -> bool | None:
    """Attempt to coerce a loosely-typed value to a boolean."""
    if value is None:
        return default
    if isinstance(value, bool):
        return value
    if isinstance(value, str):
        lowered = value.strip().lower()
        if lowered in _TRUTHY:
            return True
        if lowered in _FALSY:
            return False
        return default
    return bool(value)


def parse_json_payload(value: Any) -> Any:
    """
    Attempt to parse a value that might be a JSON string into its native object.
    
    This is a tolerant parser used to handle cases where MCP clients or LLMs
    serialize complex objects (lists, dicts) into strings. It also handles
    scalar values like numbers, booleans, and null.
    
    Args:
        value: The input value (can be str, list, dict, etc.)
        
    Returns:
        The parsed JSON object/list if the input was a valid JSON string,
        or the original value if parsing failed or wasn't necessary.
    """
    if not isinstance(value, str):
        return value
        
    val_trimmed = value.strip()
    
    # Fast path: if it doesn't look like JSON structure, return as is
    if not (
        (val_trimmed.startswith("{") and val_trimmed.endswith("}")) or 
        (val_trimmed.startswith("[") and val_trimmed.endswith("]")) or
        val_trimmed in ("true", "false", "null") or
        (val_trimmed.replace(".", "", 1).replace("-", "", 1).isdigit())
    ):
        return value

    try:
        return json.loads(value)
    except (json.JSONDecodeError, ValueError):
        # If parsing fails, assume it was meant to be a literal string
        return value
