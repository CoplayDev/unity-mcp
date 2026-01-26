from typing import Annotated, Any, Literal

from fastmcp import Context
from mcp.types import ToolAnnotations

from services.registry import mcp_for_unity_tool
from services.tools import get_unity_instance_from_context
from transport.unity_transport import send_with_unity_instance
from transport.legacy.unity_connection import async_send_command_with_retry

# All possible actions grouped by component type
PARTICLE_ACTIONS = [
    "particle_get_info", "particle_set_main", "particle_set_emission", "particle_set_shape",
    "particle_set_color_over_lifetime", "particle_set_size_over_lifetime",
    "particle_set_velocity_over_lifetime", "particle_set_noise", "particle_set_renderer",
    "particle_enable_module", "particle_play", "particle_stop", "particle_pause",
    "particle_restart", "particle_clear", "particle_add_burst", "particle_clear_bursts"
]

VFX_ACTIONS = [
    # Asset management
    "vfx_create_asset", "vfx_assign_asset", "vfx_list_templates", "vfx_list_assets",
    # Runtime control
    "vfx_get_info", "vfx_set_float", "vfx_set_int", "vfx_set_bool",
    "vfx_set_vector2", "vfx_set_vector3", "vfx_set_vector4", "vfx_set_color",
    "vfx_set_gradient", "vfx_set_texture", "vfx_set_mesh", "vfx_set_curve",
    "vfx_send_event", "vfx_play", "vfx_stop", "vfx_pause", "vfx_reinit",
    "vfx_set_playback_speed", "vfx_set_seed"
]

LINE_ACTIONS = [
    "line_get_info", "line_set_positions", "line_add_position", "line_set_position",
    "line_set_width", "line_set_color", "line_set_material", "line_set_properties",
    "line_clear", "line_create_line", "line_create_circle", "line_create_arc", "line_create_bezier"
]

TRAIL_ACTIONS = [
    "trail_get_info", "trail_set_time", "trail_set_width", "trail_set_color",
    "trail_set_material", "trail_set_properties", "trail_clear", "trail_emit"
]

ALL_ACTIONS = ["ping"] + PARTICLE_ACTIONS + VFX_ACTIONS + LINE_ACTIONS + TRAIL_ACTIONS


@mcp_for_unity_tool(
    description=(
        "Manage Unity VFX components (ParticleSystem, VisualEffect, LineRenderer, TrailRenderer). "
        "Action prefixes: particle_*, vfx_*, line_*, trail_*. "
        "Action-specific parameters go in `properties` (keys match ManageVFX.cs)."
    ),
    annotations=ToolAnnotations(
        title="Manage VFX",
        destructiveHint=True,
    ),
)
async def manage_vfx(
    ctx: Context,
    action: Annotated[str, "Action to perform (prefix: particle_, vfx_, line_, trail_)."],
    target: Annotated[str | None, "Target GameObject (name/path/id)."] = None,
    search_method: Annotated[
        Literal["by_id", "by_name", "by_path", "by_tag", "by_layer"] | None,
        "How to find the target GameObject.",
    ] = None,
    properties: Annotated[
        dict[str, Any] | str | None,
        "Action-specific parameters (dict or JSON string).",
    ] = None,
) -> dict[str, Any]:
    """Unified VFX management tool."""

    # Normalize action to lowercase to match Unity-side behavior
    action_normalized = action.lower()

    # Validate action against known actions using normalized value
    if action_normalized not in ALL_ACTIONS:
        # Provide helpful error with closest matches by prefix
        prefix = action_normalized.split(
            "_")[0] + "_" if "_" in action_normalized else ""
        available_by_prefix = {
            "particle_": PARTICLE_ACTIONS,
            "vfx_": VFX_ACTIONS,
            "line_": LINE_ACTIONS,
            "trail_": TRAIL_ACTIONS,
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
                    "particle_*, vfx_*, line_*, trail_*. Run with action='ping' to test connection."
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

    params_dict = {k: v for k, v in params_dict.items() if v is not None}

    # Send to Unity
    result = await send_with_unity_instance(
        async_send_command_with_retry,
        unity_instance,
        "manage_vfx",
        params_dict,
    )

    return result if isinstance(result, dict) else {"success": False, "message": str(result)}
