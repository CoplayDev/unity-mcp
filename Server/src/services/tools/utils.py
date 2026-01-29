"""Shared helper utilities for MCP server tools."""

from __future__ import annotations

import json
import math
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


def normalize_vector3(value: Any, param_name: str = "vector") -> tuple[list[float] | None, str | None]:
    """
    Normalize a vector parameter to [x, y, z] format.

    Handles various input formats from MCP clients/LLMs:
    - None -> (None, None)
    - list/tuple [x, y, z] -> ([x, y, z], None)
    - dict {x, y, z} -> ([x, y, z], None)
    - JSON string "[x, y, z]" or "{x, y, z}" -> parsed and normalized
    - comma-separated string "x, y, z" -> ([x, y, z], None)

    Returns:
        Tuple of (parsed_vector, error_message). If error_message is set, parsed_vector is None.
    """
    if value is None:
        return None, None

    # Handle dict with x/y/z keys (e.g., {"x": 0, "y": 1, "z": 2})
    if isinstance(value, dict):
        if all(k in value for k in ("x", "y", "z")):
            try:
                vec = [float(value["x"]), float(value["y"]), float(value["z"])]
                if all(math.isfinite(n) for n in vec):
                    return vec, None
                return None, f"{param_name} values must be finite numbers, got {value}"
            except (ValueError, TypeError, KeyError):
                return None, f"{param_name} dict values must be numbers, got {value}"
        return None, f"{param_name} dict must have 'x', 'y', 'z' keys, got {list(value.keys())}"

    # If already a list/tuple with 3 elements, convert to floats
    if isinstance(value, (list, tuple)) and len(value) == 3:
        try:
            vec = [float(value[0]), float(value[1]), float(value[2])]
            if all(math.isfinite(n) for n in vec):
                return vec, None
            return None, f"{param_name} values must be finite numbers, got {value}"
        except (ValueError, TypeError):
            return None, f"{param_name} values must be numbers, got {value}"

    # Try parsing as string
    if isinstance(value, str):
        # Check for obviously invalid values
        if value in ("[object Object]", "undefined", "null", ""):
            return None, f"{param_name} received invalid value: '{value}'. Expected [x, y, z] array or {{x, y, z}} object"

        parsed = parse_json_payload(value)

        # Handle parsed dict
        if isinstance(parsed, dict):
            return normalize_vector3(parsed, param_name)

        # Handle parsed list
        if isinstance(parsed, list) and len(parsed) == 3:
            try:
                vec = [float(parsed[0]), float(parsed[1]), float(parsed[2])]
                if all(math.isfinite(n) for n in vec):
                    return vec, None
                return None, f"{param_name} values must be finite numbers, got {parsed}"
            except (ValueError, TypeError):
                return None, f"{param_name} values must be numbers, got {parsed}"

        # Handle comma-separated strings "1,2,3", "[1,2,3]", or "(1,2,3)"
        s = value.strip()
        if (s.startswith("[") and s.endswith("]")) or (s.startswith("(") and s.endswith(")")):
            s = s[1:-1]
        parts = [p.strip() for p in (s.split(",") if "," in s else s.split())]
        if len(parts) == 3:
            try:
                vec = [float(parts[0]), float(parts[1]), float(parts[2])]
                if all(math.isfinite(n) for n in vec):
                    return vec, None
                return None, f"{param_name} values must be finite numbers, got {value}"
            except (ValueError, TypeError):
                return None, f"{param_name} values must be numbers, got {value}"

        return None, f"{param_name} must be a [x, y, z] array or {{x, y, z}} object, got: {value}"

    return None, f"{param_name} must be a list, dict, or string, got {type(value).__name__}"


