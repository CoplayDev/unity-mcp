# import triggers registration elsewhere; no direct use here
import sys
import types
from pathlib import Path

import pytest


# Locate server src dynamically to avoid hardcoded layout assumptions (same as other tests)
ROOT = Path(__file__).resolve().parents[1]
candidates = [
    ROOT / "MCPForUnity" / "UnityMcpServer~" / "src",
    ROOT / "UnityMcpServer~" / "src",
]
SRC = next((p for p in candidates if p.exists()), None)
if SRC is None:
    searched = "\n".join(str(p) for p in candidates)
    pytest.skip(
        "MCP for Unity server source not found. Tried:\n" + searched,
        allow_module_level=True,
    )
sys.path.insert(0, str(SRC))

# Stub fastmcp to avoid real MCP deps
fastmcp_pkg = types.ModuleType("fastmcp")


class _Dummy:
    pass


fastmcp_pkg.FastMCP = _Dummy
fastmcp_pkg.Context = _Dummy
sys.modules.setdefault("fastmcp", fastmcp_pkg)


# Import target module after path injection


class DummyMCP:
    def __init__(self):
        self.tools = {}

    def tool(self, *args, **kwargs):  # ignore decorator kwargs like description
        def _decorator(fn):
            self.tools[fn.__name__] = fn
            return fn
        return _decorator


# (removed unused DummyCtx)


from tests.test_helpers import DummyContext


def _register_tools():
    mcp = DummyMCP()
    # Import the tools module to trigger decorator registration
    import tools.manage_script  # trigger decorator registration
    # Get the registered tools from the registry
    from registry import get_registered_tools
    registered_tools = get_registered_tools()
    # Add all script-related tools to our dummy MCP
    for tool_info in registered_tools:
        tool_name = tool_info['name']
        if any(keyword in tool_name for keyword in ['script', 'apply_text', 'create_script', 'delete_script', 'validate_script', 'get_sha']):
            mcp.tools[tool_name] = tool_info['func']
    return mcp.tools


def test_split_uri_unity_path(monkeypatch):
    test_tools = _register_tools()
    captured = {}

    def fake_send(cmd, params):  # capture params and return success
        captured['cmd'] = cmd
        captured['params'] = params
        return {"success": True, "message": "ok"}

    # Patch the send_command_with_retry function at the module level where it's imported
    import unity_connection
    monkeypatch.setattr(unity_connection,
                        "send_command_with_retry", fake_send)
    # No need to patch tools.manage_script; it now calls unity_connection.send_command_with_retry

    fn = test_tools['apply_text_edits']
    uri = "unity://path/Assets/Scripts/MyScript.cs"
    fn(DummyContext(), uri=uri, edits=[], precondition_sha256=None)

    assert captured['cmd'] == 'manage_script'
    assert captured['params']['name'] == 'MyScript'
    assert captured['params']['path'] == 'Assets/Scripts'


@pytest.mark.parametrize(
    "uri, expected_name, expected_path",
    [
        ("file:///Users/alex/Project/Assets/Scripts/Foo%20Bar.cs",
         "Foo Bar", "Assets/Scripts"),
        ("file://localhost/Users/alex/Project/Assets/Hello.cs", "Hello", "Assets"),
        ("file:///C:/Users/Alex/Proj/Assets/Scripts/Hello.cs",
         "Hello", "Assets/Scripts"),
        # outside Assets → fall back to normalized dir
        ("file:///tmp/Other.cs", "Other", "tmp"),
    ],
)
def test_split_uri_file_urls(monkeypatch, uri, expected_name, expected_path):
    test_tools = _register_tools()
    captured = {}

    def fake_send(_cmd, params):
        captured['cmd'] = _cmd
        captured['params'] = params
        return {"success": True, "message": "ok"}

    # Patch the send_command_with_retry function at the module level where it's imported
    import unity_connection
    monkeypatch.setattr(unity_connection,
                        "send_command_with_retry", fake_send)
    # No need to patch tools.manage_script; it now calls unity_connection.send_command_with_retry

    fn = test_tools['apply_text_edits']
    fn(DummyContext(), uri=uri, edits=[], precondition_sha256=None)

    assert captured['params']['name'] == expected_name
    assert captured['params']['path'] == expected_path


def test_split_uri_plain_path(monkeypatch):
    test_tools = _register_tools()
    captured = {}

    def fake_send(_cmd, params):
        captured['params'] = params
        return {"success": True, "message": "ok"}

    # Patch the send_command_with_retry function at the module level where it's imported
    import unity_connection
    monkeypatch.setattr(unity_connection,
                        "send_command_with_retry", fake_send)
    # No need to patch tools.manage_script; it now calls unity_connection.send_command_with_retry

    fn = test_tools['apply_text_edits']
    fn(DummyContext(), uri="Assets/Scripts/Thing.cs",
       edits=[], precondition_sha256=None)

    assert captured['params']['name'] == 'Thing'
    assert captured['params']['path'] == 'Assets/Scripts'
