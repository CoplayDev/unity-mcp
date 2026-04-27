"""
OS-aware log directory resolution

Picks a platform-appropriate location for the rotating server log:
- Windows: %LOCALAPPDATA%\\UnityMCP\\Logs
- macOS:   ~/Library/Application Support/UnityMCP/Logs
- Linux/BSD: $XDG_STATE_HOME/UnityMCP/Logs (default ~/.local/state/UnityMCP/Logs)

UNITY_MCP_LOG_DIR overrides all of the above.
"""

from __future__ import annotations

import os
import sys
from collections.abc import Mapping


def resolve_log_dir(
    *,
    platform: str | None = None,
    env: Mapping[str, str] | None = None,
) -> str:
    """Return the absolute log directory path for the current OS.
    """
    if platform is None:
        platform = sys.platform
    if env is None:
        env = os.environ

    override = env.get("UNITY_MCP_LOG_DIR")
    if override:
        return os.path.expanduser(override)

    if platform == "darwin":
        return os.path.expanduser("~/Library/Application Support/UnityMCP/Logs")

    if platform == "win32":
        base = env.get("LOCALAPPDATA") or os.path.expanduser("~/AppData/Local")
        return os.path.join(base, "UnityMCP", "Logs")

    # Linux/BSD and anything else: XDG_STATE_HOME per freedesktop.org basedir spec.
    base = env.get("XDG_STATE_HOME") or os.path.expanduser("~/.local/state")
    return os.path.join(base, "UnityMCP", "Logs")
