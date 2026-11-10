"""Timeout budgets shared by the Blender HTTP bridge and CLI."""

import math
import re
from typing import Any

DEFAULT_BLENDER_TIMEOUT = 180
UNITY_FINISH_TIMEOUT = 30
SERVER_RESPONSE_GRACE = 5
CLI_RESPONSE_GRACE = 5


def blender_command_timeout(params: dict[str, Any]) -> float:
    """Allow the Blender socket budget plus Unity import/placement work.

    Match the Editor's five-second minimum and the hub's one-hour ceiling.
    Both parameter spellings are accepted, with snake_case taking precedence.
    """
    # ToolParams.GetInt treats an explicit null as present, and falls back to
    # 180 for booleans, fractional values, invalid strings and Int32 overflow.
    requested = params.get("timeout_seconds", params.get("timeoutSeconds"))
    seconds = DEFAULT_BLENDER_TIMEOUT
    if isinstance(requested, int) and not isinstance(requested, bool):
        seconds = requested
    elif isinstance(requested, float) and math.isfinite(requested) and requested.is_integer():
        seconds = int(requested)
    elif isinstance(requested, str) and re.fullmatch(r"[+-]?[0-9]+", requested.strip()):
        text = requested.strip()
        digits = text.lstrip("+-").lstrip("0") or "0"
        if len(digits) <= 10:
            seconds = int(digits) * (-1 if text.startswith("-") else 1)
    if not -(2**31) <= seconds < 2**31:
        seconds = DEFAULT_BLENDER_TIMEOUT
    return max(5.0, min(seconds, 3600.0)) + UNITY_FINISH_TIMEOUT
