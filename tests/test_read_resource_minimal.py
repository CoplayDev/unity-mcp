import sys
import pathlib
import asyncio
import types
import pytest

ROOT = pathlib.Path(__file__).resolve().parents[1]
SRC = ROOT / "MCPForUnity" / "UnityMcpServer~" / "src"
sys.path.insert(0, str(SRC))

# Stub mcp.server.fastmcp to satisfy imports without full package
mcp_pkg = types.ModuleType("mcp")
server_pkg = types.ModuleType("mcp.server")
fastmcp_pkg = types.ModuleType("mcp.server.fastmcp")


class _Dummy:
    pass


fastmcp_pkg.FastMCP = _Dummy
fastmcp_pkg.Context = _Dummy
server_pkg.fastmcp = fastmcp_pkg
mcp_pkg.server = server_pkg
sys.modules.setdefault("mcp", mcp_pkg)
sys.modules.setdefault("mcp.server", server_pkg)
sys.modules.setdefault("mcp.server.fastmcp", fastmcp_pkg)


class DummyMCP:
    def __init__(self):
        self.tools = {}

    def tool(self, *args, **kwargs):
        def deco(fn):
            self.tools[fn.__name__] = fn
            return fn
        return deco


from tests.test_helpers import DummyContext


@pytest.fixture()
def resource_tools():
    mcp = DummyMCP()
    # Import the tools module to trigger decorator registration
    import tools.resource_tools
    # Get the registered tools from the registry
    from registry import get_registered_tools
    tools = get_registered_tools()
    # Add all resource-related tools to our dummy MCP
    for tool_info in tools:
        tool_name = tool_info['name']
        if any(keyword in tool_name for keyword in ['find_in_file', 'list_resources', 'read_resource']):
            mcp.tools[tool_name] = tool_info['func']
    return mcp.tools


def test_read_resource_minimal_metadata_only(resource_tools, tmp_path):
    proj = tmp_path
    assets = proj / "Assets"
    assets.mkdir()
    f = assets / "A.txt"
    content = "hello world"
    f.write_text(content, encoding="utf-8")

    read_resource = resource_tools["read_resource"]
    loop = asyncio.new_event_loop()
    try:
        resp = loop.run_until_complete(
            read_resource(uri="unity://path/Assets/A.txt",
                          ctx=DummyContext(), project_root=str(proj))
        )
    finally:
        loop.close()

    assert resp["success"] is True
    data = resp["data"]
    assert "text" not in data
    meta = data["metadata"]
    assert "sha256" in meta and len(meta["sha256"]) == 64
    assert meta["lengthBytes"] == len(content.encode("utf-8"))
