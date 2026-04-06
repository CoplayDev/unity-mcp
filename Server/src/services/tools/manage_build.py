"""Build management — player builds, platform switching, settings, batch automation."""

from typing import Annotated, Any, Optional

from fastmcp import Context
from mcp.types import ToolAnnotations

from services.registry import mcp_for_unity_tool
from services.tools import get_unity_instance_from_context
from services.tools.utils import coerce_bool, parse_json_payload
from transport.unity_transport import send_with_unity_instance
from transport.legacy.unity_connection import async_send_command_with_retry

ALL_ACTIONS = [
    "build",
    "status",
    "platform",
    "settings",
    "scenes",
    "profiles",
    "batch",
    "cancel",
]


async def _send_build_command(
    ctx: Context,
    params_dict: dict[str, Any],
) -> dict[str, Any]:
    unity_instance = await get_unity_instance_from_context(ctx)
    result = await send_with_unity_instance(
        async_send_command_with_retry, unity_instance, "manage_build", params_dict
    )
    return result if isinstance(result, dict) else {"success": False, "message": str(result)}


@mcp_for_unity_tool(
    group="core",
    description=(
        "Manage Unity player builds — trigger builds, switch platforms, configure settings, "
        "manage build scenes and profiles, run batch builds across platforms. "
        "Actions: build, status, platform, settings, scenes, profiles, batch, cancel."
    ),
    annotations=ToolAnnotations(
        title="Manage Build",
        destructiveHint=True,
        readOnlyHint=False,
    ),
)
async def manage_build(
    ctx: Context,
    action: Annotated[str, "Action: build, status, platform, settings, scenes, profiles, batch, cancel"],
    target: Annotated[Optional[str], "Build target: windows64, osx, linux64, android, ios, webgl, uwp, tvos, visionos"] = None,
    output_path: Annotated[Optional[str], "Output path for the build"] = None,
    scenes: Annotated[Optional[str], "JSON array of scene paths, or comma-separated paths"] = None,
    development: Annotated[Optional[str], "Development build (true/false)"] = None,
    options: Annotated[Optional[str], "JSON array of BuildOptions: clean_build, auto_run, deep_profiling, compress_lz4, strict_mode, detailed_report"] = None,
    subtarget: Annotated[Optional[str], "Build subtarget: player or server"] = None,
    scripting_backend: Annotated[Optional[str], "Scripting backend: mono or il2cpp (persistent change)"] = None,
    profile: Annotated[Optional[str], "Build Profile asset path (Unity 6+ only)"] = None,
    property: Annotated[Optional[str], "Settings property: product_name, company_name, version, bundle_id, scripting_backend, defines, architecture"] = None,
    value: Annotated[Optional[str], "Value to set for the property (omit to read)"] = None,
    activate: Annotated[Optional[str], "Activate a build profile (true/false)"] = None,
    targets: Annotated[Optional[str], "JSON array of targets for batch build"] = None,
    profiles: Annotated[Optional[str], "JSON array of profile paths for batch build (Unity 6+)"] = None,
    output_dir: Annotated[Optional[str], "Base output directory for batch builds"] = None,
    job_id: Annotated[Optional[str], "Job ID for status/cancel"] = None,
) -> dict[str, Any]:
    action_lower = action.lower()
    if action_lower not in ALL_ACTIONS:
        return {
            "success": False,
            "message": f"Unknown action '{action}'. Valid actions: {', '.join(ALL_ACTIONS)}",
        }

    params_dict: dict[str, Any] = {"action": action_lower}

    coerced_development = coerce_bool(development, default=None)
    coerced_activate = coerce_bool(activate, default=None)
    parsed_scenes = parse_json_payload(scenes) if scenes else None
    # Support comma-separated scene paths as an alternative to JSON array
    if isinstance(parsed_scenes, str):
        parsed_scenes = [s.strip() for s in parsed_scenes.split(",") if s.strip()]
    parsed_options = parse_json_payload(options) if options else None
    parsed_targets = parse_json_payload(targets) if targets else None
    parsed_profiles = parse_json_payload(profiles) if profiles else None

    param_map: dict[str, Any] = {
        "target": target,
        "output_path": output_path,
        "scenes": parsed_scenes,
        "development": coerced_development,
        "options": parsed_options,
        "subtarget": subtarget,
        "scripting_backend": scripting_backend,
        "profile": profile,
        "property": property,
        "value": value,
        "activate": coerced_activate,
        "targets": parsed_targets,
        "profiles": parsed_profiles,
        "output_dir": output_dir,
        "job_id": job_id,
    }

    for key, val in param_map.items():
        if val is not None:
            params_dict[key] = val

    return await _send_build_command(ctx, params_dict)
