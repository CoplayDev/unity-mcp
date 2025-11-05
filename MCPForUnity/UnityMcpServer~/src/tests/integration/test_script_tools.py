import pytest
import asyncio

from .test_helpers import DummyContext


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


def setup_manage_asset():
    mcp = DummyMCP()
    # Import the tools module to trigger decorator registration
    import tools.manage_asset
    # Get the registered tools from the registry
    from registry import get_registered_tools
    tools = get_registered_tools()
    # Add all asset-related tools to our dummy MCP
    for tool_info in tools:
        tool_name = tool_info['name']
        if any(keyword in tool_name for keyword in ['asset', 'manage_asset']):
            mcp.tools[tool_name] = tool_info['func']
    return mcp.tools


def test_apply_text_edits_long_file(monkeypatch):
    tools = setup_manage_script()
    apply_edits = tools["apply_text_edits"]
    captured = {}

    def fake_send(cmd, params):
        captured["cmd"] = cmd
        captured["params"] = params
        return {"success": True}

    # Patch the send_command_with_retry function at the module level where it's imported
    import unity_connection
    monkeypatch.setattr(unity_connection,
                        "send_command_with_retry", fake_send)
    # No need to patch tools.manage_script; it now calls unity_connection.send_command_with_retry

    edit = {"startLine": 1005, "startCol": 0,
            "endLine": 1005, "endCol": 5, "newText": "Hello"}
    ctx = DummyContext()
    resp = apply_edits(ctx, "unity://path/Assets/Scripts/LongFile.cs", [edit])
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

    # Patch the send_command_with_retry function at the module level where it's imported
    import unity_connection
    monkeypatch.setattr(unity_connection,
                        "send_command_with_retry", fake_send)
    # No need to patch tools.manage_script; it now calls unity_connection.send_command_with_retry

    edit1 = {"startLine": 1, "startCol": 0, "endLine": 1,
             "endCol": 0, "newText": "//header\n"}
    resp1 = apply_edits(DummyContext(), "unity://path/Assets/Scripts/File.cs", [edit1])
    edit2 = {"startLine": 2, "startCol": 0, "endLine": 2,
             "endCol": 0, "newText": "//second\n"}
    resp2 = apply_edits(DummyContext(), "unity://path/Assets/Scripts/File.cs",
                        [edit2], precondition_sha256=resp1["sha256"])

    assert calls[1]["precondition_sha256"] == resp1["sha256"]
    assert resp2["sha256"] == "hash2"


def test_apply_text_edits_forwards_options(monkeypatch):
    tools = setup_manage_script()
    apply_edits = tools["apply_text_edits"]
    captured = {}

    def fake_send(cmd, params):
        captured["params"] = params
        return {"success": True}

    # Patch the send_command_with_retry function at the module level where it's imported
    import unity_connection
    monkeypatch.setattr(unity_connection,
                        "send_command_with_retry", fake_send)
    # No need to patch tools.manage_script; it now calls unity_connection.send_command_with_retry

    opts = {"validate": "relaxed", "applyMode": "atomic", "refresh": "immediate"}
    apply_edits(DummyContext(), "unity://path/Assets/Scripts/File.cs",
                [{"startLine": 1, "startCol": 1, "endLine": 1, "endCol": 1, "newText": "x"}], options=opts)
    assert captured["params"].get("options") == opts


def test_apply_text_edits_defaults_atomic_for_multi_span(monkeypatch):
    tools = setup_manage_script()
    apply_edits = tools["apply_text_edits"]
    captured = {}

    def fake_send(cmd, params):
        captured["params"] = params
        return {"success": True}

    # Patch the send_command_with_retry function at the module level where it's imported
    import unity_connection
    monkeypatch.setattr(unity_connection,
                        "send_command_with_retry", fake_send)
    # No need to patch tools.manage_script; it now calls unity_connection.send_command_with_retry

    edits = [
        {"startLine": 2, "startCol": 2, "endLine": 2, "endCol": 3, "newText": "A"},
        {"startLine": 3, "startCol": 2, "endLine": 3,
            "endCol": 2, "newText": "// tail\n"},
    ]
    apply_edits(DummyContext(), "unity://path/Assets/Scripts/File.cs",
                edits, precondition_sha256="x")
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

    # Patch the async function in the tools module
    import tools.manage_asset as tools_manage_asset
    # Patch both at the module and at the function closure location
    monkeypatch.setattr(tools_manage_asset,
                        "async_send_command_with_retry", fake_async)
    # Also patch the globals of the function object (handles dynamically loaded module alias)
    manage_asset.__globals__["async_send_command_with_retry"] = fake_async

    async def run():
        resp = await manage_asset(
            DummyContext(),
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
