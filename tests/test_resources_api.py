import pytest


import sys
from pathlib import Path
import pytest
import types

# locate server src dynamically to avoid hardcoded layout assumptions
ROOT = Path(__file__).resolve().parents[1]
candidates = [
    ROOT / "UnityMcpBridge" / "UnityMcpServer~" / "src",
    ROOT / "UnityMcpServer~" / "src",
]
SRC = next((p for p in candidates if p.exists()), None)
if SRC is None:
    searched = "\n".join(str(p) for p in candidates)
    pytest.skip(
        "Unity MCP server source not found. Tried:\n" + searched,
        allow_module_level=True,
    )
sys.path.insert(0, str(SRC))

from tools.resource_tools import register_resource_tools  # type: ignore

class DummyMCP:
    def __init__(self):
        self._tools = {}
    def tool(self, *args, **kwargs):  # accept kwargs like description
        def deco(fn):
            self._tools[fn.__name__] = fn
            return fn
        return deco

@pytest.fixture()
def resource_tools():
    mcp = DummyMCP()
    register_resource_tools(mcp)
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
    resp = asyncio.get_event_loop().run_until_complete(
        list_resources(ctx=None, pattern="*.cs", under="Assets", limit=50, project_root=str(proj))
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
    resp = asyncio.get_event_loop().run_until_complete(
        list_resources(ctx=None, pattern="*.cs", under="..", limit=10, project_root=str(proj))
    )
    assert resp["success"] is False
    assert "Assets" in resp.get("error", "") or "under project root" in resp.get("error", "")
