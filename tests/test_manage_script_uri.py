import sys
import types
from pathlib import Path

import pytest



# Locate server src dynamically to avoid hardcoded layout assumptions (same as other tests)
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

# Stub mcp.server.fastmcp to satisfy imports without full package
mcp_pkg = types.ModuleType("mcp")
server_pkg = types.ModuleType("mcp.server")
fastmcp_pkg = types.ModuleType("mcp.server.fastmcp")
class _Dummy: pass
fastmcp_pkg.FastMCP = _Dummy
fastmcp_pkg.Context = _Dummy
server_pkg.fastmcp = fastmcp_pkg
mcp_pkg.server = server_pkg
sys.modules.setdefault("mcp", mcp_pkg)
sys.modules.setdefault("mcp.server", server_pkg)
sys.modules.setdefault("mcp.server.fastmcp", fastmcp_pkg)


# Import target module after path injection
import tools.manage_script as manage_script  # type: ignore


class DummyMCP:
    def __init__(self):
        self.tools = {}

    def tool(self, *args, **kwargs):  # ignore decorator kwargs like description
        def _decorator(fn):
            self.tools[fn.__name__] = fn
            return fn
        return _decorator


class DummyCtx:  # FastMCP Context placeholder
    pass


def _register_tools():
    mcp = DummyMCP()
    manage_script.register_manage_script_tools(mcp)  # populates mcp.tools
    return mcp.tools


def test_split_uri_unity_path(monkeypatch):
    tools = _register_tools()
    captured = {}

    def fake_send(cmd, params):  # capture params and return success
        captured['cmd'] = cmd
        captured['params'] = params
        return {"success": True, "message": "ok"}

    monkeypatch.setattr(manage_script, "send_command_with_retry", fake_send)

    fn = tools['apply_text_edits']
    uri = "unity://path/Assets/Scripts/MyScript.cs"
    fn(DummyCtx(), uri=uri, edits=[], precondition_sha256=None)

    assert captured['cmd'] == 'manage_script'
    assert captured['params']['name'] == 'MyScript'
    assert captured['params']['path'] == 'Assets/Scripts'


@pytest.mark.parametrize(
    "uri, expected_name, expected_path",
    [
        ("file:///Users/alex/Project/Assets/Scripts/Foo%20Bar.cs", "Foo Bar", "Assets/Scripts"),
        ("file://localhost/Users/alex/Project/Assets/Hello.cs", "Hello", "Assets"),
        ("file:///C:/Users/Alex/Proj/Assets/Scripts/Hello.cs", "Hello", "Assets/Scripts"),
        ("file:///tmp/Other.cs", "Other", "tmp"),  # outside Assets → fall back to normalized dir
    ],
)
def test_split_uri_file_urls(monkeypatch, uri, expected_name, expected_path):
    tools = _register_tools()
    captured = {}

    def fake_send(cmd, params):
        captured['cmd'] = cmd
        captured['params'] = params
        return {"success": True, "message": "ok"}

    monkeypatch.setattr(manage_script, "send_command_with_retry", fake_send)

    fn = tools['apply_text_edits']
    fn(DummyCtx(), uri=uri, edits=[], precondition_sha256=None)

    assert captured['params']['name'] == expected_name
    assert captured['params']['path'] == expected_path


def test_split_uri_plain_path(monkeypatch):
    tools = _register_tools()
    captured = {}

    def fake_send(cmd, params):
        captured['params'] = params
        return {"success": True, "message": "ok"}

    monkeypatch.setattr(manage_script, "send_command_with_retry", fake_send)

    fn = tools['apply_text_edits']
    fn(DummyCtx(), uri="Assets/Scripts/Thing.cs", edits=[], precondition_sha256=None)

    assert captured['params']['name'] == 'Thing'
    assert captured['params']['path'] == 'Assets/Scripts'


