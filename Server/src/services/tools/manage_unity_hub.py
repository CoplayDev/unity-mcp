"""MCP tool for managing Unity Hub and Unity Editor installations on the host machine."""

from typing import Annotated, Any, Optional

from mcp.types import ToolAnnotations

from services.registry import mcp_for_unity_tool
from services.unity_hub import (
    _INSTALL_TIMEOUT,
    detect_hub_path,
    parse_available_releases,
    parse_installed_editors,
    run_hub_command,
)


ALL_ACTIONS = [
    "get_hub_info",
    "list_installed_editors",
    "list_available_releases",
    "get_install_path",
    "set_install_path",
    "install_editor",
    "install_modules",
]

READ_ONLY_ACTIONS = {
    "get_hub_info",
    "list_installed_editors",
    "list_available_releases",
    "get_install_path",
}


@mcp_for_unity_tool(
    group="unity_hub",
    unity_target=None,
    description=(
        "Manage Unity Hub and Unity Editor installations on the host machine.\n\n"
        "This tool interacts with the Unity Hub CLI directly on the host - "
        "it does NOT require a running Unity Editor instance.\n\n"
        "READ-ONLY:\n"
        "- get_hub_info: Detect Hub installation, show path and OS info\n"
        "- list_installed_editors: List all locally installed Unity Editor versions\n"
        "- list_available_releases: List available Unity Editor versions for download\n"
        "- get_install_path: Get the current Unity Editor install location\n\n"
        "STATE-CHANGING (requires confirmation):\n"
        "- set_install_path: Change where Unity Editors are installed\n"
        "- install_editor: Download and install a Unity Editor version\n"
        "- install_modules: Add platform modules (Android, iOS, etc.) to an installed editor"
    ),
    annotations=ToolAnnotations(
        title="Manage Unity Hub",
        destructiveHint=True,
        readOnlyHint=False,
        idempotentHint=False,
        openWorldHint=True,
    ),
)
async def manage_unity_hub(
    action: Annotated[str, "The Hub action to perform."],
    version: Annotated[
        Optional[str],
        "Unity Editor version (e.g., '2022.3.0f1', '6000.0.0f1').",
    ] = None,
    modules: Annotated[
        Optional[list[str]],
        "Platform modules to install (e.g., ['android', 'ios', 'webgl']).",
    ] = None,
    path: Annotated[
        Optional[str],
        "File system path for set_install_path.",
    ] = None,
    limit: Annotated[
        Optional[int],
        "Max number of releases to return for list_available_releases.",
    ] = None,
    confirm: Annotated[
        Optional[bool],
        "Set to true to confirm state-changing actions.",
    ] = None,
) -> dict[str, Any]:
    action_lower = action.lower().strip()

    if action_lower not in ALL_ACTIONS:
        return {
            "success": False,
            "message": f"Unknown action '{action}'. Valid actions: {', '.join(ALL_ACTIONS)}",
        }

    if action_lower not in READ_ONLY_ACTIONS and not confirm:
        hub_path = detect_hub_path() or "not found"
        details = _build_confirmation_message(
            action_lower,
            hub_path,
            version,
            modules,
            path,
        )
        return {
            "success": False,
            "confirmation_required": True,
            "message": details,
            "hint": "Set confirm=true to proceed.",
        }

    if action_lower == "get_hub_info":
        return await _get_hub_info()
    if action_lower == "list_installed_editors":
        return await _list_installed_editors()
    if action_lower == "list_available_releases":
        return await _list_available_releases(limit)
    if action_lower == "get_install_path":
        return await _get_install_path()
    if action_lower == "set_install_path":
        return await _set_install_path(path)
    if action_lower == "install_editor":
        return await _install_editor(version)
    if action_lower == "install_modules":
        return await _install_modules(version, modules)

    return {"success": False, "message": "Action not implemented."}


