from typing import Annotated, Any, Literal

from fastmcp import Context
from mcp.types import ToolAnnotations

from services.registry import mcp_for_unity_tool
from services.tools import get_unity_instance_from_context
from transport.unity_transport import send_with_unity_instance
from transport.legacy.unity_connection import async_send_command_with_retry

ANIMATOR_ACTIONS = [
    "animator_get_info", "animator_get_parameter",
    "animator_play", "animator_crossfade",
    "animator_set_parameter", "animator_set_speed", "animator_set_enabled",
]

CONTROLLER_ACTIONS = [
    "controller_create", "controller_add_state", "controller_add_transition",
    "controller_add_parameter", "controller_get_info", "controller_assign",
    "controller_add_layer", "controller_remove_layer", "controller_set_layer_weight",
    "controller_create_blend_tree_1d", "controller_create_blend_tree_2d", "controller_add_blend_tree_child",
]

CLIP_ACTIONS = [
    "clip_create", "clip_get_info",
    "clip_add_curve", "clip_set_curve", "clip_set_vector_curve",
    "clip_create_preset", "clip_assign",
    "clip_add_event", "clip_remove_event",
]

ALL_ACTIONS = ANIMATOR_ACTIONS + CONTROLLER_ACTIONS + CLIP_ACTIONS #Not loaded in the MCP context, but will return this in the error response (1 Shot)


@mcp_for_unity_tool(
    description=(
        "Manage Unity animation: Animator control and AnimationClip creation. "
        "Action prefixes: animator_* (play, crossfade, set parameters, get info), "
        "controller_* (create AnimatorControllers, add states/transitions/parameters), "
        "clip_* (create clips, add keyframe curves, assign to GameObjects). "
        "Action-specific parameters go in `properties` (keys match ManageAnimation.cs)."
    ),
    annotations=ToolAnnotations(
        title="Manage Animation",
        destructiveHint=True,
    ),
)
async def manage_animation(
    ctx: Context,
    action: Annotated[str, "Action to perform (prefix: animator_, controller_, clip_)."],
    target: Annotated[str | None, "Target GameObject (name/path/id)."] = None,
    search_method: Annotated[
        Literal["by_id", "by_name", "by_path", "by_tag", "by_layer"] | None,
        "How to find the target GameObject.",
    ] = None,
    clip_path: Annotated[str | None, "Asset path for AnimationClip (e.g. 'Assets/Animations/Walk.anim')."] = None,
    controller_path: Annotated[str | None, "Asset path for AnimatorController (e.g. 'Assets/Animators/Player.controller')."] = None,
    properties: Annotated[
        dict[str, Any] | str | None,
        "Action-specific parameters (dict or JSON string).",
    ] = None,
) -> dict[str, Any]:
    """Unified animation management tool."""

    action_normalized = action.lower()

    if action_normalized not in ALL_ACTIONS:
        prefix = action_normalized.split("_")[0] + "_" if "_" in action_normalized else ""
        available_by_prefix = {
            "animator_": ANIMATOR_ACTIONS,
            "controller_": CONTROLLER_ACTIONS,
            "clip_": CLIP_ACTIONS,
        }
        suggestions = available_by_prefix.get(prefix, [])
        if suggestions:
            return {
                "success": False,
                "message": f"Unknown action '{action}'. Available {prefix}* actions: {', '.join(suggestions)}",
            }
        else:
            return {
                "success": False,
                "message": (
                    f"Unknown action '{action}'. Use prefixes: "
                    "animator_* (Animator control), controller_* (AnimatorController CRUD), "
                    "clip_* (AnimationClip operations)."
                ),
            }

    unity_instance = get_unity_instance_from_context(ctx)

    params_dict: dict[str, Any] = {"action": action_normalized}
    if properties is not None:
        params_dict["properties"] = properties
    if target is not None:
        params_dict["target"] = target
    if search_method is not None:
        params_dict["searchMethod"] = search_method
    if clip_path is not None:
        params_dict["clipPath"] = clip_path
    if controller_path is not None:
        params_dict["controllerPath"] = controller_path

    params_dict = {k: v for k, v in params_dict.items() if v is not None}

    result = await send_with_unity_instance(
        async_send_command_with_retry,
        unity_instance,
        "manage_animation",
        params_dict,
    )

    return result if isinstance(result, dict) else {"success": False, "message": str(result)}
