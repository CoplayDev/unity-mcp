"""
Registry package for MCP tool auto-discovery.
"""
from .tool_registry import (
    mcp_for_unity_tool,
    get_registered_tools,
    get_group_tool_names,
    clear_tool_registry,
    TOOL_GROUPS,
    DEFAULT_ENABLED_GROUPS,
)
from .resource_registry import (
    mcp_for_unity_resource,
    get_registered_resources,
    clear_resource_registry,
)

__all__ = [
    'mcp_for_unity_tool',
    'get_registered_tools',
    'get_group_tool_names',
    'clear_tool_registry',
    'TOOL_GROUPS',
    'DEFAULT_ENABLED_GROUPS',
    'mcp_for_unity_resource',
    'get_registered_resources',
    'clear_resource_registry'
]
