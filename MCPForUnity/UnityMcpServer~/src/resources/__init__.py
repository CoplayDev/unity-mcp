"""
MCP Resources package - Auto-discovers and registers all resources in this directory.
"""
import importlib
import logging
from pathlib import Path
import pkgutil

from mcp.server.fastmcp import FastMCP
from telemetry_decorator import telemetry_resource

from registry import get_registered_resources

logger = logging.getLogger("mcp-for-unity-server")

# Export decorator for easy imports within tools
__all__ = ['register_all_resources']


def register_all_resources(mcp: FastMCP):
    """
    Auto-discover and register all resources in the resources/ directory.

    Any .py file in this directory with @mcp_for_unity_resource decorated
    functions will be automatically registered.
    """
    logger.info("Auto-discovering MCP for Unity Server resources...")
    # Dynamic import of all modules in this directory
    resources_dir = Path(__file__).parent

    for _, module_name, _ in pkgutil.iter_modules([str(resources_dir)]):
        # Skip private modules and __init__
        if module_name.startswith('_'):
            continue

        try:
            importlib.import_module(f'.{module_name}', __package__)
        except Exception as e:
            logger.warning(
                f"Failed to import resource module {module_name}: {e}")

    resources = get_registered_resources()

    if not resources:
        logger.warning("No MCP resources registered!")
        return

    for resource_info in resources:
        func = resource_info['func']
        uri = resource_info['uri']
        resource_name = resource_info['name']
        description = resource_info['description']
        kwargs = resource_info['kwargs']

        # Apply the @mcp.resource decorator and telemetry
        wrapped = mcp.resource(uri=uri, name=resource_name,
                               description=description, **kwargs)(func)
        wrapped = telemetry_resource(resource_name)(wrapped)
        resource_info['func'] = wrapped
        logger.debug(f"Registered resource: {resource_name} - {description}")

    logger.info(f"Registered {len(resources)} MCP resources")
