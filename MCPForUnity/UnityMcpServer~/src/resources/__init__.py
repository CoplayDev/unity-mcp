"""
MCP Resources package - Auto-discovers and registers all resources in this directory.
"""
import logging
from pathlib import Path

from fastmcp import FastMCP
from telemetry_decorator import telemetry_resource

from registry import get_registered_resources
from module_discovery import discover_modules

logger = logging.getLogger("mcp-for-unity-server")

# Export decorator for easy imports within tools
__all__ = ['register_all_resources']


def register_all_resources(mcp: FastMCP):
    """
    Auto-discover and register all resources in the resources/ directory.

    Any .py file in this directory or subdirectories with @mcp_for_unity_resource decorated
    functions will be automatically registered.
    """
    logger.info("Auto-discovering MCP for Unity Server resources...")
    # Dynamic import of all modules in this directory
    resources_dir = Path(__file__).parent

    # Discover and import all modules
    list(discover_modules(resources_dir, __package__))

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
        wrapped = telemetry_resource(resource_name)(func)
        wrapped = mcp.resource(uri=uri, name=resource_name,
                               description=description, **kwargs)(wrapped)
        resource_info['func'] = wrapped
        logger.debug(f"Registered resource: {resource_name} - {description}")

    logger.info(f"Registered {len(resources)} MCP resources")
