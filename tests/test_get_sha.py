import sys
import pathlib
import importlib.util
import types


ROOT = pathlib.Path(__file__).resolve().parents[1]
SRC = ROOT / "UnityMcpBridge" / "UnityMcpServer~" / "src"
sys.path.insert(0, str(SRC))

# stub mcp.server.fastmcp to satisfy imports without full dependency
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


def test_get_sha_param_shape_and_routing(monkeypatch):
    tools = setup_tools()
    get_sha = tools["get_sha"]

    captured = {}

    def fake_send(cmd, params):
        captured["cmd"] = cmd
        captured["params"] = params
        return {"success": True, "data": {"sha256": "abc", "lengthBytes": 1, "lastModifiedUtc": "2020-01-01T00:00:00Z", "uri": "unity://path/Assets/Scripts/A.cs", "path": "Assets/Scripts/A.cs"}}

    monkeypatch.setattr(manage_script, "send_command_with_retry", fake_send)

    resp = get_sha(None, uri="unity://path/Assets/Scripts/A.cs")
    assert captured["cmd"] == "manage_script"
    assert captured["params"]["action"] == "get_sha"
    assert captured["params"]["name"] == "A"
    assert captured["params"]["path"].endswith("Assets/Scripts")
    assert resp["success"] is True

