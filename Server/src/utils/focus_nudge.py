"""
Focus nudge utility for handling OS-level throttling of background Unity.

When Unity is unfocused, the OS (especially macOS App Nap) can heavily throttle
the process, causing PlayMode tests to stall. This utility temporarily brings
Unity to focus, allows it to process, then returns focus to the original app.
"""

from __future__ import annotations

import asyncio
import logging
import os
import platform
import shutil
import subprocess
import time

logger = logging.getLogger(__name__)

# Base interval between nudges (exponentially increases with consecutive nudges)
# Can be overridden via UNITY_MCP_NUDGE_BASE_INTERVAL_S environment variable
_BASE_NUDGE_INTERVAL_S = float(os.environ.get("UNITY_MCP_NUDGE_BASE_INTERVAL_S", "1.0"))

# Maximum interval between nudges (cap for exponential backoff)
# Can be overridden via UNITY_MCP_NUDGE_MAX_INTERVAL_S environment variable
_MAX_NUDGE_INTERVAL_S = float(os.environ.get("UNITY_MCP_NUDGE_MAX_INTERVAL_S", "10.0"))

# Default duration to keep Unity focused during a nudge
# Can be overridden via UNITY_MCP_NUDGE_DURATION_S environment variable
_DEFAULT_FOCUS_DURATION_S = float(os.environ.get("UNITY_MCP_NUDGE_DURATION_S", "3.0"))

_last_nudge_time: float = 0.0
_consecutive_nudges: int = 0
_last_progress_time: float = 0.0


def _is_available() -> bool:
    """Check if focus nudging is available on this platform."""
    system = platform.system()
    if system == "Darwin":
        return shutil.which("osascript") is not None
    elif system == "Windows":
        # PowerShell is typically available on Windows
        return shutil.which("powershell") is not None
    elif system == "Linux":
        return shutil.which("xdotool") is not None
    return False


def _get_current_nudge_interval() -> float:
    """
    Calculate current nudge interval using exponential backoff.

    Returns interval based on consecutive nudges without progress:
    - 0 nudges: base interval (1.0s)
    - 1 nudge: base * 2 (2.0s)
    - 2 nudges: base * 4 (4.0s)
    - 3+ nudges: base * 8 (8.0s, capped at max)
    """
    if _consecutive_nudges == 0:
        return _BASE_NUDGE_INTERVAL_S

    # Exponential backoff: interval = base * (2 ^ consecutive_nudges)
    interval = _BASE_NUDGE_INTERVAL_S * (2 ** _consecutive_nudges)
    return min(interval, _MAX_NUDGE_INTERVAL_S)


def reset_nudge_backoff() -> None:
    """
    Reset exponential backoff when progress is detected.

    Call this when test job makes progress to reset the nudge interval
    back to the base interval for quick response to future stalls.
    """
    global _consecutive_nudges, _last_progress_time
    _consecutive_nudges = 0
    _last_progress_time = time.monotonic()


def _get_frontmost_app_macos() -> str | None:
    """Get the name of the frontmost application on macOS."""
    try:
        result = subprocess.run(
            [
                "osascript", "-e",
                'tell application "System Events" to get name of first process whose frontmost is true'
            ],
            capture_output=True,
            text=True,
            timeout=5,
        )
        if result.returncode == 0:
            return result.stdout.strip()
    except Exception as e:
        logger.debug(f"Failed to get frontmost app: {e}")
    return None


def _focus_app_macos(app_name: str) -> bool:
    """Focus an application by name on macOS."""
    try:
        result = subprocess.run(
            ["osascript", "-e", f'tell application "{app_name}" to activate'],
            capture_output=True,
            text=True,
            timeout=5,
        )
        return result.returncode == 0
    except Exception as e:
        logger.debug(f"Failed to focus app {app_name}: {e}")
    return False


def _get_frontmost_app_windows() -> str | None:
    """Get the title of the frontmost window on Windows."""
    try:
        # PowerShell command to get active window title
        script = '''
Add-Type @"
using System;
using System.Runtime.InteropServices;
public class Win32 {
    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")]
    public static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder text, int count);
}
"@
$hwnd = [Win32]::GetForegroundWindow()
$sb = New-Object System.Text.StringBuilder 256
[Win32]::GetWindowText($hwnd, $sb, 256)
$sb.ToString()
'''
        result = subprocess.run(
            ["powershell", "-Command", script],
            capture_output=True,
            text=True,
            timeout=5,
        )
        if result.returncode == 0:
            return result.stdout.strip()
    except Exception as e:
        logger.debug(f"Failed to get frontmost window: {e}")
    return None


def _focus_app_windows(window_title: str) -> bool:
    """Focus a window by title on Windows. For Unity, uses Unity Editor pattern."""
    try:
        # For Unity, we use a pattern match since the title varies
        if window_title == "Unity":
            script = '''
Add-Type @"
using System;
using System.Runtime.InteropServices;
public class Win32 {
    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")]
    public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
}
"@
$unity = Get-Process | Where-Object {$_.MainWindowTitle -like "*Unity*"} | Select-Object -First 1
if ($unity) {
    [Win32]::ShowWindow($unity.MainWindowHandle, 9)
    [Win32]::SetForegroundWindow($unity.MainWindowHandle)
}
'''
        else:
            # Try to find window by title - escape special PowerShell characters
            safe_title = window_title.replace("'", "''").replace("`", "``")
            script = f'''
Add-Type @"
using System;
using System.Runtime.InteropServices;
public class Win32 {{
    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")]
    public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
}}
"@
$proc = Get-Process | Where-Object {{$_.MainWindowTitle -eq '{safe_title}'}} | Select-Object -First 1
if ($proc) {{
    [Win32]::ShowWindow($proc.MainWindowHandle, 9)
    [Win32]::SetForegroundWindow($proc.MainWindowHandle)
}}
'''
        result = subprocess.run(
            ["powershell", "-Command", script],
            capture_output=True,
            text=True,
            timeout=5,
        )
        return result.returncode == 0
    except Exception as e:
        logger.debug(f"Failed to focus window {window_title}: {e}")
    return False


