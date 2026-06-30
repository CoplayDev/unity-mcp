"""
Defines the generate_model tool for AI 3D model generation in Unity.

Thin pass-through: this tool carries NO API keys and NO file bytes. The C# side
reads the user's provider key from the OS secure store, performs the provider
HTTPS call, downloads the result, and imports it into the Unity project.
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
        "Generate 3D models with AI providers (Tripo, Meshy) and import them "
        "into the Unity project. Bring-your-own-key: provider keys live in the editor's "
        "secure store and never cross the bridge.\n\n"
        "ACTIONS:\n"
        "- generate: Submit a generation job (text->3D or image->3D). Returns { job_id } "
        "immediately; poll with the status action. Params: provider, mode (text|image), "
        "prompt, image_path|image_url, format (glb|fbx|obj|usdz), target_size, texture, "
        "tier, name, output_folder.\n"
        "- status: Poll an async job by job_id -> { state, progress, assetPath?, error? }.\n"
        "- cancel: Cancel an in-flight job by job_id.\n"
        "- list_providers: List configured 3D providers and capabilities (no key values)."
    ),
    annotations=ToolAnnotations(
        title="Generate Model",
        destructiveHint=False,
    ),
)
async def generate_model(
    ctx: Context,
    action: Annotated[Literal["generate", "status", "cancel", "list_providers"],
                      "Action to perform."],

    provider: Annotated[str, "Provider id (tripo, meshy)."] | None = None,
    mode: Annotated[str, "Generation mode: text or image."] | None = None,
    prompt: Annotated[str, "Text prompt for text->3D."] | None = None,
    image_path: Annotated[str, "Path to a source image for image->3D."] | None = None,
    image_url: Annotated[str, "URL of a source image for image->3D."] | None = None,
    format: Annotated[str, "Output model format: glb, fbx, obj, or usdz."] | None = None,
    target_size: Annotated[float, "Normalize the largest dimension to this size (meters)."] | None = None,
    texture: Annotated[bool, "Whether to generate textures for the model."] | None = None,
    tier: Annotated[str, "Provider quality/cost tier."] | None = None,
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
        "format": format,
        "targetSize": target_size,
        "texture": texture,
        "tier": tier,
        "name": name,
        "outputFolder": output_folder,
        "jobId": job_id,
    }

    # Remove None values
    params_dict = {k: v for k, v in params_dict.items() if v is not None}

    result = await send_with_unity_instance(
        async_send_command_with_retry,
        unity_instance,
        "generate_model",
        params_dict,
    )

    return result if isinstance(result, dict) else {"success": False, "message": str(result)}
