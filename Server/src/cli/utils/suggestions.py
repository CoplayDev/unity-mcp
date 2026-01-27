"""Helpers for CLI suggestion messages."""

from __future__ import annotations

import difflib
from typing import Iterable, List


def suggest_matches(
    value: str,
    choices: Iterable[str],
    *,
    limit: int = 3,
    cutoff: float = 0.6,
) -> List[str]:
    """Return close matches for a value from a list of choices."""
    try:
        normalized = [c for c in choices if isinstance(c, str)]
    except Exception:
        normalized = []
    if not value or not normalized:
        return []
    return difflib.get_close_matches(value, normalized, n=limit, cutoff=cutoff)


def format_suggestions(matches: Iterable[str]) -> str | None:
    """Format matches into a CLI-friendly suggestion string."""
    items = [m for m in matches if m]
    if not items:
        return None
    if len(items) == 1:
        return f"Did you mean: {items[0]}"
    joined = ", ".join(items)
    return f"Did you mean one of: {joined}"
