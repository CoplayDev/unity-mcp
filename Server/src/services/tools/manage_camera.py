from typing import Annotated, Any, Literal

from fastmcp import Context
from fastmcp.server.server import ToolResult
from mcp.types import ToolAnnotations

from services.registry import mcp_for_unity_tool
from services.tools import get_unity_instance_from_context
from services.tools.utils import build_screenshot_params, extract_screenshot_images
from transport.unity_transport import send_with_unity_instance
from transport.legacy.unity_connection import async_send_command_with_retry

# All possible actions grouped by category
SETUP_ACTIONS = ["ping", "ensure_brain", "get_brain_status"]

CREATION_ACTIONS = ["create_camera"]

CONFIGURATION_ACTIONS = [
    "set_target", "set_priority", "set_lens",
    "set_body", "set_aim", "set_noise",
]

EXTENSION_ACTIONS = ["add_extension", "remove_extension"]

CONTROL_ACTIONS = [
    "set_blend", "force_camera", "release_override", "list_cameras",
]

CAPTURE_ACTIONS = ["screenshot", "screenshot_multiview"]

ALL_ACTIONS = SETUP_ACTIONS + CREATION_ACTIONS + CONFIGURATION_ACTIONS + EXTENSION_ACTIONS + CONTROL_ACTIONS + CAPTURE_ACTIONS


