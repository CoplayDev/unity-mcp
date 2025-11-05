from telemetry import record_telemetry, record_milestone, RecordType, MilestoneType
from fastmcp import FastMCP
import logging
from logging.handlers import RotatingFileHandler
import os
import argparse
from contextlib import asynccontextmanager
from typing import AsyncIterator, Dict, Any
from config import config
from tools import register_all_tools
from resources import register_all_resources
from unity_connection import get_unity_connection_pool, UnityConnectionPool
from unity_instance_middleware import UnityInstanceMiddleware, set_unity_instance_middleware
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

# Global connection pool
_unity_connection_pool: UnityConnectionPool = None


@asynccontextmanager
async def server_lifespan(server: FastMCP) -> AsyncIterator[Dict[str, Any]]:
    """Handle server startup and shutdown."""
    global _unity_connection_pool
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
            # Initialize connection pool and discover instances
            _unity_connection_pool = get_unity_connection_pool()
            instances = _unity_connection_pool.discover_all_instances()

            if instances:
                logger.info(f"Discovered {len(instances)} Unity instance(s): {[i.id for i in instances]}")

                # Try to connect to default instance
                try:
                    _unity_connection_pool.get_connection()
                    logger.info("Connected to default Unity instance on startup")

                    # Record successful Unity connection (deferred)
                    import threading as _t
                    _t.Timer(1.0, lambda: record_telemetry(
                        RecordType.UNITY_CONNECTION,
                        {
                            "status": "connected",
                            "connection_time_ms": (time.perf_counter() - start_clk) * 1000,
                            "instance_count": len(instances)
                        }
                    )).start()
                except Exception as e:
                    logger.warning("Could not connect to default Unity instance: %s", e)
            else:
                logger.warning("No Unity instances found on startup")

    except ConnectionError as e:
        logger.warning("Could not connect to Unity on startup: %s", e)

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
        # Yield the connection pool so it can be attached to the context
        # Note: Tools will use get_unity_connection_pool() directly
        yield {"pool": _unity_connection_pool}
    finally:
        if _unity_connection_pool:
            _unity_connection_pool.disconnect_all()
        logger.info("MCP for Unity Server shut down")

# Initialize MCP server
mcp = FastMCP(
    name="mcp-for-unity-server",
    lifespan=server_lifespan,
    instructions="""
This server provides tools to interact with the Unity Game Engine Editor.

Important Workflows:

Resources vs Tools:
- Use RESOURCES to read editor state (editor_state, project_info, project_tags, tests, etc)
- Use TOOLS to perform actions and mutations (manage_editor for play mode control, tag/layer management, etc)
- Always check related resources before modifying the engine state with tools

Script Management:
- After creating or modifying scripts (by your own tools or the `manage_script` tool) use `read_console` to check for compilation errors before proceeding
- Only after successful compilation can new components/types be used
- You can poll the `editor_state` resource's `isCompiling` field to check if the domain reload is complete

Scene Setup:
- Always include a Camera and main Light (Directional Light) in new scenes
- Create prefabs with `manage_asset` for reusable GameObjects
- Use `manage_scene` to load, save, and query scene information

Path Conventions:
- Unless specified otherwise, all paths are relative to the project's `Assets/` folder
- Use forward slashes (/) in paths for cross-platform compatibility

Console Monitoring:
- Check `read_console` regularly to catch errors, warnings, and compilation status
- Filter by log type (Error, Warning, Log) to focus on specific issues

Menu Items:
- Use `execute_menu_item` when you have read the menu items resource
- This lets you interact with Unity's menu system and third-party tools
"""
)

# Initialize and register middleware for session-based Unity instance routing
unity_middleware = UnityInstanceMiddleware()
set_unity_instance_middleware(unity_middleware)
mcp.add_middleware(unity_middleware)
logger.info("Registered Unity instance middleware for session-based routing")

# Register all tools
register_all_tools(mcp)

# Register all resources
register_all_resources(mcp)


def main():
    """Entry point for uvx and console scripts."""
    parser = argparse.ArgumentParser(
        description="MCP for Unity Server",
        formatter_class=argparse.RawDescriptionHelpFormatter,
        epilog="""
Environment Variables:
  UNITY_MCP_DEFAULT_INSTANCE   Default Unity instance to target (project name, hash, or 'Name@hash')
  UNITY_MCP_SKIP_STARTUP_CONNECT   Skip initial Unity connection attempt (set to 1/true/yes/on)
  UNITY_MCP_TELEMETRY_ENABLED   Enable telemetry (set to 1/true/yes/on)

Examples:
  # Use specific Unity project as default
  python -m src.server --default-instance "MyProject"

  # Or use environment variable
  UNITY_MCP_DEFAULT_INSTANCE="MyProject" python -m src.server
        """
    )
    parser.add_argument(
        "--default-instance",
        type=str,
        metavar="INSTANCE",
        help="Default Unity instance to target (project name, hash, or 'Name@hash'). "
             "Overrides UNITY_MCP_DEFAULT_INSTANCE environment variable."
    )

    args = parser.parse_args()

    # Set environment variable if --default-instance is provided
    if args.default_instance:
        os.environ["UNITY_MCP_DEFAULT_INSTANCE"] = args.default_instance
        logger.info(f"Using default Unity instance from command-line: {args.default_instance}")

    mcp.run(transport='stdio')


# Run the server
if __name__ == "__main__":
    main()
