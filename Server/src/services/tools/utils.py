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


def coerce_int(value: Any, default: int | None = None) -> int | None:
    """Attempt to coerce a loosely-typed value to an integer."""
    if value is None:
        return default
    try:
        if isinstance(value, bool):
            return default
        if isinstance(value, int):
            return value
        s = str(value).strip()
        if s.lower() in ("", "none", "null"):
            return default
        return int(float(s))
    except Exception:
        return default


def coerce_float(value: Any, default: float | None = None) -> float | None:
    """Attempt to coerce a loosely-typed value to a float-like number."""
    if value is None:
        return default
    try:
        # Treat booleans as invalid numeric input instead of coercing to 0/1.
        if isinstance(value, bool):
            return default
        if isinstance(value, (int, float)):
            return float(value)
        s = str(value).strip()
        if s.lower() in ("", "none", "null"):
            return default
        return float(s)
    except (TypeError, ValueError):
        return default


def normalize_properties(value: Any) -> tuple[dict[str, Any] | None, str | None]:
    """
    Robustly normalize a properties parameter to a dict.

    Handles various input formats from MCP clients/LLMs:
    - None -> (None, None)
    - dict -> (dict, None)
    - JSON string -> (parsed_dict, None) or (None, error_message)
    - Invalid values -> (None, error_message)

    Returns:
        Tuple of (parsed_dict, error_message). If error_message is set, parsed_dict is None.
    """
    if value is None:
        return None, None

    # Already a dict - return as-is
    if isinstance(value, dict):
        return value, None

    # Try parsing as string
    if isinstance(value, str):
        # Check for obviously invalid values from serialization bugs
        if value in ("[object Object]", "undefined", "null", ""):
            return None, f"properties received invalid value: '{value}'. Expected a JSON object like {{\"key\": value}}"

        parsed = parse_json_payload(value)
        if isinstance(parsed, dict):
            return parsed, None

        return None, f"properties must be a JSON object (dict), got string that parsed to {type(parsed).__name__}"

    return None, f"properties must be a dict or JSON string, got {type(value).__name__}"
