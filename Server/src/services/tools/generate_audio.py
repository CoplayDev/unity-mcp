"""
Defines the generate_audio tool for AI audio generation in Unity.

Thin pass-through: this tool carries no API keys. The C# side reads provider
credentials from the OS secure store, performs the HTTPS call, downloads the
result, and imports it as an AudioClip.
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
        "Generate audio and import it as an AudioClip into the Unity project. Provider keys "
        "live in the editor's secure store and never cross the bridge. Omit model to use the "
        "model selected in the MCP for Unity -> Asset Generation tab.\n\n"
        "ACTIONS:\n"
        "- generate: Submit an audio job. Cover models require one of audio_url or "
        "audio_base64 and also accept cover_feature_id. Returns { job_id }; poll with the "
        "status action. URL results expire after 24 hours.\n"
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

    provider: Annotated[str, "Provider id."] | None = None,
    prompt: Annotated[str, "Text prompt describing the sound or music."] | None = None,
    model: Annotated[str, "Provider model id. Omit to use the GUI-selected default."] | None = None,
    duration: Annotated[float, "Requested length in seconds (soft-clamped per model)."] | None = None,
    lyrics: Annotated[str, "Optional lyrics for a cover."] | None = None,
    lyrics_optimizer: Annotated[bool, "Whether to optimize the supplied lyrics."] | None = None,
    is_instrumental: Annotated[bool, "Whether to generate an instrumental cover."] | None = None,
    audio_url: Annotated[str, "Reference-audio URL for a cover (6-360 seconds, at most 50 MB)."] | None = None,
    audio_base64: Annotated[str, "Base64 reference audio for a cover (6-360 seconds, at most 50 MB)."] | None = None,
    cover_feature_id: Annotated[str, "Optional preprocessed cover feature id."] | None = None,
    output_format: Annotated[Literal["url", "hex"], "Provider response format; URL results expire after 24 hours."] | None = None,
    audio_format: Annotated[Literal["mp3", "wav", "pcm"], "Generated audio encoding."] | None = None,
    aigc_watermark: Annotated[bool, "Whether to add the regional AIGC watermark."] | None = None,
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
        "lyrics": lyrics,
        "lyricsOptimizer": lyrics_optimizer,
        "isInstrumental": is_instrumental,
        "audioUrl": audio_url,
        "audioBase64": audio_base64,
        "coverFeatureId": cover_feature_id,
        "outputFormat": output_format,
        "audioFormat": audio_format,
        "aigcWatermark": aigc_watermark,
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
