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


def _parse_env_float(env_var: str, default: float) -> float:
    """Safely parse environment variable as float, logging warnings on failure."""
    value = os.environ.get(env_var)
    if value is None:
        return default
    try:
        parsed = float(value)
        if parsed <= 0:
            logger.warning(f"Invalid {env_var}={value!r}, using default {default}: must be > 0")
            return default
        return parsed
    except (ValueError, TypeError) as e:
        logger.warning(f"Invalid {env_var}={value!r}, using default {default}: {e}")
        return default


# Base interval between nudges (exponentially increases with consecutive nudges)
# Can be overridden via UNITY_MCP_NUDGE_BASE_INTERVAL_S environment variable
_BASE_NUDGE_INTERVAL_S = _parse_env_float("UNITY_MCP_NUDGE_BASE_INTERVAL_S", 1.0)

# Maximum interval between nudges (cap for exponential backoff)
# Can be overridden via UNITY_MCP_NUDGE_MAX_INTERVAL_S environment variable
_MAX_NUDGE_INTERVAL_S = _parse_env_float("UNITY_MCP_NUDGE_MAX_INTERVAL_S", 10.0)

# Default duration to keep Unity focused during a nudge
# Can be overridden via UNITY_MCP_NUDGE_DURATION_S environment variable
_DEFAULT_FOCUS_DURATION_S = _parse_env_float("UNITY_MCP_NUDGE_DURATION_S", 3.0)

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


def _get_current_focus_duration() -> float:
    """
    Calculate current focus duration using exponential backoff.

    Base durations (3, 5, 8, 12 seconds) are scaled proportionally by the
    configured UNITY_MCP_NUDGE_DURATION_S relative to _DEFAULT_FOCUS_DURATION_S.
    For example, if UNITY_MCP_NUDGE_DURATION_S=6.0 (2x default), all durations
    are doubled: (6, 10, 16, 24 seconds).
    """
    # Base durations for each nudge level
    base_durations = [3.0, 5.0, 8.0, 12.0]
    base_duration = base_durations[min(_consecutive_nudges, len(base_durations) - 1)]

    # Scale by ratio of configured to default duration (if UNITY_MCP_NUDGE_DURATION_S is set)
    scale = 1.0
    if os.environ.get("UNITY_MCP_NUDGE_DURATION_S") is not None:
        configured_duration = _parse_env_float("UNITY_MCP_NUDGE_DURATION_S", _DEFAULT_FOCUS_DURATION_S)
        if _DEFAULT_FOCUS_DURATION_S > 0:
            scale = configured_duration / _DEFAULT_FOCUS_DURATION_S
    duration = base_duration * scale
    if duration <= 0:
        return _DEFAULT_FOCUS_DURATION_S
    return duration


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


def _find_unity_pid_by_project_path(project_path: str) -> int | None:
    """Find Unity Editor PID by matching project path in command line args.

    Args:
        project_path: Full path to Unity project root, OR just the project name.
            - Full path: "/Users/name/Projects/MyGame"
            - Project name: "MyGame" (will match any path ending with this)

    Returns:
        PID of matching Unity process, or None if not found
    """
    try:
        # Use ps to find Unity processes with -projectpath argument
        result = subprocess.run(
            ["ps", "aux"],
            capture_output=True,
            text=True,
            timeout=5,
        )
        if result.returncode != 0:
            return None

        # Determine if project_path is a full path or just a name
        is_full_path = "/" in project_path or "\\" in project_path

        # Look for Unity.app processes with matching -projectpath
        for line in result.stdout.splitlines():
            if "Unity.app/Contents/MacOS/Unity" not in line:
                continue

            # Check for -projectpath argument
            if "-projectpath" not in line:
                continue

            if is_full_path:
                # Exact match for full path
                if f"-projectpath {project_path}" not in line:
                    continue
            else:
                # Match if path ends with project name (e.g., ".../UnityMCPTests")
                if "-projectpath" in line:
                    # Extract the path after -projectpath
                    try:
                        parts = line.split("-projectpath", 1)[1].split()[0]
                        if not parts.endswith(f"/{project_path}") and not parts.endswith(f"\\{project_path}") and parts != project_path:
                            continue
                    except (IndexError, ValueError):
                        continue

            # Extract PID (second column in ps aux output)
            parts = line.split()
            if len(parts) >= 2:
                try:
                    pid = int(parts[1])
                    logger.debug(f"Found Unity PID {pid} for project path/name {project_path}")
                    return pid
                except ValueError:
                    continue

        logger.warning(f"No Unity process found with project path/name {project_path}")
        return None
    except Exception as e:
        logger.debug(f"Failed to find Unity PID: {e}")
        return None


