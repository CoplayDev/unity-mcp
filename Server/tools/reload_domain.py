from fastmcp import Context
from models import MCPResponse
from registry import mcp_for_unity_tool
from unity_connection import async_send_command_with_retry


@mcp_for_unity_tool(
    description="Trigger a Unity domain reload to recompile scripts and refresh assemblies. Essential after creating or modifying scripts before new components can be used."
)
async def reload_domain(ctx: Context) -> MCPResponse:
    """
    Request Unity to reload its domain (script assemblies).
    This is necessary after:

    - Creating new C# scripts
    - Modifying existing scripts
    - Before attempting to add new components to GameObjects

    Returns immediately after triggering the reload request.
    Unity will handle the actual recompilation asynchronously.
    """
    await ctx.info("Requesting Unity domain reload")
    result = await async_send_command_with_retry("reload_domain", {})
    return MCPResponse(**result) if isinstance(result, dict) else result
