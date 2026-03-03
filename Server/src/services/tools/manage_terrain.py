"""
Tool for Unity Terrain inspection and modification.
Actions: get_info, get_height, set_heights, flatten,
         get_splat_weights, paint_texture, get_heightmap_sample
Works with any Unity scene containing a Terrain component.
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
        "Inspect and modify Unity Terrain at runtime or in Edit mode. "
        "Actions: get_info (heightmap resolution, size, layer/tree counts), "
        "get_height (sample world-space height at x/z), "
        "set_heights (paint circular brush with set/raise/lower/smooth modes), "
        "flatten (set entire heightmap to uniform normalized height), "
        "get_splat_weights (texture layer weights at world position), "
        "paint_texture (paint terrain texture layer with circular brush), "
        "get_heightmap_sample (read NxN heightmap patch around world position). "
        "All actions accept an optional 'target' param (GameObject name or instance ID) "
        "to select a specific Terrain; defaults to the active terrain in the scene."
    ),
    annotations=ToolAnnotations(
        title="Manage Terrain",
    ),
)
async def manage_terrain(
    ctx: Context,
    action: Annotated[
        Literal[
            "get_info", "get_height", "set_heights", "flatten",
            "get_splat_weights", "paint_texture", "get_heightmap_sample"
        ],
        "Action to perform on the Terrain."
    ],
    # Target selection
    target: Annotated[
        str,
        "GameObject name or instance ID of the Terrain. Defaults to the active terrain."
    ] | None = None,
    # World-space position
    x: Annotated[float, "World-space X coordinate (for get_height, set_heights, get_splat_weights, paint_texture, get_heightmap_sample)"] | None = None,
    z: Annotated[float, "World-space Z coordinate (for get_height, set_heights, get_splat_weights, paint_texture, get_heightmap_sample)"] | None = None,
    # Brush params
    radius: Annotated[float, "Brush radius in world units (for set_heights, paint_texture)"] | None = None,
    height: Annotated[float, "Normalized height 0-1 (for set_heights, flatten)"] | None = None,
    mode: Annotated[
        Literal["set", "raise", "lower", "smooth"],
        "Brush mode for set_heights (default: set)"
    ] | None = None,
    # Texture painting
    layer_index: Annotated[int, "Terrain layer index to paint (for paint_texture)"] | None = None,
    strength: Annotated[float, "Paint strength 0-1 (for paint_texture)"] | None = None,
    # Heightmap sample
    size: Annotated[int, "Patch size NxN in heightmap pixels, clamped to 64 (for get_heightmap_sample)"] | None = None,
) -> dict[str, Any]:
    unity_instance = get_unity_instance_from_context(ctx)

    params = {
        "action": action,
        "target": target,
        "x": x,
        "z": z,
        "radius": radius,
        "height": height,
        "mode": mode,
        "layer_index": layer_index,
        "strength": strength,
        "size": size,
    }
    params = {k: v for k, v in params.items() if v is not None}

    try:
        response = await send_with_unity_instance(
            async_send_command_with_retry, unity_instance, "manage_terrain", params
        )

        if isinstance(response, dict) and response.get("success"):
            return {
                "success": True,
                "message": response.get("message", "Terrain operation successful."),
                "data": response.get("data"),
            }
        return response if isinstance(response, dict) else {"success": False, "message": str(response)}

    except Exception as e:
        return {"success": False, "message": f"Python error managing terrain: {str(e)}"}
