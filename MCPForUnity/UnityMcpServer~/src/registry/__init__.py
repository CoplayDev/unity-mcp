"""
Registry package for MCP tool auto-discovery.
"""
from .tool_registry import (
    mcp_for_unity_tool,
    get_registered_tools,
    clear_tool_registry,
)
from .resource_registry import (
    mcp_for_unity_resource,
    get_registered_resources,
    clear_resource_registry,
)

__all__ = [
    'mcp_for_unity_tool',
    'get_registered_tools',
    'clear_tool_registry',
    'mcp_for_unity_resource',
    'get_registered_resources',
    'clear_resource_registry'
]
