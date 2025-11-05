import os
import sys
import types

# Ensure telemetry is disabled during test collection and execution to avoid
# any background network or thread startup that could slow or block pytest.
os.environ.setdefault("DISABLE_TELEMETRY", "true")
os.environ.setdefault("UNITY_MCP_DISABLE_TELEMETRY", "true")
os.environ.setdefault("MCP_DISABLE_TELEMETRY", "true")

# NOTE: These tests are integration tests for the MCP server Python code.
# They test tools, resources, and utilities without requiring Unity to be running.
# Tests can now import directly from the parent package since they're inside src/
# To run: cd MCPForUnity/UnityMcpServer~/src && uv run pytest tests/integration/ -v

# Stub telemetry modules to avoid file I/O during import of tools package
telemetry = types.ModuleType("telemetry")
def _noop(*args, **kwargs):
    pass
class MilestoneType:
    pass
telemetry.record_resource_usage = _noop
telemetry.record_tool_usage = _noop
telemetry.record_milestone = _noop
telemetry.MilestoneType = MilestoneType
telemetry.get_package_version = lambda: "0.0.0"
sys.modules.setdefault("telemetry", telemetry)

telemetry_decorator = types.ModuleType("telemetry_decorator")
def telemetry_tool(*dargs, **dkwargs):
    def _wrap(fn):
        return fn
    return _wrap
telemetry_decorator.telemetry_tool = telemetry_tool
sys.modules.setdefault("telemetry_decorator", telemetry_decorator)

# Stub fastmcp module (not mcp.server.fastmcp)
fastmcp = types.ModuleType("fastmcp")

class _DummyFastMCP:
    pass

class _DummyContext:
    pass

fastmcp.FastMCP = _DummyFastMCP
fastmcp.Context = _DummyContext
sys.modules.setdefault("fastmcp", fastmcp)
