"""Shared helper utilities for MCP server tools."""

from __future__ import annotations

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
