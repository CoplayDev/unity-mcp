import sys
import pathlib
import importlib.util


ROOT = pathlib.Path(__file__).resolve().parents[1]
SRC = ROOT / "MCPForUnity" / "UnityMcpServer~" / "src"
sys.path.insert(0, str(SRC))


def _load_module(path: pathlib.Path, name: str):
    spec = importlib.util.spec_from_file_location(name, path)
    if spec is None or spec.loader is None:
        raise ImportError(f"Cannot load module {name} from {path}")
    mod = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(mod)
    return mod


manage_script = _load_module(
    SRC / "tools" / "manage_script.py", "manage_script_mod")


class DummyMCP:
    def __init__(self):
        self.tools = {}

    def tool(self, *args, **kwargs):
        def deco(fn):
            self.tools[fn.__name__] = fn
            return fn
        return deco


from tests.test_helpers import DummyContext


def setup_tools():
    mcp = DummyMCP()
    # Import the tools module to trigger decorator registration
    import tools.manage_script
    # Get the registered tools from the registry
    from registry import get_registered_tools
    tools = get_registered_tools()
    # Add all script-related tools to our dummy MCP
    for tool_info in tools:
        tool_name = tool_info['name']
        if any(keyword in tool_name for keyword in ['script', 'apply_text', 'create_script', 'delete_script', 'validate_script', 'get_sha']):
            mcp.tools[tool_name] = tool_info['func']
    return mcp.tools


def test_get_sha_param_shape_and_routing(monkeypatch):
    tools = setup_tools()
    get_sha = tools["get_sha"]

    captured = {}

    def fake_send(cmd, params):
        captured["cmd"] = cmd
        captured["params"] = params
        return {"success": True, "data": {"sha256": "abc", "lengthBytes": 1, "lastModifiedUtc": "2020-01-01T00:00:00Z", "uri": "unity://path/Assets/Scripts/A.cs", "path": "Assets/Scripts/A.cs"}}

    # Patch the send_command_with_retry function at the module level where it's imported
    import unity_connection
    monkeypatch.setattr(unity_connection,
                        "send_command_with_retry", fake_send)
    # No need to patch tools.manage_script; it now calls unity_connection.send_command_with_retry

    resp = get_sha(DummyContext(), uri="unity://path/Assets/Scripts/A.cs")
    assert captured["cmd"] == "manage_script"
    assert captured["params"]["action"] == "get_sha"
    assert captured["params"]["name"] == "A"
    assert captured["params"]["path"].endswith("Assets/Scripts")
    assert resp["success"] is True
    assert resp["data"] == {"sha256": "abc", "lengthBytes": 1}
