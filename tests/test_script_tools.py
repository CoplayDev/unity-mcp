import sys
import pathlib
import importlib.util
import types
import pytest
import asyncio

# add server src to path and load modules without triggering package imports
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

def load_module(path, name):
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module

manage_script_module = load_module(SRC / "tools" / "manage_script.py", "manage_script_module")
manage_asset_module = load_module(SRC / "tools" / "manage_asset.py", "manage_asset_module")


class DummyMCP:
    def __init__(self):
        self.tools = {}

    def tool(self, *args, **kwargs):  # accept decorator kwargs like description
        def decorator(func):
            self.tools[func.__name__] = func
            return func
        return decorator

def setup_manage_script():
    mcp = DummyMCP()
    manage_script_module.register_manage_script_tools(mcp)
    return mcp.tools

def setup_manage_asset():
    mcp = DummyMCP()
    manage_asset_module.register_manage_asset_tools(mcp)
    return mcp.tools

def test_apply_text_edits_long_file(monkeypatch):
    tools = setup_manage_script()
    apply_edits = tools["apply_text_edits"]
    captured = {}

    def fake_send(cmd, params):
        captured["cmd"] = cmd
        captured["params"] = params
        return {"success": True}

    monkeypatch.setattr(manage_script_module, "send_command_with_retry", fake_send)

    edit = {"startLine": 1005, "startCol": 0, "endLine": 1005, "endCol": 5, "newText": "Hello"}
    resp = apply_edits(None, "unity://path/Assets/Scripts/LongFile.cs", [edit])
    assert captured["cmd"] == "manage_script"
    assert captured["params"]["action"] == "apply_text_edits"
    assert captured["params"]["edits"][0]["startLine"] == 1005
    assert resp["success"] is True

def test_sequential_edits_use_precondition(monkeypatch):
    tools = setup_manage_script()
    apply_edits = tools["apply_text_edits"]
    calls = []

    def fake_send(cmd, params):
        calls.append(params)
        return {"success": True, "sha256": f"hash{len(calls)}"}

    monkeypatch.setattr(manage_script_module, "send_command_with_retry", fake_send)

    edit1 = {"startLine": 1, "startCol": 0, "endLine": 1, "endCol": 0, "newText": "//header\n"}
    resp1 = apply_edits(None, "unity://path/Assets/Scripts/File.cs", [edit1])
    edit2 = {"startLine": 2, "startCol": 0, "endLine": 2, "endCol": 0, "newText": "//second\n"}
    resp2 = apply_edits(None, "unity://path/Assets/Scripts/File.cs", [edit2], precondition_sha256=resp1["sha256"])

    assert calls[1]["precondition_sha256"] == resp1["sha256"]
    assert resp2["sha256"] == "hash2"


def test_apply_text_edits_forwards_options(monkeypatch):
    tools = setup_manage_script()
    apply_edits = tools["apply_text_edits"]
    captured = {}

    def fake_send(cmd, params):
        captured["params"] = params
        return {"success": True}

    monkeypatch.setattr(manage_script_module, "send_command_with_retry", fake_send)

    opts = {"validate": "relaxed", "applyMode": "atomic", "refresh": "immediate"}
    apply_edits(None, "unity://path/Assets/Scripts/File.cs", [{"startLine":1,"startCol":1,"endLine":1,"endCol":1,"newText":"x"}], options=opts)
    assert captured["params"].get("options") == opts


def test_apply_text_edits_defaults_atomic_for_multi_span(monkeypatch):
    tools = setup_manage_script()
    apply_edits = tools["apply_text_edits"]
    captured = {}

    def fake_send(cmd, params):
        captured["params"] = params
        return {"success": True}

    monkeypatch.setattr(manage_script_module, "send_command_with_retry", fake_send)

    edits = [
        {"startLine": 2, "startCol": 2, "endLine": 2, "endCol": 3, "newText": "A"},
        {"startLine": 3, "startCol": 2, "endLine": 3, "endCol": 2, "newText": "// tail\n"},
    ]
    apply_edits(None, "unity://path/Assets/Scripts/File.cs", edits, precondition_sha256="x")
    opts = captured["params"].get("options", {})
    assert opts.get("applyMode") == "atomic"

def test_manage_asset_prefab_modify_request(monkeypatch):
    tools = setup_manage_asset()
    manage_asset = tools["manage_asset"]
    captured = {}

    async def fake_async(cmd, params, loop=None):
        captured["cmd"] = cmd
        captured["params"] = params
        return {"success": True}

    monkeypatch.setattr(manage_asset_module, "async_send_command_with_retry", fake_async)
    monkeypatch.setattr(manage_asset_module, "get_unity_connection", lambda: object())

    async def run():
        resp = await manage_asset(
            None,
            action="modify",
            path="Assets/Prefabs/Player.prefab",
            properties={"hp": 100},
        )
        assert captured["cmd"] == "manage_asset"
        assert captured["params"]["action"] == "modify"
        assert captured["params"]["path"] == "Assets/Prefabs/Player.prefab"
        assert captured["params"]["properties"] == {"hp": 100}
        assert resp["success"] is True

    asyncio.run(run())
