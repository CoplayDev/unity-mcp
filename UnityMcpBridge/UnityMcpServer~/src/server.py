from mcp.server.fastmcp import FastMCP, Context, Image
import logging
from dataclasses import dataclass
from contextlib import asynccontextmanager
from typing import AsyncIterator, Dict, Any, List
from config import config
from tools import register_all_tools
from unity_connection import get_unity_connection, UnityConnection
from telemetry import record_telemetry, record_milestone, RecordType, MilestoneType
import time

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
    
    # Record server startup telemetry
    start_time = time.time()
    record_telemetry(RecordType.STARTUP, {
        "server_version": "3.0.2",
        "startup_time": start_time
    })
    
    # Record first startup milestone
    record_milestone(MilestoneType.FIRST_STARTUP)
    
    try:
        _unity_connection = get_unity_connection()
        logger.info("Connected to Unity on startup")
        
        # Record successful Unity connection
        record_telemetry(RecordType.UNITY_CONNECTION, {
            "status": "connected",
            "connection_time_ms": (time.time() - start_time) * 1000
        })
        
    except Exception as e:
        logger.warning(f"Could not connect to Unity on startup: {str(e)}")
        _unity_connection = None
        
        # Record connection failure
        record_telemetry(RecordType.UNITY_CONNECTION, {
            "status": "failed",
            "error": str(e)[:200],
            "connection_time_ms": (time.time() - start_time) * 1000
        })
        
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
        "Available MCP for Unity Server Tools:\\n\\n"
        "- `manage_editor`: Controls editor state and queries info.\\n"
        "- `execute_menu_item`: Executes Unity Editor menu items by path.\\n"
        "- `read_console`: Reads or clears Unity console messages, with filtering options.\\n"
        "- `manage_scene`: Manages scenes.\\n"
        "- `manage_gameobject`: Manages GameObjects in the scene.\\n"
        "- `manage_script`: Manages C# script files.\\n"
        "- `manage_asset`: Manages prefabs and assets.\\n"
        "- `manage_shader`: Manages shaders.\\n\\n"
        "Tips:\\n"
        "- Create prefabs for reusable GameObjects.\\n"
        "- Always include a camera and main light in your scenes.\\n"
    )

# Run the server
if __name__ == "__main__":
    mcp.run(transport='stdio')