def _get_frontmost_app_linux() -> str | None:
    """Get the window ID of the frontmost window on Linux."""
    try:
        result = subprocess.run(
            ["xdotool", "getactivewindow"],
            capture_output=True,
            text=True,
            timeout=5,
        )
        if result.returncode == 0:
            return result.stdout.strip()
    except Exception as e:
        logger.debug(f"Failed to get active window: {e}")
    return None


def _focus_app_linux(window_id: str) -> bool:
    """Focus a window by ID on Linux, or Unity by name."""
    try:
        if window_id == "Unity":
            # Find Unity window by name pattern
            result = subprocess.run(
                ["xdotool", "search", "--name", "Unity"],
                capture_output=True,
                text=True,
                timeout=5,
            )
            if result.returncode == 0 and result.stdout.strip():
                window_id = result.stdout.strip().split("\n")[0]
            else:
                return False

        result = subprocess.run(
            ["xdotool", "windowactivate", window_id],
            capture_output=True,
            text=True,
            timeout=5,
        )
        return result.returncode == 0
    except Exception as e:
        logger.debug(f"Failed to focus window {window_id}: {e}")
    return False


def _get_frontmost_app() -> str | None:
    """Get the frontmost application/window (platform-specific)."""
    system = platform.system()
    if system == "Darwin":
        return _get_frontmost_app_macos()
    elif system == "Windows":
        return _get_frontmost_app_windows()
    elif system == "Linux":
        return _get_frontmost_app_linux()
    return None


def _focus_app(app_or_window: str) -> bool:
    """Focus an application/window (platform-specific)."""
    system = platform.system()
    if system == "Darwin":
        return _focus_app_macos(app_or_window)
    elif system == "Windows":
        return _focus_app_windows(app_or_window)
    elif system == "Linux":
        return _focus_app_linux(app_or_window)
    return False


async def nudge_unity_focus(
    focus_duration_s: float | None = None,
    force: bool = False,
) -> bool:
    """
    Temporarily focus Unity to allow it to process, then return focus.

    Uses exponential backoff: interval starts at base (1.0s) and doubles
    with each consecutive nudge up to max (10.0s). Resets on progress.

    Args:
        focus_duration_s: How long to keep Unity focused (seconds).
            Defaults to _DEFAULT_FOCUS_DURATION_S (configurable via
            UNITY_MCP_NUDGE_DURATION_S environment variable).
        force: If True, ignore the minimum interval between nudges

    Returns:
        True if nudge was performed, False if skipped or failed
    """
    if focus_duration_s is None:
        focus_duration_s = _DEFAULT_FOCUS_DURATION_S
    global _last_nudge_time, _consecutive_nudges

    if not _is_available():
        logger.debug("Focus nudging not available on this platform")
        return False

    # Rate limit nudges using exponential backoff
    now = time.monotonic()
    current_interval = _get_current_nudge_interval()
    if not force and (now - _last_nudge_time) < current_interval:
        logger.debug(f"Skipping nudge - too soon since last nudge (interval: {current_interval:.1f}s)")
        return False

    # Get current frontmost app
    original_app = _get_frontmost_app()
    if original_app is None:
        logger.debug("Could not determine frontmost app")
        return False

    # Check if Unity is already focused (no nudge needed)
    if "Unity" in original_app:
        logger.debug("Unity already focused, no nudge needed")
        return False

    logger.info(f"Nudging Unity focus (interval: {current_interval:.1f}s, consecutive: {_consecutive_nudges}, will return to {original_app})")
    _last_nudge_time = now
    _consecutive_nudges += 1

    # Focus Unity
    if not _focus_app("Unity"):
        logger.warning("Failed to focus Unity")
        return False

    # Wait for Unity to process
    await asyncio.sleep(focus_duration_s)

    # Return focus to original app
    if original_app and original_app != "Unity":
        if _focus_app(original_app):
            logger.info(f"Returned focus to {original_app}")
        else:
            logger.warning(f"Failed to return focus to {original_app}")

    return True


def should_nudge(
    status: str,
    editor_is_focused: bool,
    last_update_unix_ms: int | None,
    current_time_ms: int | None = None,
    stall_threshold_ms: int = 3_000,
) -> bool:
    """
    Determine if we should nudge Unity based on test job state.

    Works with exponential backoff in nudge_unity_focus():
    - First nudge happens after 3s of no progress
    - Subsequent nudges use exponential backoff (1s, 2s, 4s, 8s, 10s max)
    - Backoff resets when progress is detected (call reset_nudge_backoff())

    Args:
        status: Job status ("running", "succeeded", "failed")
        editor_is_focused: Whether Unity reports being focused
        last_update_unix_ms: Last time the job was updated (Unix ms)
        current_time_ms: Current time (Unix ms), or None to use current time
        stall_threshold_ms: How long without updates before considering it stalled
            (default 3s for quick stall detection with exponential backoff)

    Returns:
        True if conditions suggest a nudge would help
    """
    # Only nudge running jobs
    if status != "running":
        return False

    # Only nudge unfocused Unity
    if editor_is_focused:
        return False

    # Check if job appears stalled
    if last_update_unix_ms is None:
        return True  # No updates yet, might be stuck at start

    if current_time_ms is None:
        current_time_ms = int(time.time() * 1000)

    time_since_update_ms = current_time_ms - last_update_unix_ms
    return time_since_update_ms > stall_threshold_ms
