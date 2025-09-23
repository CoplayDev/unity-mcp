import logging
from .manage_script_edits import register_manage_script_edits_tools
from .manage_script import register_manage_script_tools
from .manage_scene import register_manage_scene_tools
from .manage_editor import register_manage_editor_tools
from .manage_gameobject import register_manage_gameobject_tools
from .manage_asset import register_manage_asset_tools
from .manage_prefabs import register_manage_prefabs_tools
from .manage_shader import register_manage_shader_tools
from .read_console import register_read_console_tools
from .manage_menu_item import register_manage_menu_item_tools
from .resource_tools import register_resource_tools

logger = logging.getLogger("mcp-for-unity-server")

def register_all_tools(mcp):
    """Register all refactored tools with the MCP server."""
    # Prefer the surgical edits tool so LLMs discover it first
    logger.info("Registering MCP for Unity Server refactored tools...")
    register_manage_script_edits_tools(mcp)
    register_manage_script_tools(mcp)
    register_manage_scene_tools(mcp)
    register_manage_editor_tools(mcp)
    register_manage_gameobject_tools(mcp)
    register_manage_asset_tools(mcp)
    register_manage_prefabs_tools(mcp)
    register_manage_shader_tools(mcp)
    register_read_console_tools(mcp)
    register_manage_menu_item_tools(mcp)
    register_resource_tools(mcp)
    logger.info("MCP for Unity Server tool registration complete.")