def _focus_app_macos(app_name: str, unity_project_path: str | None = None) -> bool:
    """Focus an application by name on macOS.

    For Unity, can target a specific instance by project path (multi-instance support).

    Args:
        app_name: Application name to focus ("Unity" or specific app name)
        unity_project_path: For Unity apps, the full project root path to match against
            -projectpath command line arg (e.g., "/path/to/project" NOT "/path/to/project/Assets")
    """
    try:
        # For Unity, use PID-based activation for precise targeting
        if app_name == "Unity":
            if unity_project_path:
                # Find specific Unity instance by project path
                pid = _find_unity_pid_by_project_path(unity_project_path)
                if pid is None:
                    logger.warning(f"Could not find Unity PID for project {unity_project_path}, falling back to any Unity")
                    return _focus_any_unity_macos()

                # Two-step activation for full Unity wake-up:
                # 1. Bring window to front
                # 2. Activate the application bundle (triggers full app activation like cmd+tab or clicking)
                script = f'''
tell application "System Events"
    set targetProc to first process whose unix id is {pid}
    set frontmost of targetProc to true

    -- Get bundle identifier to activate the app properly
    set bundleID to bundle identifier of targetProc
end tell

-- Activate using bundle identifier (ensures Unity wakes up and starts processing)
tell application id bundleID to activate
'''
                result = subprocess.run(
                    ["osascript", "-e", script],
                    capture_output=True,
                    text=True,
                    timeout=5,
                )
                if result.returncode != 0:
                    logger.debug(f"Failed to activate Unity PID {pid}: {result.stderr}")
                    return False
                logger.info(f"Activated Unity instance with PID {pid} for project {unity_project_path}")
                return True
            else:
                # No project path provided - activate any Unity process
                return _focus_any_unity_macos()
        else:
            # For other apps, use direct activation
            # Escape double quotes in app_name to prevent AppleScript injection
            escaped_app_name = app_name.replace('"', '\\"')
            result = subprocess.run(
                ["osascript", "-e", f'tell application "{escaped_app_name}" to activate'],
                capture_output=True,
                text=True,
                timeout=5,
            )
            return result.returncode == 0
    except Exception as e:
        logger.debug(f"Failed to focus app {app_name}: {e}")
    return False


def _focus_any_unity_macos() -> bool:
    """Focus any Unity process on macOS (fallback when no project path specified)."""
    try:
        script = '''
tell application "System Events"
    set unityProc to first process whose name contains "Unity"
    set frontmost of unityProc to true
end tell
'''
        result = subprocess.run(
            ["osascript", "-e", script],
            capture_output=True,
            text=True,
            timeout=5,
        )
        if result.returncode != 0:
            logger.debug(f"Failed to activate Unity via System Events: {result.stderr}")
            return False
        return True
    except Exception as e:
        logger.debug(f"Failed to focus Unity: {e}")
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


def _focus_app(app_or_window: str, unity_project_path: str | None = None) -> bool:
    """Focus an application/window (platform-specific).

    Args:
        app_or_window: Application name to focus
        unity_project_path: For Unity apps on macOS, the full project root path for
            multi-instance support
    """
    system = platform.system()
    if system == "Darwin":
        return _focus_app_macos(app_or_window, unity_project_path)
    elif system == "Windows":
        return _focus_app_windows(app_or_window)
    elif system == "Linux":
        return _focus_app_linux(app_or_window)
    return False


async def nudge_unity_focus(
    focus_duration_s: float | None = None,
    force: bool = False,
    unity_project_path: str | None = None,
) -> bool:
    """
    Temporarily focus Unity to allow it to process, then return focus.

    Uses exponential backoff for both interval and duration:
    - Interval: 1s, 2s, 4s, 8s, 10s (time between nudges)
    - Duration: 3s, 5s, 8s, 12s (how long Unity stays focused)
    Resets on progress.

    Args:
        focus_duration_s: How long to keep Unity focused (seconds).
            If None, uses exponential backoff (3s/5s/8s/12s based on consecutive nudges).
            Can be overridden with UNITY_MCP_NUDGE_DURATION_S env var.
        force: If True, ignore the minimum interval between nudges
        unity_project_path: Full path to Unity project root for multi-instance support.
            e.g., "/Users/name/project" (NOT "/Users/name/project/Assets")
            If None, targets any Unity process.

    Returns:
        True if nudge was performed, False if skipped or failed
    """
    if focus_duration_s is None:
        # Use exponential backoff for focus duration
        focus_duration_s = _get_current_focus_duration()
    if focus_duration_s <= 0:
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

    project_info = f" for {unity_project_path}" if unity_project_path else ""
    logger.info(f"Nudging Unity focus{project_info} (interval: {current_interval:.1f}s, consecutive: {_consecutive_nudges}, duration: {focus_duration_s:.1f}s, will return to {original_app})")

    # Focus Unity (with optional project path for multi-instance support)
    if not _focus_app("Unity", unity_project_path):
        logger.warning(f"Failed to focus Unity{project_info}")
        return False

    # Wait for window switch animation to complete before starting timer
    # macOS activate is asynchronous, so Unity might not be visible yet
    await asyncio.sleep(0.5)

    # Verify Unity is actually focused now
    current_app = _get_frontmost_app()
    if current_app and "Unity" not in current_app:
        logger.warning(f"Unity activation didn't complete - current app is {current_app}")
        # Continue anyway in case Unity is processing in background

    # Only update state after successful activation attempt
    _last_nudge_time = now
    _consecutive_nudges += 1

    # Wait for Unity to process (actual working time)
    await asyncio.sleep(focus_duration_s)

    # Return focus to original app
    if original_app and original_app != "Unity":
        if _focus_app(original_app):
            logger.info(f"Returned focus to {original_app} after {focus_duration_s:.1f}s Unity focus")
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
