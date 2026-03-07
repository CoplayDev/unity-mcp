from typing import Annotated, Any, Literal

from fastmcp import Context
from mcp.types import ToolAnnotations

from services.registry import mcp_for_unity_tool
from services.tools import get_unity_instance_from_context
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

ALL_ACTIONS = SETUP_ACTIONS + CREATION_ACTIONS + CONFIGURATION_ACTIONS + EXTENSION_ACTIONS + CONTROL_ACTIONS


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
        "- list_cameras: List all cameras with status\n"
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
) -> dict[str, Any]:
    """Unified camera management tool (Unity Camera + Cinemachine)."""

    action_normalized = action.lower()

    if action_normalized not in ALL_ACTIONS:
        categories = {
            "Setup": SETUP_ACTIONS,
            "Creation": CREATION_ACTIONS,
            "Configuration": CONFIGURATION_ACTIONS,
            "Extensions": EXTENSION_ACTIONS,
            "Control": CONTROL_ACTIONS,
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

    params_dict = {k: v for k, v in params_dict.items() if v is not None}

    result = await send_with_unity_instance(
        async_send_command_with_retry,
        unity_instance,
        "manage_camera",
        params_dict,
    )

    return result if isinstance(result, dict) else {"success": False, "message": str(result)}
