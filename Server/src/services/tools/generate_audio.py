"""
Defines the generate_audio tool for AI audio (SFX / music) generation in Unity.

Thin pass-through: this tool carries NO API keys and NO file bytes. The C# side
reads the user's fal.ai key from the OS secure store, performs the provider
HTTPS call, downloads the result, and imports it as an AudioClip.
"""
from typing import Annotated, Any, Literal

from fastmcp import Context
from mcp.types import ToolAnnotations

from services.registry import mcp_for_unity_tool
from services.tools import get_unity_instance_from_context
from transport.unity_transport import send_with_unity_instance
from transport.legacy.unity_connection import async_send_command_with_retry


@mcp_for_unity_tool(
    group="asset_gen",
    description=(
        "Generate audio (sound effects and background music) with fal.ai models and import "
        "them as AudioClips into the Unity project. Bring-your-own-key: the fal key lives in "
        "the editor's secure store (shared with image generation) and never crosses the bridge.\n\n"
        "MODELS (all via fal.ai): fal-ai/stable-audio-25/text-to-audio (music + SFX, <=190s), "
        "cassetteai/sound-effects-generator (SFX, <=30s), cassetteai/music-generator (music), "
        "fal-ai/lyria2 (music). Omit model to use the model selected in the "
        "MCP for Unity -> Asset Generation tab.\n\n"
        "ACTIONS:\n"
        "- generate: Submit an audio job from a text prompt. Returns { job_id }; poll with the "
        "status action. Params: provider (fal), prompt, model, duration (seconds), name, "
        "output_folder.\n"
        "- status: Poll an async job by job_id -> { state, progress, assetPath?, error? }.\n"
        "- cancel: Cancel an in-flight job by job_id.\n"
        "- list_providers: List configured audio providers and capabilities (no key values)."
    ),
    annotations=ToolAnnotations(
        title="Generate Audio",
        destructiveHint=False,
    ),
)
async def generate_audio(
    ctx: Context,
    action: Annotated[Literal["generate", "status", "cancel", "list_providers"],
                      "Action to perform."],

    provider: Annotated[str, "Provider id (fal)."] | None = None,
    prompt: Annotated[str, "Text prompt describing the sound or music."] | None = None,
    model: Annotated[str, "fal model id (e.g. fal-ai/stable-audio-25/text-to-audio). "
                     "Omit to use the GUI-selected default."] | None = None,
    duration: Annotated[float, "Requested length in seconds (soft-clamped per model)."] | None = None,
    name: Annotated[str, "Base name for the imported asset."] | None = None,
    output_folder: Annotated[str, "Destination folder under Assets/ for the import."] | None = None,
    job_id: Annotated[str, "Job id for status/cancel."] | None = None,
) -> dict[str, Any]:
    unity_instance = await get_unity_instance_from_context(ctx)

    params_dict = {
        "action": action.lower(),
        "provider": provider,
        "prompt": prompt,
        "model": model,
        "duration": duration,
        "name": name,
        "outputFolder": output_folder,
        "jobId": job_id,
    }

    # Remove None values
    params_dict = {k: v for k, v in params_dict.items() if v is not None}

    result = await send_with_unity_instance(
        async_send_command_with_retry,
        unity_instance,
        "generate_audio",
        params_dict,
    )

    return result if isinstance(result, dict) else {"success": False, "message": str(result)}
