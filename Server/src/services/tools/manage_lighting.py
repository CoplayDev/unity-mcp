"""
Tool for Unity lighting — lights, probes, lightmapping, environment/render settings.
Actions: list_lights, get_light, set_light, bake, cancel_bake, get_bake_status,
         list_probes, get_probe, get_environment, set_environment, get_lightmap_settings.
Uses built-in UnityEngine/UnityEditor lighting APIs — no package dependency.
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
        "Unity lighting system management. "
        "Actions: list_lights (all Light components, optional type filter), "
        "get_light (full light properties), "
        "set_light (modify color, intensity, range, shadows), "
        "bake (trigger lightmap bake — async), "
        "cancel_bake (cancel in-progress bake), "
        "get_bake_status (is baking? progress?), "
        "list_probes (light probes + reflection probes), "
        "get_probe (probe detail — type, bounds, mode), "
        "get_environment (RenderSettings — ambient, fog, skybox, sun), "
        "set_environment (modify RenderSettings), "
        "get_lightmap_settings (lightmapper config). "
        "Uses built-in Unity lighting APIs — no package dependency."
    ),
    annotations=ToolAnnotations(
        title="Manage Lighting",
    ),
)
async def manage_lighting(
    ctx: Context,
    action: Annotated[
        Literal[
            "list_lights", "get_light", "set_light",
            "bake", "cancel_bake", "get_bake_status",
            "list_probes", "get_probe",
            "get_environment", "set_environment", "get_lightmap_settings"
        ],
        "Action to perform on Unity lighting."
    ],
    # Target
    target: Annotated[str, "GameObject name or instance ID"] | None = None,
    # Properties
    properties: Annotated[str, "JSON object of properties to set"] | None = None,
    # Filter
    type_filter: Annotated[str, "Light type filter: Directional, Point, Spot, Area"] | None = None,
    # Pagination
    page_size: Annotated[int, "Max results to return (default 50)"] | None = None,
    cursor: Annotated[int, "Pagination cursor (0-based offset)"] | None = None,
) -> dict[str, Any]:
    unity_instance = get_unity_instance_from_context(ctx)

    params = {
        "action": action,
        "target": target,
        "properties": properties,
        "type_filter": type_filter,
        "page_size": page_size,
        "cursor": cursor,
    }
    params = {k: v for k, v in params.items() if v is not None}

    try:
        response = await send_with_unity_instance(
            async_send_command_with_retry, unity_instance, "manage_lighting", params
        )

        if isinstance(response, dict) and response.get("success"):
            return {
                "success": True,
                "message": response.get("message", "Lighting operation successful."),
                "data": response.get("data"),
            }
        return response if isinstance(response, dict) else {"success": False, "message": str(response)}

    except Exception as e:
        return {"success": False, "message": f"Python error managing lighting: {str(e)}"}
