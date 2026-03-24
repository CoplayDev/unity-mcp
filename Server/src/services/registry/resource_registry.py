"""
Resource registry for auto-discovery of MCP resources.
"""
from typing import Callable, Any

from .unity_targeting import (
    add_optional_unity_instance_parameter,
    append_unity_instance_query_template,
)

# Global registry to collect decorated resources
_resource_registry: list[dict[str, Any]] = []


def mcp_for_unity_resource(
    uri: str,
    name: str | None = None,
    description: str | None = None,
    unity_target: bool = True,
    **kwargs
) -> Callable:
    """
    Decorator for registering MCP resources in the server's resources directory.

    Resources are registered in the global resource registry.

    Args:
        name: Resource name (defaults to function name)
        description: Resource description
        **kwargs: Additional arguments passed to @mcp.resource()

    Example:
        @mcp_for_unity_resource("mcpforunity://resource", description="Gets something interesting")
        async def my_custom_resource(ctx: Context, ...):
            pass
    """
    def decorator(func: Callable) -> Callable:
        resource_name = name if name is not None else func.__name__
        registered_func = func
        registered_uri = uri
        if unity_target:
            registered_func = add_optional_unity_instance_parameter(func)
            registered_uri = append_unity_instance_query_template(uri)
        _resource_registry.append({
            'func': registered_func,
            'uri': registered_uri,
            'name': resource_name,
            'description': description,
            'unity_target': unity_target,
            'kwargs': kwargs
        })

        return registered_func

    return decorator


def get_registered_resources() -> list[dict[str, Any]]:
    """Get all registered resources"""
    return _resource_registry.copy()


def clear_resource_registry():
    """Clear the resource registry (useful for testing)"""
    _resource_registry.clear()
