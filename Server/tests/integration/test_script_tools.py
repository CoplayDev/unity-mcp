import pytest
import asyncio

from .test_helpers import DummyContext, DummyMCP, setup_script_tools


def setup_asset_tools():
    """Setup asset-related tools for testing."""
    mcp = DummyMCP()
    import services.tools.manage_asset
    from services.registry import get_registered_tools
    for tool_info in get_registered_tools():
        tool_name = tool_info['name']
        if any(keyword in tool_name for keyword in ['asset', 'manage_asset']):
            mcp.tools[tool_name] = tool_info['func']
    return mcp.tools


@pytest.mark.asyncio
async def test_apply_text_edits_long_file(monkeypatch):
    tools = setup_script_tools()
    apply_edits = tools["apply_text_edits"]
    captured = {}

    async def fake_send(cmd, params, **kwargs):
        captured["cmd"] = cmd
        captured["params"] = params
        return {"success": True}

    # Patch the send_command_with_retry function at the module level where it's imported
    import transport.legacy.unity_connection
    monkeypatch.setattr(
        transport.legacy.unity_connection,
        "async_send_command_with_retry",
        fake_send,
    )
    # No need to patch tools.manage_script; it now calls unity_connection.send_command_with_retry

    edit = {"startLine": 1005, "startCol": 0,
            "endLine": 1005, "endCol": 5, "newText": "Hello"}
    ctx = DummyContext()
    resp = await apply_edits(ctx, "mcpforunity://path/Assets/Scripts/LongFile.cs", [edit])
    assert captured["cmd"] == "manage_script"
    assert captured["params"]["action"] == "apply_text_edits"
    assert captured["params"]["edits"][0]["startLine"] == 1005
    assert resp["success"] is True


@pytest.mark.asyncio
async def test_sequential_edits_use_precondition(monkeypatch):
    tools = setup_script_tools()
    apply_edits = tools["apply_text_edits"]
    calls = []

    async def fake_send(cmd, params, **kwargs):
        calls.append(params)
        return {"success": True, "sha256": f"hash{len(calls)}"}

    # Patch the send_command_with_retry function at the module level where it's imported
    import transport.legacy.unity_connection
    monkeypatch.setattr(
        transport.legacy.unity_connection,
        "async_send_command_with_retry",
        fake_send,
    )
    # No need to patch tools.manage_script; it now calls unity_connection.send_command_with_retry

    edit1 = {"startLine": 1, "startCol": 0, "endLine": 1,
             "endCol": 0, "newText": "//header\n"}
    resp1 = await apply_edits(DummyContext(), "mcpforunity://path/Assets/Scripts/File.cs", [edit1])
    edit2 = {"startLine": 2, "startCol": 0, "endLine": 2,
             "endCol": 0, "newText": "//second\n"}
    resp2 = await apply_edits(
        DummyContext(),
        "mcpforunity://path/Assets/Scripts/File.cs",
        [edit2],
        precondition_sha256=resp1["sha256"],
    )

    assert calls[1]["precondition_sha256"] == resp1["sha256"]
    assert resp2["sha256"] == "hash2"


@pytest.mark.asyncio
async def test_apply_text_edits_forwards_options(monkeypatch):
    tools = setup_script_tools()
    apply_edits = tools["apply_text_edits"]
    captured = {}

    async def fake_send(cmd, params, **kwargs):
        captured["params"] = params
        return {"success": True}

    # Patch the send_command_with_retry function at the module level where it's imported
    import transport.legacy.unity_connection
    monkeypatch.setattr(
        transport.legacy.unity_connection,
        "async_send_command_with_retry",
        fake_send,
    )
    # No need to patch tools.manage_script; it now calls unity_connection.send_command_with_retry

    opts = {"validate": "relaxed", "applyMode": "atomic", "refresh": "immediate"}
    await apply_edits(
        DummyContext(),
        "mcpforunity://path/Assets/Scripts/File.cs",
        [{"startLine": 1, "startCol": 1, "endLine": 1, "endCol": 1, "newText": "x"}],
        options=opts,
    )
    assert captured["params"].get("options") == opts


@pytest.mark.asyncio
async def test_apply_text_edits_defaults_atomic_for_multi_span(monkeypatch):
    tools = setup_script_tools()
    apply_edits = tools["apply_text_edits"]
    captured = {}

    async def fake_send(cmd, params, **kwargs):
        captured["params"] = params
        return {"success": True}

    # Patch the send_command_with_retry function at the module level where it's imported
    import transport.legacy.unity_connection
    monkeypatch.setattr(
        transport.legacy.unity_connection,
        "async_send_command_with_retry",
        fake_send,
    )
    # No need to patch tools.manage_script; it now calls unity_connection.send_command_with_retry

    edits = [
        {"startLine": 2, "startCol": 2, "endLine": 2, "endCol": 3, "newText": "A"},
        {"startLine": 3, "startCol": 2, "endLine": 3,
            "endCol": 2, "newText": "// tail\n"},
    ]
    await apply_edits(
        DummyContext(),
        "mcpforunity://path/Assets/Scripts/File.cs",
        edits,
        precondition_sha256="x",
    )
    opts = captured["params"].get("options", {})
    assert opts.get("applyMode") == "atomic"


@pytest.mark.asyncio
async def test_manage_asset_prefab_modify_request(monkeypatch):
    tools = setup_asset_tools()
    manage_asset = tools["manage_asset"]
    captured = {}

    async def fake_async(cmd, params, loop=None):
        captured["cmd"] = cmd
        captured["params"] = params
        return {"success": True}

    # Patch the async function in the tools module
    import services.tools.manage_asset as tools_manage_asset
    # Patch both at the module and at the function closure location
    monkeypatch.setattr(tools_manage_asset,
                        "async_send_command_with_retry", fake_async)
    # Also patch the globals of the function object (handles dynamically loaded module alias)
    manage_asset.__globals__["async_send_command_with_retry"] = fake_async

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
