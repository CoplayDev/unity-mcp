"""
MCP Resources package - Auto-discovers and registers all resources in this directory.
"""
import inspect
import logging
from pathlib import Path

from fastmcp import FastMCP
from core.telemetry_decorator import telemetry_resource
from core.logging_decorator import log_execution

from services.registry import get_registered_resources
from utils.module_discovery import discover_modules

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

    registered_count = 0
    for resource_info in resources:
        func = resource_info['func']
        uri = resource_info['uri']
        resource_name = resource_info['name']
        description = resource_info['description']
        kwargs = resource_info['kwargs']

        # Check if URI contains query parameters (e.g., {?unity_instance})
        has_query_params = '{?' in uri

        if has_query_params:
            wrapped_template = log_execution(resource_name, "Resource")(func)
            wrapped_template = telemetry_resource(
                resource_name)(wrapped_template)
            wrapped_template = mcp.resource(
                uri=uri,
                name=resource_name,
                description=description,
                **kwargs,
            )(wrapped_template)
            logger.debug(
                f"Registered resource template: {resource_name} - {uri}")
            registered_count += 1
            resource_info['func'] = wrapped_template
        else:
            wrapped = log_execution(resource_name, "Resource")(func)
            wrapped = telemetry_resource(resource_name)(wrapped)
            wrapped = mcp.resource(
                uri=uri,
                name=resource_name,
                description=description,
                **kwargs,
            )(wrapped)
            resource_info['func'] = wrapped
            logger.debug(
                f"Registered resource: {resource_name} - {description}")
            registered_count += 1

    logger.info(
        f"Registered {registered_count} MCP resources ({len(resources)} unique)")
