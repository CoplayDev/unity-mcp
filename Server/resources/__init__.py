"""
MCP Resources package - Auto-discovers and registers all resources in this directory.
"""
import logging
import inspect
from pathlib import Path
from typing import get_type_hints

from fastmcp import FastMCP
from telemetry_decorator import telemetry_resource

from registry import get_registered_resources
from module_discovery import discover_modules

logger = logging.getLogger("mcp-for-unity-server")

# Export decorator for easy imports within tools
__all__ = ['register_all_resources']


def _create_fixed_wrapper(original_func, has_other_params):
    """
    Factory function to create a wrapper that calls original_func with unity_instance=None.
    This avoids closure issues in loops and preserves sync/async nature of the original function.
    """
    is_async = inspect.iscoroutinefunction(original_func)

    if has_other_params:
        if is_async:
            async def fixed_wrapper(*args, **kwargs):
                return await original_func(*args, **kwargs, unity_instance=None)
        else:
            def fixed_wrapper(*args, **kwargs):
                return original_func(*args, **kwargs, unity_instance=None)
    else:
        if is_async:
            async def fixed_wrapper():
                return await original_func(unity_instance=None)
        else:
            def fixed_wrapper():
                return original_func(unity_instance=None)

    return fixed_wrapper


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
            # Register two versions for backward compatibility:
            # 1. Template version with query parameters (for multi-instance)
            wrapped_template = telemetry_resource(resource_name)(func)
            wrapped_template = mcp.resource(uri=uri, name=resource_name,
                                           description=description, **kwargs)(wrapped_template)
            logger.debug(f"Registered resource template: {resource_name} - {uri}")
            registered_count += 1

            # 2. Fixed version without query parameters (for single-instance/default)
            # Remove query parameters from URI
            fixed_uri = uri.split('{?')[0]
            fixed_name = f"{resource_name}_default"
            fixed_description = f"{description} (default instance)"

            # Create a wrapper function that doesn't accept unity_instance parameter
            # This wrapper will call the original function with unity_instance=None
            sig = inspect.signature(func)
            params = list(sig.parameters.values())

            # Filter out unity_instance parameter
            fixed_params = [p for p in params if p.name != 'unity_instance']

            # Create wrapper using factory function to avoid closure issues
            has_other_params = len(fixed_params) > 0
            fixed_wrapper = _create_fixed_wrapper(func, has_other_params)

            # Update signature to match filtered parameters
            if has_other_params:
                fixed_wrapper.__signature__ = sig.replace(parameters=fixed_params)
                fixed_wrapper.__annotations__ = {
                    k: v for k, v in func.__annotations__.items()
                    if k != 'unity_instance'
                }
            else:
                fixed_wrapper.__signature__ = inspect.Signature(parameters=[])
                fixed_wrapper.__annotations__ = {
                    k: v for k, v in func.__annotations__.items()
                    if k == 'return'
                }

            # Preserve function metadata
            fixed_wrapper.__name__ = fixed_name
            fixed_wrapper.__doc__ = func.__doc__

            wrapped_fixed = telemetry_resource(fixed_name)(fixed_wrapper)
            wrapped_fixed = mcp.resource(uri=fixed_uri, name=fixed_name,
                                        description=fixed_description, **kwargs)(wrapped_fixed)
            logger.debug(f"Registered resource (fixed): {fixed_name} - {fixed_uri}")
            registered_count += 1

            resource_info['func'] = wrapped_template
        else:
            # No query parameters, register as-is
            wrapped = telemetry_resource(resource_name)(func)
            wrapped = mcp.resource(uri=uri, name=resource_name,
                                   description=description, **kwargs)(wrapped)
            resource_info['func'] = wrapped
            logger.debug(f"Registered resource: {resource_name} - {description}")
            registered_count += 1

    logger.info(f"Registered {registered_count} MCP resources ({len(resources)} unique)")
