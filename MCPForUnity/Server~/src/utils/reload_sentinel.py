"""
Deprecated: Sentinel flipping is handled inside Unity via the MCP menu
'MCP/Flip Reload Sentinel'. This module remains only as a compatibility shim.
All functions are no-ops to prevent accidental external writes.
"""


def flip_reload_sentinel(*args, **kwargs) -> str:
    return "reload_sentinel.py is deprecated; use execute_menu_item → 'MCP/Flip Reload Sentinel'"
