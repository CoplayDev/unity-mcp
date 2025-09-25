import sys
import pathlib
import importlib.util
import types

ROOT = pathlib.Path(__file__).resolve().parents[1]
SRC = ROOT / "UnityMcpBridge" / "UnityMcpServer~" / "src"
sys.path.insert(0, str(SRC))

# stub mcp.server.fastmcp similar to test_get_sha
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

def _load_module(path: pathlib.Path, name: str):
    spec = importlib.util.spec_from_file_location(name, path)
    mod = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(mod)
    return mod

manage_script = _load_module(SRC / "tools" / "manage_script.py", "manage_script_mod")

class DummyMCP:
    def __init__(self):
        self.tools = {}

    def tool(self, *args, **kwargs):
        def deco(fn):
            self.tools[fn.__name__] = fn
            return fn
        return deco

def setup_tools():
    mcp = DummyMCP()
    manage_script.register_manage_script_tools(mcp)
    return mcp.tools

def test_validate_script_returns_counts(monkeypatch):
    tools = setup_tools()
    validate_script = tools["validate_script"]

    def fake_send(cmd, params):
        return {
            "success": True,
            "data": {
                "diagnostics": [
                    {"severity": "warning"},
                    {"severity": "error"},
                    {"severity": "fatal"},
                ]
            },
        }

    monkeypatch.setattr(manage_script, "send_command_with_retry", fake_send)

    resp = validate_script(None, uri="unity://path/Assets/Scripts/A.cs")
    assert resp == {"success": True, "data": {"warnings": 1, "errors": 2}}
