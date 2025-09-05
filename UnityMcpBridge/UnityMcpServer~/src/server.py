from mcp.server.fastmcp import FastMCP, Context, Image
import logging
from dataclasses import dataclass
from contextlib import asynccontextmanager
from typing import AsyncIterator, Dict, Any, List
from config import config
from tools import register_all_tools
from unity_connection import get_unity_connection, UnityConnection

# Configure logging using settings from config
logging.basicConfig(
    level=getattr(logging, config.log_level),
    format=config.log_format
)
logger = logging.getLogger("mcp-for-unity-server")

# Global connection state
_unity_connection: UnityConnection = None


@asynccontextmanager
async def server_lifespan(server: FastMCP) -> AsyncIterator[Dict[str, Any]]:
    """Handle server startup and shutdown."""
    global _unity_connection
    logger.info("MCP for Unity Server starting up")
    try:
        _unity_connection = get_unity_connection()
        logger.info("Connected to Unity on startup")
    except Exception as e:
        logger.warning(f"Could not connect to Unity on startup: {str(e)}")
        _unity_connection = None
    try:
        # Yield the connection object so it can be attached to the context
        # The key 'bridge' matches how tools like read_console expect to access it (ctx.bridge)
        yield {"bridge": _unity_connection}
    finally:
        if _unity_connection:
            _unity_connection.disconnect()
            _unity_connection = None
        logger.info("MCP for Unity Server shut down")

# Initialize MCP server
mcp = FastMCP(
    "mcp-for-unity-server",
    description="Unity Editor integration via Model Context Protocol",
    lifespan=server_lifespan
)

# Register all tools
register_all_tools(mcp)

# Asset Creation Strategy


@mcp.prompt()
def asset_creation_strategy() -> str:
    """Guide for discovering and using MCP for Unity tools effectively."""
    return (
        "Available MCP for Unity Server Tools:\n\n"
        "- `manage_editor`: Controls editor state and queries info.\n"
        "- `manage_menu_item`: Executes and lists Unity Editor menu items.\n"
        "- `read_console`: Reads or clears Unity console messages, with filtering options.\n"
        "- `manage_scene`: Manages scenes.\n"
        "- `manage_gameobject`: Manages GameObjects in the scene.\n"
        "- `manage_script`: Manages C# script files.\n"
        "- `manage_asset`: Manages prefabs and assets.\n"
        "- `manage_shader`: Manages shaders.\n\n"
        "Tips:\n"
        "- Create prefabs for reusable GameObjects.\n"
        "- Always include a camera and main light in your scenes.\n"
        "- Unless specified otherwise, paths are relative to the project's `Assets/` folder.\n"
        "- After creating or modifying scripts with `manage_script`, allow Unity to recompile; use `read_console` to check for compile errors.\n"
        "- Use `manage_menu_item` for interacting with Unity systems and third party tools like a user would.\n"
        "- List menu items before using them if you are unsure of the menu path.\n"
        "- If a menu item seems missing, refresh the cache: use manage_menu_item with action='list' and refresh=true, or action='refresh'. Avoid refreshing every time; prefer refresh only when the menu set likely changed.\n"
    )


# Run the server
if __name__ == "__main__":
    mcp.run(transport='stdio')
