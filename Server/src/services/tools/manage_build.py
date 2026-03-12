"""
Tool for Unity build pipeline — player settings, quality settings, scripting defines, builds.
Actions: get_player_settings, set_player_settings, get_quality_settings, set_quality_level,
         get_build_settings, set_build_scenes, build, get_scripting_defines, set_scripting_defines.
Uses built-in UnityEditor APIs — no package dependency.
"""
from typing import Annotated, Any, Literal

from fastmcp import Context
from mcp.types import ToolAnnotations

from services.registry import mcp_for_unity_tool
from services.tools import get_unity_instance_from_context
from transport.unity_transport import send_with_unity_instance
from transport.legacy.unity_connection import async_send_command_with_retry


@mcp_for_unity_tool(
    description=(
        "Unity build pipeline control. "
        "Actions: get_player_settings (company, product, bundle ID, version), "
        "set_player_settings (modify player settings), "
        "get_quality_settings (quality levels, current level), "
        "set_quality_level (switch active quality level), "
        "get_build_settings (active target, scenes list), "
        "set_build_scenes (set scene list for build), "
        "build (trigger player build — async job), "
        "get_scripting_defines (current defines per platform), "
        "set_scripting_defines (add/remove defines). "
        "Uses built-in UnityEditor APIs — no package dependency."
    ),
    annotations=ToolAnnotations(title="Manage Build"),
)
async def manage_build(
    ctx: Context,
    action: Annotated[
        Literal[
            "get_player_settings", "set_player_settings",
            "get_quality_settings", "set_quality_level",
            "get_build_settings", "set_build_scenes",
            "build", "get_scripting_defines", "set_scripting_defines"
        ],
        "Action to perform on Unity build pipeline."
    ],
    # Properties
    properties: Annotated[str, "JSON object of properties to set"] | None = None,
    # Build params
    target: Annotated[str, "Build target platform (e.g. StandaloneWindows64)"] | None = None,
    output_path: Annotated[str, "Output path for build"] | None = None,
    options: Annotated[str, "Build options (comma-separated)"] | None = None,
    # Quality
    level: Annotated[str, "Quality level name or index"] | None = None,
    # Scenes
    scenes: Annotated[str, "JSON array of scene paths"] | None = None,
    # Scripting defines
    platform: Annotated[str, "Target platform for defines"] | None = None,
    defines: Annotated[str, "Comma-separated scripting define symbols"] | None = None,
) -> dict[str, Any]:
    unity_instance = get_unity_instance_from_context(ctx)

    params = {
        "action": action,
        "properties": properties,
        "target": target,
        "output_path": output_path,
        "options": options,
        "level": level,
        "scenes": scenes,
        "platform": platform,
        "defines": defines,
    }
    params = {k: v for k, v in params.items() if v is not None}

    try:
        response = await send_with_unity_instance(
            async_send_command_with_retry, unity_instance, "manage_build", params
        )

        if isinstance(response, dict) and response.get("success"):
            return {
                "success": True,
                "message": response.get("message", "Build operation successful."),
                "data": response.get("data"),
            }
        return response if isinstance(response, dict) else {"success": False, "message": str(response)}

    except Exception as e:
        return {"success": False, "message": f"Python error managing build: {str(e)}"}
