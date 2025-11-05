import asyncio
import pytest

from .test_helpers import DummyContext


class DummyMCP:
    def __init__(self):
        self.tools = {}

    def tool(self, *args, **kwargs):
        def deco(fn):
            self.tools[fn.__name__] = fn
            return fn
        return deco


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


def test_find_in_file_returns_positions(resource_tools, tmp_path):
    proj = tmp_path
    assets = proj / "Assets"
    assets.mkdir()
    f = assets / "A.txt"
    f.write_text("hello world", encoding="utf-8")
    find_in_file = resource_tools["find_in_file"]
    loop = asyncio.new_event_loop()
    try:
        resp = loop.run_until_complete(
            find_in_file(uri="unity://path/Assets/A.txt",
                         pattern="world", ctx=DummyContext(), project_root=str(proj))
        )
    finally:
        loop.close()
    assert resp["success"] is True
    assert resp["data"]["matches"] == [
        {"startLine": 1, "startCol": 7, "endLine": 1, "endCol": 12}]