def _build_confirmation_message(
    action: str,
    hub_path: str,
    version: Optional[str],
    modules: Optional[list[str]],
    path: Optional[str],
) -> str:
    if action == "install_editor":
        return f"Install Unity Editor {version or '(version required)'} using Hub at '{hub_path}'?"
    if action == "install_modules":
        mods = ", ".join(modules) if modules else "(modules required)"
        return f"Install modules [{mods}] for Unity {version or '(version required)'} using Hub at '{hub_path}'?"
    if action == "set_install_path":
        return f"Change Unity Editor install path to '{path or '(path required)'}' using Hub at '{hub_path}'?"
    return f"Execute '{action}' on Hub at '{hub_path}'?"


async def _get_hub_info() -> dict[str, Any]:
    import platform as _platform

    hub_path = detect_hub_path()
    return {
        "success": True,
        "action": "get_hub_info",
        "data": {
            "hub_detected": hub_path is not None,
            "hub_path": hub_path,
            "os": _platform.system(),
            "os_version": _platform.version(),
            "architecture": _platform.machine(),
        },
    }


async def _list_installed_editors() -> dict[str, Any]:
    result = await run_hub_command(["editors", "--installed"])
    if not result["success"]:
        return {**result, "action": "list_installed_editors"}

    editors = parse_installed_editors(result["raw_output"])
    return {
        "success": True,
        "action": "list_installed_editors",
        "hub_path": result["hub_path"],
        "data": editors,
        "raw_output": result["raw_output"],
    }


async def _list_available_releases(limit: Optional[int]) -> dict[str, Any]:
    result = await run_hub_command(["editors", "--releases"])
    if not result["success"]:
        return {**result, "action": "list_available_releases"}

    releases = parse_available_releases(result["raw_output"], limit)
    return {
        "success": True,
        "action": "list_available_releases",
        "hub_path": result["hub_path"],
        "data": releases,
        "raw_output": result["raw_output"],
    }


async def _get_install_path() -> dict[str, Any]:
    result = await run_hub_command(["install-path", "--get"])
    if not result["success"]:
        return {**result, "action": "get_install_path"}

    return {
        "success": True,
        "action": "get_install_path",
        "hub_path": result["hub_path"],
        "data": {"install_path": result["raw_output"]},
    }


async def _set_install_path(path: Optional[str]) -> dict[str, Any]:
    if not path:
        return {
            "success": False,
            "action": "set_install_path",
            "message": "path is required.",
        }

    result = await run_hub_command(["install-path", "--set", path])
    if not result["success"]:
        return {**result, "action": "set_install_path"}

    return {
        "success": True,
        "action": "set_install_path",
        "hub_path": result["hub_path"],
        "data": {"install_path": path},
        "message": f"Install path changed to: {path}",
    }


async def _install_editor(version: Optional[str]) -> dict[str, Any]:
    if not version:
        return {
            "success": False,
            "action": "install_editor",
            "message": "version is required.",
        }

    result = await run_hub_command(
        ["install", "--version", version],
        timeout=_INSTALL_TIMEOUT,
    )
    if not result["success"]:
        return {**result, "action": "install_editor"}

    return {
        "success": True,
        "action": "install_editor",
        "hub_path": result["hub_path"],
        "data": {"version": version},
        "message": f"Unity Editor {version} installation started.",
        "raw_output": result["raw_output"],
    }


async def _install_modules(
    version: Optional[str],
    modules: Optional[list[str]],
) -> dict[str, Any]:
    if not version:
        return {
            "success": False,
            "action": "install_modules",
            "message": "version is required.",
        }
    if not modules:
        return {
            "success": False,
            "action": "install_modules",
            "message": "modules list is required and must not be empty.",
        }

    args = ["install-modules", "--version", version]
    for module_name in modules:
        args.extend(["--module", module_name])

    result = await run_hub_command(args, timeout=_INSTALL_TIMEOUT)
    if not result["success"]:
        return {**result, "action": "install_modules"}

    return {
        "success": True,
        "action": "install_modules",
        "hub_path": result["hub_path"],
        "data": {"version": version, "modules": modules},
        "message": f"Modules {modules} installation started for Unity {version}.",
        "raw_output": result["raw_output"],
    }
