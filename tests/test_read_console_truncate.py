import sys
import pathlib
import importlib.util
import types

ROOT = pathlib.Path(__file__).resolve().parents[1]
SRC = ROOT / "UnityMcpBridge" / "UnityMcpServer~" / "src"
sys.path.insert(0, str(SRC))

# stub mcp.server.fastmcp
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

read_console_mod = _load_module(SRC / "tools" / "read_console.py", "read_console_mod")

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
    read_console_mod.register_read_console_tools(mcp)
    return mcp.tools

def test_read_console_full_default(monkeypatch):
    tools = setup_tools()
    read_console = tools["read_console"]

    captured = {}

    def fake_send(cmd, params):
        captured["params"] = params
        return {
            "success": True,
            "data": {"lines": [{"level": "error", "message": "oops", "stacktrace": "trace", "time": "t"}]},
        }

    monkeypatch.setattr(read_console_mod, "send_command_with_retry", fake_send)
    monkeypatch.setattr(read_console_mod, "get_unity_connection", lambda: object())

    resp = read_console(ctx=None, count=10)
    assert resp == {
        "success": True,
        "data": {"lines": [{"level": "error", "message": "oops", "stacktrace": "trace", "time": "t"}]},
    }
    assert captured["params"]["count"] == 10
    assert captured["params"]["includeStacktrace"] is True


def test_read_console_truncated(monkeypatch):
    tools = setup_tools()
    read_console = tools["read_console"]

    captured = {}

    def fake_send(cmd, params):
        captured["params"] = params
        return {
            "success": True,
            "data": {"lines": [{"level": "error", "message": "oops", "stacktrace": "trace"}]},
        }

    monkeypatch.setattr(read_console_mod, "send_command_with_retry", fake_send)
    monkeypatch.setattr(read_console_mod, "get_unity_connection", lambda: object())

    resp = read_console(ctx=None, count=10, include_stacktrace=False)
    assert resp == {"success": True, "data": {"lines": [{"level": "error", "message": "oops"}]}}
    assert captured["params"]["includeStacktrace"] is False
