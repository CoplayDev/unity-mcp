"""
MCP Tools package - Auto-discovers and registers all tools in this directory.
"""
import importlib
import logging
from pathlib import Path
import pkgutil

from mcp.server.fastmcp import FastMCP
from telemetry_decorator import telemetry_tool

from registry import get_registered_tools, mcp_for_unity_tool

logger = logging.getLogger("mcp-for-unity-server")

# Export decorator for easy imports within tools
__all__ = ['register_all_tools', 'mcp_for_unity_tool']


def register_all_tools(mcp: FastMCP):
    """
    Auto-discover and register all tools in the tools/ directory.

    Any .py file in this directory or subdirectories with @mcp_for_unity_tool decorated
    functions will be automatically registered.
    """
    logger.info("Auto-discovering MCP for Unity Server tools...")
    # Dynamic import of all modules in this directory
    tools_dir = Path(__file__).parent

    # Discover modules in the top level
    for _, module_name, _ in pkgutil.iter_modules([str(tools_dir)]):
        # Skip private modules and __init__
        if module_name.startswith('_'):
            continue

        try:
            importlib.import_module(f'.{module_name}', __package__)
        except Exception as e:
            logger.warning(f"Failed to import tool module {module_name}: {e}")

    # Discover modules in subdirectories (one level deep)
    for subdir in tools_dir.iterdir():
        if not subdir.is_dir() or subdir.name.startswith('_') or subdir.name.startswith('.'):
            continue

        # Check if subdirectory contains Python modules
        for _, module_name, _ in pkgutil.iter_modules([str(subdir)]):
            # Skip private modules and __init__
            if module_name.startswith('_'):
                continue

            try:
                # Import as tools.subdirname.modulename
                full_module_name = f'.{subdir.name}.{module_name}'
                importlib.import_module(full_module_name, __package__)
            except Exception as e:
                logger.warning(f"Failed to import tool module {subdir.name}.{module_name}: {e}")

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
        wrapped = mcp.tool(
            name=tool_name, description=description, **kwargs)(func)
        wrapped = telemetry_tool(tool_name)(wrapped)
        tool_info['func'] = wrapped
        logger.info(f"Registered tool: {tool_name} - {description}")

    logger.info(f"Registered {len(tools)} MCP tools")
