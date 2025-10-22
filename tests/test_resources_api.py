import sys
from pathlib import Path
import pytest
import types

# locate server src dynamically to avoid hardcoded layout assumptions
ROOT = Path(__file__).resolve().parents[1]
SRC = ROOT / "MCPForUnity" / "UnityMcpServer~" / "src"
sys.path.insert(0, str(SRC))

# Stub telemetry modules to avoid file I/O during import of tools package
telemetry = types.ModuleType("telemetry")
def _noop(*args, **kwargs):
    pass
class MilestoneType:  # minimal placeholder
    pass
telemetry.record_resource_usage = _noop
telemetry.record_tool_usage = _noop
telemetry.record_milestone = _noop
telemetry.MilestoneType = MilestoneType
telemetry.get_package_version = lambda: "0.0.0"
sys.modules.setdefault("telemetry", telemetry)

telemetry_decorator = types.ModuleType("telemetry_decorator")
def telemetry_tool(*_args, **_kwargs):
    def _wrap(fn):
        return fn
    return _wrap
telemetry_decorator.telemetry_tool = telemetry_tool
sys.modules.setdefault("telemetry_decorator", telemetry_decorator)


class DummyMCP:
    def __init__(self):
        self._tools = {}

    def tool(self, *args, **kwargs):  # accept kwargs like description
        def deco(fn):
            self._tools[fn.__name__] = fn
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
            mcp._tools[tool_name] = tool_info['func']
    return mcp._tools


def test_resource_list_filters_and_rejects_traversal(resource_tools, tmp_path, monkeypatch):
    # Create fake project structure
    proj = tmp_path
    assets = proj / "Assets" / "Scripts"
    assets.mkdir(parents=True)
    (assets / "A.cs").write_text("// a", encoding="utf-8")
    (assets / "B.txt").write_text("b", encoding="utf-8")
    outside = tmp_path / "Outside.cs"
    outside.write_text("// outside", encoding="utf-8")
    # Symlink attempting to escape
    sneaky_link = assets / "link_out"
    try:
        sneaky_link.symlink_to(outside)
    except Exception:
        # Some platforms may not allow symlinks in tests; ignore
        pass

    list_resources = resource_tools["list_resources"]
    # Only .cs under Assets should be listed
    import asyncio
    resp = asyncio.run(
        list_resources(ctx=DummyContext(), pattern="*.cs", under="Assets",
                       limit=50, project_root=str(proj))
    )
    assert resp["success"] is True
    uris = resp["data"]["uris"]
    assert any(u.endswith("Assets/Scripts/A.cs") for u in uris)
    assert not any(u.endswith("B.txt") for u in uris)
    assert not any(u.endswith("Outside.cs") for u in uris)


def test_resource_list_rejects_outside_paths(resource_tools, tmp_path):
    proj = tmp_path
    # under points outside Assets
    list_resources = resource_tools["list_resources"]
    import asyncio
    resp = asyncio.run(
        list_resources(ctx=DummyContext(), pattern="*.cs", under="..",
                       limit=10, project_root=str(proj))
    )
    assert resp["success"] is False
    assert "Assets" in resp.get(
        "error", "") or "under project root" in resp.get("error", "")