def normalize_color(value: Any, output_range: str = "float") -> tuple[list[float] | None, str | None]:
    """
    Normalize a color parameter to [r, g, b, a] format.

    Handles various input formats from MCP clients/LLMs:
    - None -> (None, None)
    - list/tuple [r, g, b] or [r, g, b, a] -> normalized with optional alpha
    - dict {r, g, b} or {r, g, b, a} -> converted to list
    - hex string "#RGB", "#RRGGBB", "#RRGGBBAA" -> parsed to [r, g, b, a]
    - JSON string -> parsed and normalized

    Args:
        value: The color value to normalize
        output_range: "float" for 0.0-1.0 range, "int" for 0-255 range

    Returns:
        Tuple of (parsed_color, error_message). If error_message is set, parsed_color is None.
    """
    if value is None:
        return None, None

    def _to_output_range(components: list[float], from_hex: bool = False) -> list:
        """Convert color components to the requested output range."""
        if output_range == "int":
            if from_hex:
                # Already 0-255 from hex parsing
                return [int(c) for c in components]
            # Check if input is normalized (0-1) or already 0-255
            if all(0 <= c <= 1 for c in components):
                return [int(round(c * 255)) for c in components]
            return [int(c) for c in components]
        else:  # float
            if from_hex:
                # Convert 0-255 to 0-1
                return [c / 255.0 for c in components]
            if any(c > 1 for c in components):
                return [c / 255.0 for c in components]
            return [float(c) for c in components]

    # Handle dict with r/g/b keys
    if isinstance(value, dict):
        if all(k in value for k in ("r", "g", "b")):
            try:
                color = [float(value["r"]), float(value["g"]), float(value["b"])]
                if "a" in value:
                    color.append(float(value["a"]))
                else:
                    if output_range == "int" and all(0 <= c <= 1 for c in color):
                        color.append(1.0)
                    else:
                        color.append(1.0 if output_range == "float" else 255)
                return _to_output_range(color), None
            except (ValueError, TypeError, KeyError):
                return None, f"color dict values must be numbers, got {value}"
        return None, f"color dict must have 'r', 'g', 'b' keys, got {list(value.keys())}"

    # Already a list/tuple - validate
    if isinstance(value, (list, tuple)):
        if len(value) in (3, 4):
            try:
                color = [float(c) for c in value]
                if len(color) == 3:
                    if output_range == "int" and all(0 <= c <= 1 for c in color):
                        color.append(1.0)
                    else:
                        color.append(1.0 if output_range == "float" else 255)
                return _to_output_range(color), None
            except (ValueError, TypeError):
                return None, f"color values must be numbers, got {value}"
        return None, f"color must have 3 or 4 components, got {len(value)}"

    # Try parsing as string
    if isinstance(value, str):
        if value in ("[object Object]", "undefined", "null", ""):
            return None, f"color received invalid value: '{value}'. Expected [r, g, b, a] or {{r, g, b, a}}"

        # Handle hex colors
        if value.startswith("#"):
            h = value.lstrip("#")
            try:
                if len(h) == 3:
                    # Short form #RGB -> expand to #RRGGBB
                    components = [int(c + c, 16) for c in h] + [255]
                    return _to_output_range(components, from_hex=True), None
                elif len(h) == 6:
                    components = [int(h[i:i+2], 16) for i in (0, 2, 4)] + [255]
                    return _to_output_range(components, from_hex=True), None
                elif len(h) == 8:
                    components = [int(h[i:i+2], 16) for i in (0, 2, 4, 6)]
                    return _to_output_range(components, from_hex=True), None
            except ValueError:
                return None, f"Invalid hex color: {value}"
            return None, f"Invalid hex color length: {value}"

        # Try parsing as JSON
        parsed = parse_json_payload(value)

        # Handle parsed dict
        if isinstance(parsed, dict):
            return normalize_color(parsed, output_range)

        # Handle parsed list
        if isinstance(parsed, (list, tuple)) and len(parsed) in (3, 4):
            try:
                color = [float(c) for c in parsed]
                if len(color) == 3:
                    if output_range == "int" and all(0 <= c <= 1 for c in color):
                        color.append(1.0)
                    else:
                        color.append(1.0 if output_range == "float" else 255)
                return _to_output_range(color), None
            except (ValueError, TypeError):
                return None, f"color values must be numbers, got {parsed}"

        # Handle tuple-style strings "(r, g, b)" or "(r, g, b, a)"
        s = value.strip()
        if (s.startswith("[") and s.endswith("]")) or (s.startswith("(") and s.endswith(")")):
            s = s[1:-1]
        parts = [p.strip() for p in s.split(",")]
        if len(parts) in (3, 4):
            try:
                color = [float(p) for p in parts]
                if len(color) == 3:
                    if output_range == "int" and all(0 <= c <= 1 for c in color):
                        color.append(1.0)
                    else:
                        color.append(1.0 if output_range == "float" else 255)
                return _to_output_range(color), None
            except (ValueError, TypeError):
                pass  # Fall through to error message

        return None, f"Failed to parse color string: {value}"

    return None, f"color must be a list, dict, hex string, or JSON string, got {type(value).__name__}"
