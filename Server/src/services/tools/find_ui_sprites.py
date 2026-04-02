"""
Defines the find_ui_sprites tool for discovering UI assets with metadata like 9-slicing.
"""
import asyncio
from typing import Annotated, Any

from fastmcp import Context

from services.registry import mcp_for_unity_tool
from services.tools import get_unity_instance_from_context
from transport.unity_transport import send_with_unity_instance
from transport.legacy.unity_connection import async_send_command_with_retry


@mcp_for_unity_tool(
    group="core",
    description=(
        "Search for UI Sprite assets in the Unity project. Returns metadata like 9-slicing (borders) and dimensions.\n"
        "Use this for the Vision-to-UI workflow to map reference images to existing project assets."
    ),
)
async def find_ui_sprites(
    ctx: Context,
    search_pattern: Annotated[str, "Optional name or keyword to search for (e.g. 'button')."] = "",
    path: Annotated[str, "Optional directory scope (defaults to 'Assets')."] = "Assets",
    only_9_slice: Annotated[bool, "If true, only returns sprites with 9-slicing borders configured."] = False,
    limit: Annotated[int, "Maximum number of results to return."] = 20,
) -> dict[str, Any]:
    unity_instance = await get_unity_instance_from_context(ctx)

    params_dict: dict[str, Any] = {
        "searchPattern": search_pattern,
        "path": path,
        "only9Slice": only_9_slice,
        "limit": int(limit),
    }

    # Get the current asyncio event loop
    loop = asyncio.get_running_loop()

    # Use centralized async retry helper with instance routing
    return await send_with_unity_instance(
        async_send_command_with_retry, 
        unity_instance, 
        "find_ui_sprites", 
        params_dict, 
        loop=loop
    )
