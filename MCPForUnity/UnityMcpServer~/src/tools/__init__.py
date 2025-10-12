"""
MCP Tools package - Auto-discovers and registers all tools in this directory.
"""
import importlib
import logging
from pathlib import Path
import pkgutil

from mcp.server.fastmcp import FastMCP
from telemetry_decorator import telemetry_tool

from registry import get_registered_tools

logger = logging.getLogger("mcp-for-unity-server")

# Export decorator for easy imports within tools
__all__ = ['register_all_tools']


def register_all_tools(mcp: FastMCP):
    """
    Auto-discover and register all tools in the tools/ directory.

    Any .py file in this directory with @mcp_for_unity_tool decorated
    functions will be automatically registered.
    """
    logger.info("Auto-discovering MCP for Unity Server tools...")
    # Dynamic import of all modules in this directory
    tools_dir = Path(__file__).parent

    for _, module_name, _ in pkgutil.iter_modules([str(tools_dir)]):
        # Skip private modules and __init__
        if module_name.startswith('_'):
            continue

        try:
            importlib.import_module(f'.{module_name}', __package__)
        except Exception as e:
            logger.warning(f"Failed to import tool module {module_name}: {e}")

    tools = get_registered_tools()

    if not tools:
        logger.warning("No MCP tools registered!")
        return

    for tool_info in tools:
        func = tool_info['func']
        tool_name = tool_info['name']
        description = tool_info['description']
        kwargs = tool_info['kwargs']

        # Apply the @mcp.tool decorator and telemetry
        wrapped = telemetry_tool(tool_name)(func)
        wrapped = mcp.tool(
            name=tool_name, description=description, **kwargs)(wrapped)
        tool_info['func'] = wrapped
        logger.debug(f"Registered tool: {tool_name} - {description}")

    logger.info(f"Registered {len(tools)} MCP tools")
