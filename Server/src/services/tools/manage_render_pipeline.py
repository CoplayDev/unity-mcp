"""
Tool for Unity render pipeline — URP/HDRP settings, volume profiles, post-processing.
Actions: get_pipeline_info, list_volumes, get_volume, set_volume_override,
         list_renderer_features, get_render_pipeline_asset, list_post_processing,
         toggle_volume_override.
Uses built-in GraphicsSettings + Volume APIs — no package dependency.
"""
from typing import Annotated, Any, Literal

from fastmcp import Context
from mcp.types import ToolAnnotations

from services.registry import mcp_for_unity_tool
from services.tools import get_unity_instance_from_context
from transport.unity_transport import send_with_unity_instance
from transport.legacy.unity_connection import async_send_command_with_retry


@mcp_for_unity_tool(
    description=(
        "Unity render pipeline management (URP/HDRP). "
        "Actions: get_pipeline_info (active render pipeline type, asset name), "
        "list_volumes (all Volume components — global/local), "
        "get_volume (volume profile overrides and values), "
        "set_volume_override (modify a volume override value), "
        "list_renderer_features (URP renderer features), "
        "get_render_pipeline_asset (active pipeline asset settings), "
        "list_post_processing (active post-processing effects summary), "
        "toggle_volume_override (enable/disable a specific override). "
        "Uses built-in GraphicsSettings — no package dependency."
    ),
    annotations=ToolAnnotations(title="Manage Render Pipeline"),
)
async def manage_render_pipeline(
    ctx: Context,
    action: Annotated[
        Literal[
            "get_pipeline_info", "list_volumes", "get_volume",
            "set_volume_override", "list_renderer_features",
            "get_render_pipeline_asset", "list_post_processing",
            "toggle_volume_override"
        ],
        "Action to perform on Unity render pipeline."
    ],
    # Target
    target: Annotated[str, "Volume GameObject name or instance ID"] | None = None,
    # Override params
    override_type: Annotated[str, "Volume override type name (e.g. Bloom, ColorAdjustments)"] | None = None,
    property: Annotated[str, "Override property name to set"] | None = None,
    value: Annotated[str, "Value to set (string representation)"] | None = None,
    enabled: Annotated[bool, "Enable/disable override (for toggle_volume_override)"] | None = None,
    # Pagination
    page_size: Annotated[int, "Max results to return (default 50)"] | None = None,
    cursor: Annotated[int, "Pagination cursor (0-based offset)"] | None = None,
) -> dict[str, Any]:
    unity_instance = get_unity_instance_from_context(ctx)

    params = {
        "action": action,
        "target": target,
        "override_type": override_type,
        "property": property,
        "value": value,
        "enabled": enabled,
        "page_size": page_size,
        "cursor": cursor,
    }
    params = {k: v for k, v in params.items() if v is not None}

    try:
        response = await send_with_unity_instance(
            async_send_command_with_retry, unity_instance, "manage_render_pipeline", params
        )

        if isinstance(response, dict) and response.get("success"):
            return {
                "success": True,
                "message": response.get("message", "Render pipeline operation successful."),
                "data": response.get("data"),
            }
        return response if isinstance(response, dict) else {"success": False, "message": str(response)}

    except Exception as e:
        return {"success": False, "message": f"Python error managing render pipeline: {str(e)}"}