@mcp_for_unity_tool(
    group="core",
    description=(
        "Manage cameras (Unity Camera + Cinemachine). Works without Cinemachine using basic Camera; "
        "unlocks presets, pipelines, and blending when Cinemachine is installed. "
        "Use ping to check Cinemachine availability.\n\n"
        "SETUP:\n"
        "- ping: Check if Cinemachine is available\n"
        "- ensure_brain: Ensure CinemachineBrain exists on main camera\n"
        "- get_brain_status: Get Brain state (active camera, blend, etc.)\n\n"
        "CAMERA CREATION:\n"
        "- create_camera: Create camera with preset (third_person, freelook, "
        "follow, dolly, static, top_down, side_scroller). Falls back to basic Camera without Cinemachine.\n\n"
        "CAMERA CONFIGURATION:\n"
        "- set_target: Set Follow and/or LookAt targets on a camera\n"
        "- set_priority: Set camera priority for Brain selection\n"
        "- set_lens: Configure lens (fieldOfView, nearClipPlane, farClipPlane, orthographicSize, dutch)\n"
        "- set_body: Configure Body component (bodyType to swap, plus component properties)\n"
        "- set_aim: Configure Aim component (aimType to swap, plus component properties)\n"
        "- set_noise: Configure Noise component (amplitudeGain, frequencyGain)\n\n"
        "EXTENSIONS:\n"
        "- add_extension: Add extension (extensionType: CinemachineConfiner2D, CinemachineDeoccluder, "
        "CinemachineImpulseListener, CinemachineFollowZoom, CinemachineRecomposer, etc.)\n"
        "- remove_extension: Remove extension by type\n\n"
        "CAMERA CONTROL:\n"
        "- set_blend: Configure default blend (style: Cut/EaseInOut/Linear/etc., duration)\n"
        "- force_camera: Override Brain to use specific camera\n"
        "- release_override: Release camera override\n"
        "- list_cameras: List all cameras with status\n\n"
        "CAPTURE:\n"
        "- screenshot: Capture from a camera. Supports include_image=true for inline base64 PNG, "
        "batch='surround' for 6-angle contact sheet, batch='orbit' for configurable grid, "
        "look_at/view_position for positioned capture.\n"
        "- screenshot_multiview: Shorthand for screenshot with batch='surround' and include_image=true."
    ),
    annotations=ToolAnnotations(
        title="Manage Camera",
        destructiveHint=True,
    ),
)
async def manage_camera(
    ctx: Context,
    action: Annotated[str, "The camera action to perform."],
    target: Annotated[str | None, "Target camera (name, path, or instance ID)."] = None,
    search_method: Annotated[
        Literal["by_id", "by_name", "by_path"] | None,
        "How to find target.",
    ] = None,
    properties: Annotated[
        dict[str, Any] | str | None,
        "Action-specific parameters (dict or JSON string).",
    ] = None,
    # --- screenshot params ---
    screenshot_file_name: Annotated[str | None,
        "Screenshot file name (optional). Defaults to timestamp."] = None,
    screenshot_super_size: Annotated[int | str | None,
        "Screenshot supersize multiplier (integer >= 1)."] = None,
    camera: Annotated[str | None,
        "Camera to capture from (name, path, or instance ID). Defaults to Camera.main."] = None,
    include_image: Annotated[bool | str | None,
        "If true, return screenshot as inline base64 PNG. Default false."] = None,
    max_resolution: Annotated[int | str | None,
        "Max resolution (longest edge px) for inline image. Default 640."] = None,
    batch: Annotated[str | None,
        "Batch capture mode: 'surround' (6 angles) or 'orbit' (configurable grid)."] = None,
    look_at: Annotated[str | int | list[float] | None,
        "Target to aim camera at. GameObject name/path/ID or [x,y,z]."] = None,
    view_position: Annotated[list[float] | str | None,
        "World position [x,y,z] to place camera for positioned capture."] = None,
    view_rotation: Annotated[list[float] | str | None,
        "Euler rotation [x,y,z] for camera. Overrides look_at if both provided."] = None,
    orbit_angles: Annotated[int | str | None,
        "Number of azimuth samples for batch='orbit' (default 8, max 36)."] = None,
    orbit_elevations: Annotated[list[float] | str | None,
        "Elevation angles in degrees for batch='orbit' (default [0, 30, -15])."] = None,
    orbit_distance: Annotated[float | str | None,
        "Camera distance from target for batch='orbit' (default auto)."] = None,
    orbit_fov: Annotated[float | str | None,
        "Camera FOV in degrees for batch='orbit' (default 60)."] = None,
) -> dict[str, Any] | ToolResult:
    """Unified camera management tool (Unity Camera + Cinemachine)."""

    action_normalized = action.lower()

    if action_normalized not in ALL_ACTIONS:
        categories = {
            "Setup": SETUP_ACTIONS,
            "Creation": CREATION_ACTIONS,
            "Configuration": CONFIGURATION_ACTIONS,
            "Extensions": EXTENSION_ACTIONS,
            "Control": CONTROL_ACTIONS,
            "Capture": CAPTURE_ACTIONS,
        }
        category_list = "; ".join(
            f"{cat}: {', '.join(actions)}" for cat, actions in categories.items()
        )
        return {
            "success": False,
            "message": (
                f"Unknown action '{action}'. Available actions by category — {category_list}. "
                "Run with action='ping' to check Cinemachine availability."
            ),
        }

    unity_instance = await get_unity_instance_from_context(ctx)

    params_dict: dict[str, Any] = {"action": action_normalized}
    if properties is not None:
        params_dict["properties"] = properties
    if target is not None:
        params_dict["target"] = target
    if search_method is not None:
        params_dict["searchMethod"] = search_method

    # Screenshot params — only relevant for screenshot/screenshot_multiview actions
    if action_normalized in CAPTURE_ACTIONS:
        err = build_screenshot_params(
            params_dict,
            screenshot_file_name=screenshot_file_name,
            screenshot_super_size=screenshot_super_size,
            camera=camera,
            include_image=include_image,
            max_resolution=max_resolution,
            batch=batch,
            look_at=look_at,
            orbit_angles=orbit_angles,
            orbit_elevations=orbit_elevations,
            orbit_distance=orbit_distance,
            orbit_fov=orbit_fov,
            view_position=view_position,
            view_rotation=view_rotation,
        )
        if err is not None:
            return err

    result = await send_with_unity_instance(
        async_send_command_with_retry,
        unity_instance,
        "manage_camera",
        params_dict,
    )

    if not isinstance(result, dict):
        return {"success": False, "message": str(result)}

    # For capture actions, check for inline images to return as ImageContent
    if action_normalized in CAPTURE_ACTIONS:
        image_result = extract_screenshot_images(result)
        if image_result is not None:
            return image_result

    return result
