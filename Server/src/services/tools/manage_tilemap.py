"""
Tool for Unity 2D Tilemap — inspect, place/remove tiles, fill areas.
Actions: list_tilemaps, get_info, get_tile, set_tile, clear_tile, clear_all, get_bounds, fill_area.
Requires com.unity.2d.tilemap package.
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
        "Unity 2D Tilemap operations. "
        "Actions: list_tilemaps (all Tilemap components), "
        "get_info (size, cell layout, tile count), "
        "get_tile (tile at position), "
        "set_tile (place tile), "
        "clear_tile (remove tile), "
        "clear_all (clear entire tilemap), "
        "get_bounds (used tile bounds), "
        "fill_area (fill rectangle with tile). "
        "Requires com.unity.2d.tilemap package."
    ),
    annotations=ToolAnnotations(title="Manage Tilemap"),
)
async def manage_tilemap(
    ctx: Context,
    action: Annotated[
        Literal[
            "list_tilemaps", "get_info", "get_tile", "set_tile",
            "clear_tile", "clear_all", "get_bounds", "fill_area"
        ],
        "Action to perform on Unity Tilemap."
    ],
    target: Annotated[str, "GameObject name or instance ID with Tilemap component"] | None = None,
    position: Annotated[str, "Cell position as 'x,y,z'"] | None = None,
    tile_asset: Annotated[str, "Asset path to TileBase asset"] | None = None,
    min: Annotated[str, "Min cell position as 'x,y,z' (for fill_area)"] | None = None,
    max: Annotated[str, "Max cell position as 'x,y,z' (for fill_area)"] | None = None,
    page_size: Annotated[int, "Max results to return (default 50)"] | None = None,
    cursor: Annotated[int, "Pagination cursor (0-based offset)"] | None = None,
) -> dict[str, Any]:
    unity_instance = get_unity_instance_from_context(ctx)

    params = {"action": action, "target": target, "position": position,
              "tile_asset": tile_asset, "min": min, "max": max,
              "page_size": page_size, "cursor": cursor}
    params = {k: v for k, v in params.items() if v is not None}

    try:
        response = await send_with_unity_instance(
            async_send_command_with_retry, unity_instance, "manage_tilemap", params)
        if isinstance(response, dict) and response.get("success"):
            return {"success": True, "message": response.get("message", "OK"), "data": response.get("data")}
        return response if isinstance(response, dict) else {"success": False, "message": str(response)}
    except Exception as e:
        return {"success": False, "message": f"Python error managing tilemap: {str(e)}"}
