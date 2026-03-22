"""
MCP tool for targeted asset reimport — reimport specific assets or folders
without the slow "Reimport All" dialog. AI-friendly, no confirmation popups.
"""
from typing import Annotated, Any

from fastmcp import Context
from mcp.types import ToolAnnotations

from services.registry import mcp_for_unity_tool
from services.tools import get_unity_instance_from_context
from transport.unity_transport import send_with_unity_instance
from transport.legacy.unity_connection import async_send_command_with_retry


@mcp_for_unity_tool(
    description=(
        "Reimport specific Unity assets by path. Faster and more granular than "
        "'Reimport All'. No confirmation dialog — works seamlessly in automated "
        "workflows. Supports individual files and recursive folder reimport."
    ),
    annotations=ToolAnnotations(
        title="Reimport Assets",
        destructiveHint=True,
    ),
)
async def reimport_assets(
    ctx: Context,
    paths: Annotated[
        list[str],
        "Asset paths to reimport (e.g. ['Assets/Prefabs/Unit.prefab', 'Assets/Shaders/'])."
    ],
    force: Annotated[
        bool,
        "Use ForceUpdate import option. Default: true."
    ] = True,
    recursive: Annotated[
        bool,
        "Recursively reimport all assets in folders. Default: true."
    ] = True,
) -> dict[str, Any]:
    unity_instance = await get_unity_instance_from_context(ctx)
    params = {
        "paths": paths,
        "force": force,
        "recursive": recursive,
    }
    response = await send_with_unity_instance(
        async_send_command_with_retry,
        unity_instance,
        "reimport_assets",
        params,
    )
    return response
