"""Unity Hub CLI integration - runs on the host machine, not inside Unity Editor."""

import asyncio
import os
import platform
import shutil
from typing import Any, Optional


_HUB_PATHS = {
    "Darwin": ["/Applications/Unity Hub.app/Contents/MacOS/Unity Hub"],
    "Windows": [
        r"C:\Program Files\Unity Hub\Unity Hub.exe",
        r"C:\Program Files (x86)\Unity Hub\Unity Hub.exe",
    ],
    "Linux": ["/usr/bin/unityhub", "/snap/bin/unityhub"],
}

_DEFAULT_TIMEOUT = 30
_INSTALL_TIMEOUT = 600


def detect_hub_path() -> Optional[str]:
    """Find the Unity Hub executable on the host machine."""
    env_path = os.environ.get("UNITY_HUB_PATH")
    if env_path and os.path.isfile(env_path):
        return env_path

    system = platform.system()
    for path in _HUB_PATHS.get(system, []):
        if os.path.isfile(path):
            return path

    which = shutil.which("unityhub") or shutil.which("Unity Hub")
    if which:
        return which

    return None


async def run_hub_command(
    args: list[str],
    timeout: int = _DEFAULT_TIMEOUT,
    hub_path: Optional[str] = None,
) -> dict[str, Any]:
    """Run a Unity Hub CLI command and return a structured result."""
    hub = hub_path or detect_hub_path()
    if not hub:
        searched = _HUB_PATHS.get(platform.system(), [])
        return {
            "success": False,
            "error": {
                "type": "hub_not_found",
                "message": (
                    "Unity Hub executable not found. "
                    f"Searched: {searched}. Set UNITY_HUB_PATH env var to override."
                ),
            },
        }

    cmd = [hub, "--", "--headless", *args]

    try:
        proc = await asyncio.wait_for(
            asyncio.create_subprocess_exec(
                *cmd,
                stdout=asyncio.subprocess.PIPE,
                stderr=asyncio.subprocess.PIPE,
            ),
            timeout=5,
        )
        stdout_bytes, stderr_bytes = await asyncio.wait_for(
            proc.communicate(),
            timeout=timeout,
        )
    except asyncio.TimeoutError:
        return {
            "success": False,
            "error": {
                "type": "timeout",
                "message": f"Hub command timed out after {timeout}s",
                "command": args,
            },
        }
    except FileNotFoundError:
        return {
            "success": False,
            "error": {
                "type": "hub_not_found",
                "message": f"Hub executable not found at: {hub}",
            },
        }
    except Exception as exc:
        return {
            "success": False,
            "error": {
                "type": "subprocess_error",
                "message": str(exc),
            },
        }

    stdout = stdout_bytes.decode("utf-8", errors="replace").strip()
    stderr = stderr_bytes.decode("utf-8", errors="replace").strip()

    if proc.returncode != 0:
        return {
            "success": False,
            "hub_path": hub,
            "error": {
                "type": "hub_command_failed",
                "message": stderr or stdout or f"Exit code {proc.returncode}",
                "exit_code": proc.returncode,
                "stderr": stderr,
                "stdout": stdout,
            },
        }

    return {
        "success": True,
        "hub_path": hub,
        "raw_output": stdout,
        "stderr": stderr if stderr else None,
    }


def parse_installed_editors(raw_output: str) -> list[dict[str, str]]:
    """Parse `editors --installed` output into a structured list."""
    editors: list[dict[str, str]] = []
    for line in raw_output.strip().splitlines():
        line = line.strip()
        if not line:
            continue

        # Format: "6000.3.9f1  (Apple silicon) installed at /path/to/Unity.app"
        # or: "2022.3.0f1 , installed at /path/to/editor"
        path = ""
        installed_at_idx = line.lower().find("installed at")
        if installed_at_idx >= 0:
            path = line[installed_at_idx + len("installed at"):].strip()
            version_part = line[:installed_at_idx].strip().rstrip(",").strip()
        else:
            parts = line.split(",", 1)
            version_part = parts[0].strip()
            if len(parts) > 1:
                path = parts[1].strip()

        # Extract clean version (first token before any parenthetical)
        version = version_part.split("(")[0].strip().split()[0] if version_part else ""

        if version:
            editors.append({"version": version, "path": path})

    return editors


def parse_available_releases(
    raw_output: str,
    limit: Optional[int] = None,
) -> list[dict[str, str]]:
    """Parse `editors --releases` output into a structured list."""
    releases: list[dict[str, str]] = []
    for line in raw_output.strip().splitlines():
        line = line.strip()
        if not line:
            continue

        version = line.split(",", 1)[0].strip().split(" ")[0].strip()
        if not version:
            continue

        entry = {"version": version}
        if "LTS" in line:
            entry["channel"] = "LTS"
        elif "Tech" in line:
            entry["channel"] = "Tech"
        releases.append(entry)

    if limit and limit > 0:
        releases = releases[:limit]

    return releases
