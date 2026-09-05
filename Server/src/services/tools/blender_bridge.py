"""
Defines the blender_bridge tool: drive a running Blender (BlenderMCP addon socket) from the
Unity Editor so a Blender → Unity handoff is a single call.

Thin pass-through: the C# side (Editor/Tools/Blender/BlenderBridgeTool.cs) opens the addon
socket itself, exports/imports through the shared model pipeline, and places the result in the
open scene. No API keys and no file bytes cross the MCP bridge. Socket host/port and the
blender-mcp checkout path are configured in Window > MCP for Unity > Generative > Blender Bridge.
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
        "Bridge to a running Blender that has the BlenderMCP addon connected (socket, default "
        "127.0.0.1:9876; configured in Window > MCP for Unity > Generative > Blender Bridge). "
        "Unity talks to the addon directly, so no BlenderMCP client is needed.\n\n"
        "Actions:\n"
        "- status: is Blender reachable, is the checkout configured, does the installed addon match it.\n"
        "- scene_info / object_info(object_name): read Blender scene / one object.\n"
        "- screenshot(max_size, output_folder): Blender viewport → PNG under Library/BlenderBridge "
        "(or copied under Assets/ when output_folder is given). Returns the path.\n"
        "- run_python(code): execute Python inside Blender; returns stdout.\n"
        "- import_model: export from Blender (object_names with their children, or selection_only, "
        "else the whole scene) as glb (default; keeps PBR, emission, animation) or fbx, import it "
        "through the shared model pipeline, place it in the open scene at position, and scale it so "
        "its largest dimension equals target_size meters (0 keeps the imported scale). Set "
        "apply_modifiers=false for rigs / shape keys. animation_type applies to FBX only. "
        "auto_animate (default true) creates a looping AnimatorController for imported clips so the "
        "model actually moves; save_prefab stores the placed instance as a prefab; ensure_bloom adds a "
        "Bloom volume when the model has emissive materials. "
        "Returns asset_path, asset_guid, game_object, bounds, and animation / prefab_path / bloom when applicable.\n"
        "- compare_screenshot(game_object, max_size, output_folder): Blender viewport (left) and a Unity capture "
        "framed on the placed object (right) composited into one PNG, for eyeballing fidelity.\n"
        "- setup_bloom: enable post-processing on the main camera and add a Bloom override to the global volume.\n"
        "- check_updates: git fetch the blender-mcp checkout and report how far behind its remotes "
        "it is, plus whether Blender's installed addon.py matches the checkout.\n"
        "- sync_addon(force): copy the checkout's addon.py into Blender's addons folder (backs up "
        "the old file); restart Blender afterwards.\n\n"
        "check_updates and sync_addon need the checkout path to be set; the other actions only need "
        "Blender running with the addon connected."
    ),
    annotations=ToolAnnotations(
        title="Blender Bridge",
        destructiveHint=True,
    ),
)
async def blender_bridge(
    ctx: Context,
    action: Annotated[
        Literal["status", "scene_info", "object_info", "screenshot", "run_python",
                "import_model", "compare_screenshot", "setup_bloom", "check_updates", "sync_addon"],
        "Operation to perform.",
    ],
    object_name: Annotated[str, "object_info: name of the Blender object to inspect."] | None = None,
    object_names: Annotated[
        list[str], "import_model: Blender objects to export (children included). Omit for selection_only or the whole scene."
    ] | None = None,
    selection_only: Annotated[bool, "import_model: export only what is currently selected in Blender."] | None = None,
    format: Annotated[Literal["glb", "fbx"], "import_model: export format (default glb)."] | None = None,  # noqa: A002
    name: Annotated[str, "import_model: asset and GameObject name (defaults to the single exported object)."] | None = None,
    target_size: Annotated[float, "import_model: final size in meters of the largest dimension; 0 keeps the imported scale."] | None = None,
    position: Annotated[list[float], "import_model: world position [x, y, z] for the placed instance."] | None = None,
    place_in_scene: Annotated[bool, "import_model: instantiate the imported asset into the open scene (default true)."] | None = None,
    apply_modifiers: Annotated[bool, "import_model: bake modifiers on export (default true; false for skinned meshes / shape keys)."] | None = None,
    output_folder: Annotated[str, "import_model / screenshot: destination folder under Assets/."] | None = None,
    animation_type: Annotated[
        Literal["none", "generic", "humanoid", "legacy"],
        "import_model, FBX only: rig/animation import mode (FBX imports zero clips unless set).",
    ] | None = None,
    auto_animate: Annotated[bool, "import_model: create a looping AnimatorController for imported clips (default true)."] | None = None,
    save_prefab: Annotated[bool, "import_model: save the placed instance as a prefab next to the asset."] | None = None,
    ensure_bloom: Annotated[bool, "import_model: add a Bloom volume when the model has emissive materials."] | None = None,
    game_object: Annotated[str, "compare_screenshot: name of the placed GameObject to frame in Unity."] | None = None,
    code: Annotated[str, "run_python: Python source to execute inside Blender."] | None = None,
    max_size: Annotated[int, "screenshot: max pixels on the longest side (default 1000)."] | None = None,
    force: Annotated[bool, "sync_addon: overwrite even when the installed addon already matches."] | None = None,
    timeout_seconds: Annotated[int, "Seconds to wait for Blender (default 180; big exports can be slow)."] | None = None,
) -> dict[str, Any]:
    """Forward one Blender Bridge action to the Unity Editor and return its response."""
    unity_instance = await get_unity_instance_from_context(ctx)

    params_dict = {
        "action": action,
        "objectName": object_name,
        "objectNames": object_names,
        "selectionOnly": selection_only,
        "format": format,
        "name": name,
        "targetSize": target_size,
        "position": position,
        "placeInScene": place_in_scene,
        "applyModifiers": apply_modifiers,
        "outputFolder": output_folder,
        "animationType": animation_type,
        "autoAnimate": auto_animate,
        "savePrefab": save_prefab,
        "ensureBloom": ensure_bloom,
        "gameObject": game_object,
        "code": code,
        "maxSize": max_size,
        "force": force,
        "timeoutSeconds": timeout_seconds,
    }
    params_dict = {k: v for k, v in params_dict.items() if v is not None}

    result = await send_with_unity_instance(
        async_send_command_with_retry,
        unity_instance,
        "blender_bridge",
        params_dict,
    )

    return result if isinstance(result, dict) else {"success": False, "message": str(result)}
