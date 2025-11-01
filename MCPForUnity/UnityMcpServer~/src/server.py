from telemetry import record_telemetry, record_milestone, RecordType, MilestoneType
from fastmcp import FastMCP
import logging
from logging.handlers import RotatingFileHandler
import os
from contextlib import asynccontextmanager
from typing import AsyncIterator, Dict, Any
from config import config
from tools import register_all_tools
from resources import register_all_resources
from unity_connection import get_unity_connection, UnityConnection
import time

# Configure logging using settings from config
logging.basicConfig(
    level=getattr(logging, config.log_level),
    format=config.log_format,
    stream=None,  # None -> defaults to sys.stderr; avoid stdout used by MCP stdio
    force=True    # Ensure our handler replaces any prior stdout handlers
)
logger = logging.getLogger("mcp-for-unity-server")

# Also write logs to a rotating file so logs are available when launched via stdio
try:
    import os as _os
    _log_dir = _os.path.join(_os.path.expanduser(
        "~/Library/Application Support/UnityMCP"), "Logs")
    _os.makedirs(_log_dir, exist_ok=True)
    _file_path = _os.path.join(_log_dir, "unity_mcp_server.log")
    _fh = RotatingFileHandler(
        _file_path, maxBytes=512*1024, backupCount=2, encoding="utf-8")
    _fh.setFormatter(logging.Formatter(config.log_format))
    _fh.setLevel(getattr(logging, config.log_level))
    logger.addHandler(_fh)
    # Also route telemetry logger to the same rotating file and normal level
    try:
        tlog = logging.getLogger("unity-mcp-telemetry")
        tlog.setLevel(getattr(logging, config.log_level))
        tlog.addHandler(_fh)
    except Exception:
        # Never let logging setup break startup
        pass
except Exception:
    # Never let logging setup break startup
    pass
# Quieten noisy third-party loggers to avoid clutter during stdio handshake
for noisy in ("httpx", "urllib3"):
    try:
        logging.getLogger(noisy).setLevel(
            max(logging.WARNING, getattr(logging, config.log_level)))
    except Exception:
        pass

# Import telemetry only after logging is configured to ensure its logs use stderr and proper levels
# Ensure a slightly higher telemetry timeout unless explicitly overridden by env
try:

    # Ensure generous timeout unless explicitly overridden by env
    if not os.environ.get("UNITY_MCP_TELEMETRY_TIMEOUT"):
        os.environ["UNITY_MCP_TELEMETRY_TIMEOUT"] = "5.0"
except Exception:
    pass

# Global connection state
_unity_connection: UnityConnection = None


@asynccontextmanager
async def server_lifespan(server: FastMCP) -> AsyncIterator[Dict[str, Any]]:
    """Handle server startup and shutdown."""
    global _unity_connection
    logger.info("MCP for Unity Server starting up")

    # Record server startup telemetry
    start_time = time.time()
    start_clk = time.perf_counter()
    try:
        from pathlib import Path
        ver_path = Path(__file__).parent / "server_version.txt"
        server_version = ver_path.read_text(encoding="utf-8").strip()
    except Exception:
        server_version = "unknown"
    # Defer initial telemetry by 1s to avoid stdio handshake interference
    import threading

    def _emit_startup():
        try:
            record_telemetry(RecordType.STARTUP, {
                "server_version": server_version,
                "startup_time": start_time,
            })
            record_milestone(MilestoneType.FIRST_STARTUP)
        except Exception:
            logger.debug("Deferred startup telemetry failed", exc_info=True)
    threading.Timer(1.0, _emit_startup).start()

    try:
        skip_connect = os.environ.get(
            "UNITY_MCP_SKIP_STARTUP_CONNECT", "").lower() in ("1", "true", "yes", "on")
        if skip_connect:
            logger.info(
                "Skipping Unity connection on startup (UNITY_MCP_SKIP_STARTUP_CONNECT=1)")
        else:
            _unity_connection = get_unity_connection()
            logger.info("Connected to Unity on startup")

            # Record successful Unity connection (deferred)
            import threading as _t
            _t.Timer(1.0, lambda: record_telemetry(
                RecordType.UNITY_CONNECTION,
                {
                    "status": "connected",
                    "connection_time_ms": (time.perf_counter() - start_clk) * 1000,
                }
            )).start()

    except ConnectionError as e:
        logger.warning("Could not connect to Unity on startup: %s", e)
        _unity_connection = None

        # Record connection failure (deferred)
        import threading as _t
        _err_msg = str(e)[:200]
        _t.Timer(1.0, lambda: record_telemetry(
            RecordType.UNITY_CONNECTION,
            {
                "status": "failed",
                "error": _err_msg,
                "connection_time_ms": (time.perf_counter() - start_clk) * 1000,
            }
        )).start()
    except Exception as e:
        logger.warning(
            "Unexpected error connecting to Unity on startup: %s", e)
        _unity_connection = None
        import threading as _t
        _err_msg = str(e)[:200]
        _t.Timer(1.0, lambda: record_telemetry(
            RecordType.UNITY_CONNECTION,
            {
                "status": "failed",
                "error": _err_msg,
                "connection_time_ms": (time.perf_counter() - start_clk) * 1000,
            }
        )).start()

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
    name="mcp-for-unity-server",
    lifespan=server_lifespan,
    instructions="""
This server provides tools to interact with the Unity Game Engine Editor.\n\n
Available tools:\n
- `manage_editor`: Controls editor state and queries info.\n
- `execute_menu_item`: Executes, lists and checks for the existence of Unity Editor menu items.\n
- `read_console`: Reads or clears Unity console messages, with filtering options.\n
- `manage_scene`: Manages scenes.\n
- `manage_gameobject`: Manages GameObjects in the scene.\n
- `manage_script`: Manages C# script files.\n
- `manage_asset`: Manages prefabs and assets.\n
- `manage_shader`: Manages shaders.\n\n
- Tips:\n
- Create prefabs for reusable GameObjects.\n
- Always include a camera and main light in your scenes.\n
- Unless specified otherwise, paths are relative to the project's `Assets/` folder.\n
- After creating or modifying scripts with `manage_script`, allow Unity to recompile; use `read_console` to check for compile errors.\n
- Use `execute_menu_item` for interacting with Unity systems and third party tools like a user would.\n

"""
)

# Register all tools
register_all_tools(mcp)

# Register all resources
register_all_resources(mcp)


def main():
    """Entry point for uvx and console scripts."""
    mcp.run(transport='stdio')


# Run the server
if __name__ == "__main__":
    main()
