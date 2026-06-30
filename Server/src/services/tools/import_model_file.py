"""
Defines the import_model_file tool: import a local 3D model file (already on disk,
e.g. exported from Blender) into the Unity project.

Thin pass-through: NO API keys and NO file bytes cross the bridge. The C# side copies
the file under Assets/ and runs the shared model-import pipeline.
"""
from typing import Annotated, Any

from fastmcp import Context
from mcp.types import ToolAnnotations

from services.registry import mcp_for_unity_tool
from services.tools import get_unity_instance_from_context
from transport.unity_transport import send_with_unity_instance
from transport.legacy.unity_connection import async_send_command_with_retry


@mcp_for_unity_tool(
    group="asset_gen",
    description=(
        "Import a local 3D model file that already exists on disk (e.g. an FBX/OBJ/glTF "
        "exported from Blender or another DCC tool) into the Unity project. The file is copied "
        "under Assets/ and run through Unity's model-import pipeline (scale-normalize, material "
        "settings; glTF requires glTFast). Carries no API keys and no file bytes over the bridge.\n\n"
        "Params: source_path (absolute or Assets-relative path to a .fbx/.obj/.glb/.gltf/.zip), "
        "name, output_folder (under Assets/), target_size. Returns { asset_path, asset_guid }.\n\n"
        "For multi-file exports (a text .gltf with an external .bin, or an .obj with a sibling "
        ".mtl/textures), zip them and pass the .zip — a bare .gltf/.obj is copied without its sidecars."
    ),
    annotations=ToolAnnotations(
        title="Import Model File",
        destructiveHint=False,
    ),
)
async def import_model_file(
    ctx: Context,
    source_path: Annotated[str, "Path to the model file on disk (.fbx/.obj/.glb/.gltf/.zip)."],
    name: Annotated[str, "Base name for the imported asset."] | None = None,
    output_folder: Annotated[str, "Destination folder under Assets/ for the import."] | None = None,
    target_size: Annotated[float, "Normalize the largest dimension to this size (meters)."] | None = None,
) -> dict[str, Any]:
    unity_instance = await get_unity_instance_from_context(ctx)

    params_dict = {
        "sourcePath": source_path,
        "name": name,
        "outputFolder": output_folder,
        "targetSize": target_size,
    }
    params_dict = {k: v for k, v in params_dict.items() if v is not None}

    result = await send_with_unity_instance(
        async_send_command_with_retry,
        unity_instance,
        "import_model_file",
        params_dict,
    )

    return result if isinstance(result, dict) else {"success": False, "message": str(result)}
