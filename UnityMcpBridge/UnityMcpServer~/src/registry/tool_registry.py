"""
Tool registry for auto-discovery of MCP tools.
"""
from typing import Callable, Any

# Global registry to collect decorated tools
_tool_registry: list[dict[str, Any]] = []


def mcp_for_unity_tool(
    name: str | None = None,
    description: str | None = None,
    **kwargs
) -> Callable:
    """
    Decorator for registering MCP tools with auto-discovery.

    Automatically applies:
    - Telemetry tracking (if available and enabled)
    - Registration in the global tool registry

    Args:
        name: Tool name (defaults to function name)
        description: Tool description
        enable_telemetry: Whether to enable telemetry for this tool (default: True)
        **kwargs: Additional arguments passed to @mcp.tool()

    Example:
        @mcp_for_unity_tool(description="Does something cool")
        async def my_custom_tool(ctx: Context, ...):
            pass
    """
    def decorator(func: Callable) -> Callable:
        tool_name = name if name is not None else func.__name__
        _tool_registry.append({
            'func': func,
            'name': tool_name,
            'description': description,
            'kwargs': kwargs
        })

        return func

    return decorator


def get_registered_tools() -> list[dict[str, Any]]:
    """Get all registered tools"""
    return _tool_registry.copy()


def clear_registry():
    """Clear the tool registry (useful for testing)"""
    _tool_registry.clear()
