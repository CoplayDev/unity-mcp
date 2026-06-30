"""
Defines the generate_image tool for AI 2D image generation in Unity.

Thin pass-through: this tool carries NO API keys and NO file bytes. The C# side
reads the user's provider key from the OS secure store, performs the provider
HTTPS call, downloads/decodes the result, and imports it as a texture.
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
        "Generate 2D images with AI providers (fal.ai, OpenRouter) and import them as "
        "textures/sprites into the Unity project. Bring-your-own-key: provider keys live "
        "in the editor's secure store and never cross the bridge.\n\n"
        "ACTIONS:\n"
        "- generate: Submit an image job (text->image or image->image). Returns { job_id }; "
        "poll with the status action. Params: "
        "provider, mode (text|image), prompt, image_path|image_url, model, transparent, "
        "width, height, name, output_folder.\n"
        "- remove_background: Unsupported in this version; returns an error instead of a job_id.\n"
        "- status: Poll an async job by job_id -> { state, progress, assetPath?, error? }.\n"
        "- cancel: Cancel an in-flight job by job_id.\n"
        "- list_providers: List configured image providers and capabilities (no key values)."
    ),
    annotations=ToolAnnotations(
        title="Generate Image",
        destructiveHint=False,
    ),
)
async def generate_image(
    ctx: Context,
    action: Annotated[Literal["generate", "remove_background", "status", "cancel", "list_providers"],
                      "Action to perform."],

    provider: Annotated[str, "Provider id (fal, openrouter)."] | None = None,
    mode: Annotated[str, "Generation mode: text or image."] | None = None,
    prompt: Annotated[str, "Text prompt for text->image."] | None = None,
    image_path: Annotated[str, "Path to a source image for image->image mode."] | None = None,
    image_url: Annotated[str, "URL of a source image for image->image."] | None = None,
    model: Annotated[str, "Provider model id/slug (e.g. FLUX, gemini-2.5-flash-image)."] | None = None,
    transparent: Annotated[bool, "Mark the imported texture as alpha-is-transparency. NOTE: fal/FLUX "
                           "and OpenRouter have no generation-time transparency, so this only sets the "
                           "Unity import flag — it does not make the model render a transparent background."] | None = None,
    width: Annotated[int, "Output width in pixels."] | None = None,
    height: Annotated[int, "Output height in pixels."] | None = None,
    name: Annotated[str, "Base name for the imported asset."] | None = None,
    output_folder: Annotated[str, "Destination folder under Assets/ for the import."] | None = None,
    job_id: Annotated[str, "Job id for status/cancel."] | None = None,
) -> dict[str, Any]:
    unity_instance = await get_unity_instance_from_context(ctx)

    params_dict = {
        "action": action.lower(),
        "provider": provider,
        "mode": mode,
        "prompt": prompt,
        "imagePath": image_path,
        "imageUrl": image_url,
        "model": model,
        "transparent": transparent,
        "width": width,
        "height": height,
        "name": name,
        "outputFolder": output_folder,
        "jobId": job_id,
    }

    # Remove None values
    params_dict = {k: v for k, v in params_dict.items() if v is not None}

    result = await send_with_unity_instance(
        async_send_command_with_retry,
        unity_instance,
        "generate_image",
        params_dict,
    )

    return result if isinstance(result, dict) else {"success": False, "message": str(result)}
