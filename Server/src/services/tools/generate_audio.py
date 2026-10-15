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
        "Use list_models to discover current models; compatibility is checked before generation. Omit model to use the "
        "Asset Generation tab selection.\n\n"
        "ACTIONS:\n"
        "- generate: Submit an audio job from a text prompt. Returns { job_id }; poll with the "
        "status action. Params: provider (fal), prompt, model, duration (seconds), name, "
        "output_folder.\n"
        "- status: Poll an async job by job_id -> { state, progress, assetPath?, error? }.\n"
        "- cancel: Cancel an in-flight job by job_id.\n"
        "- list_providers: List configured audio providers and capabilities (no key values).\n"
        "- list_models: List models and freshness; refresh stale fal data in the background.\n"
        "- refresh_models: Force a fal refresh. Repeat list_models while catalogs[].refreshing is true."
    ),
    annotations=ToolAnnotations(
        title="Generate Audio",
        destructiveHint=False,
    ),
)
async def generate_audio(
    ctx: Context,
    action: Annotated[Literal["generate", "status", "cancel", "list_providers", "list_models", "refresh_models"],
                      "Action to perform."],

    provider: Annotated[str, "Provider id (fal)."] | None = None,
    prompt: Annotated[str, "Text prompt describing the sound or music."] | None = None,
    model: Annotated[str, "fal model id returned by list_models. "
                     "Omit to use the GUI-selected default."] | None = None,
    duration: Annotated[float, "Requested length in seconds (soft-clamped per model)."] | None = None,
    name: Annotated[str, "Base name for the imported asset."] | None = None,
    output_folder: Annotated[str, "Destination folder under Assets/ for the import."] | None = None,
    job_id: Annotated[str, "Job id for status/cancel."] | None = None,
    search: Annotated[str, "Filter list_models by name, id or use case."] | None = None,
    mode: Annotated[str, "Filter list_models by input mode (text)."] | None = None,
    limit: Annotated[int, "Model page size (1..200; default 50)."] | None = None,
    offset: Annotated[int, "Model page offset (default 0)."] | None = None,
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
        "search": search,
        "mode": mode,
        "limit": limit,
        "offset": offset,
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
