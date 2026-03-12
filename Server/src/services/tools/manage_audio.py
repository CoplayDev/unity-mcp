"""
Tool for Unity audio — sources, clips, mixers, listener, playback control.
Actions: list_sources, get_source, set_source, play, stop, pause,
         list_clips, get_clip_info, list_mixers, get_mixer, set_mixer_param.
Uses built-in UnityEngine.Audio — no package dependency.
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
        "Unity audio system management. "
        "Actions: list_sources (all AudioSource components), "
        "get_source (full AudioSource detail), "
        "set_source (modify volume, pitch, spatial blend), "
        "play/stop/pause (control playback — Play mode only), "
        "list_clips (AudioClip assets in project), "
        "get_clip_info (clip length, frequency, channels, load type), "
        "list_mixers (AudioMixer assets), "
        "get_mixer (mixer groups, exposed params, current values), "
        "set_mixer_param (set exposed mixer float). "
        "Uses built-in UnityEngine.Audio — no package dependency."
    ),
    annotations=ToolAnnotations(
        title="Manage Audio",
    ),
)
async def manage_audio(
    ctx: Context,
    action: Annotated[
        Literal[
            "list_sources", "get_source", "set_source",
            "play", "stop", "pause",
            "list_clips", "get_clip_info",
            "list_mixers", "get_mixer", "set_mixer_param"
        ],
        "Action to perform on Unity audio."
    ],
    # Target params
    target: Annotated[str, "GameObject name/ID (for source actions) or asset path/name (for clip/mixer)"] | None = None,
    # Properties for set actions
    properties: Annotated[str, "JSON object of properties to set"] | None = None,
    # Mixer param
    param_name: Annotated[str, "Exposed mixer parameter name (for set_mixer_param)"] | None = None,
    value: Annotated[float, "Value to set (for set_mixer_param)"] | None = None,
    # Filter/pagination
    filter: Annotated[str, "Name filter for list operations"] | None = None,
    page_size: Annotated[int, "Max results to return (default 50)"] | None = None,
    cursor: Annotated[int, "Pagination cursor (0-based offset)"] | None = None,
) -> dict[str, Any]:
    unity_instance = get_unity_instance_from_context(ctx)

    params = {
        "action": action,
        "target": target,
        "properties": properties,
        "param_name": param_name,
        "value": value,
        "filter": filter,
        "page_size": page_size,
        "cursor": cursor,
    }
    params = {k: v for k, v in params.items() if v is not None}

    try:
        response = await send_with_unity_instance(
            async_send_command_with_retry, unity_instance, "manage_audio", params
        )

        if isinstance(response, dict) and response.get("success"):
            return {
                "success": True,
                "message": response.get("message", "Audio operation successful."),
                "data": response.get("data"),
            }
        return response if isinstance(response, dict) else {"success": False, "message": str(response)}

    except Exception as e:
        return {"success": False, "message": f"Python error managing audio: {str(e)}"}
