"""
Tool registry for auto-discovery of MCP tools.
"""
from typing import Callable, Any

# Global registry to collect decorated tools
_tool_registry: list[dict[str, Any]] = []


def mcp_for_unity_tool(
    name: str | None = None,
    description: str | None = None,
    unity_target: str | None = "self",
    **kwargs
) -> Callable:
    """
    Decorator for registering MCP tools in the server's tools directory.

    Tools are registered in the global tool registry.

    Args:
        name: Tool name (defaults to function name)
        description: Tool description
        unity_target: Visibility target used by middleware filtering.
            - "self" (default): tool follows its own enabled state.
            - None: server-only tool, always visible in tool listing.
            - "<tool_name>": alias tool that follows another Unity tool state.
        **kwargs: Additional arguments passed to @mcp.tool()

    Example:
        @mcp_for_unity_tool(description="Does something cool")
        async def my_custom_tool(ctx: Context, ...):
            pass
    """
    def decorator(func: Callable) -> Callable:
        tool_name = name if name is not None else func.__name__
        # Safety guard: unity_target is internal metadata and must never leak into mcp.tool kwargs.
        tool_kwargs = dict(kwargs)  # Create a copy to avoid side effects
        if "unity_target" in tool_kwargs:
            del tool_kwargs["unity_target"]

        if unity_target is None:
            normalized_unity_target: str | None = None
        elif isinstance(unity_target, str) and unity_target.strip():
            normalized_unity_target = (
                tool_name if unity_target == "self" else unity_target.strip()
            )
        else:
            raise ValueError(
                f"Invalid unity_target for tool '{tool_name}': {unity_target!r}. "
                "Expected None or a non-empty string."
            )

        _tool_registry.append({
            'func': func,
            'name': tool_name,
            'description': description,
            'unity_target': normalized_unity_target,
            'kwargs': tool_kwargs,
        })

        return func

    return decorator


def get_registered_tools() -> list[dict[str, Any]]:
    """Get all registered tools"""
    return _tool_registry.copy()


def clear_tool_registry():
    """Clear the tool registry (useful for testing)"""
    _tool_registry.clear()
