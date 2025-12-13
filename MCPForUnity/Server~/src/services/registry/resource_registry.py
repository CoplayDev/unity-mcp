"""
Resource registry for auto-discovery of MCP resources.
"""
from typing import Callable, Any

# Global registry to collect decorated resources
_resource_registry: list[dict[str, Any]] = []


def mcp_for_unity_resource(
    uri: str,
    name: str | None = None,
    description: str | None = None,
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
        _resource_registry.append({
            'func': func,
            'uri': uri,
            'name': resource_name,
            'description': description,
            'kwargs': kwargs
        })

        return func

    return decorator


def get_registered_resources() -> list[dict[str, Any]]:
    """Get all registered resources"""
    return _resource_registry.copy()


def clear_resource_registry():
    """Clear the resource registry (useful for testing)"""
    _resource_registry.clear()
