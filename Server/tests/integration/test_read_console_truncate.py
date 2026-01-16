import pytest

from .test_helpers import DummyContext


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
    # Import the tools module to trigger decorator registration
    import services.tools.read_console
    # Get the registered tools from the registry
    from services.registry import get_registered_tools
    registered_tools = get_registered_tools()
    # Add all console-related tools to our dummy MCP
    for tool_info in registered_tools:
        tool_name = tool_info['name']
        if any(keyword in tool_name for keyword in ['read_console', 'console']):
            mcp.tools[tool_name] = tool_info['func']
    return mcp.tools


@pytest.mark.asyncio
async def test_read_console_full_default(monkeypatch):
    tools = setup_tools()
    read_console = tools["read_console"]

    captured = {}

    async def fake_send(_cmd, params, **_kwargs):
        captured["params"] = params
        return {
            "success": True,
            "data": {"lines": [{"level": "error", "message": "oops", "stacktrace": "trace", "time": "t"}]},
        }

    # Patch the send_command_with_retry function in the tools module
    import services.tools.read_console
    monkeypatch.setattr(
        services.tools.read_console,
        "async_send_command_with_retry",
        fake_send,
    )

    resp = await read_console(ctx=DummyContext(), action="get", count=10)
    assert resp == {
        "success": True,
        "data": {"lines": [{"level": "error", "message": "oops", "time": "t"}]},
    }
    assert captured["params"]["count"] == 10
    assert captured["params"]["includeStacktrace"] is False


@pytest.mark.asyncio
async def test_read_console_truncated(monkeypatch):
    tools = setup_tools()
    read_console = tools["read_console"]

    captured = {}

    async def fake_send(_cmd, params, **_kwargs):
        captured["params"] = params
        return {
            "success": True,
            "data": {"lines": [{"level": "error", "message": "oops", "stacktrace": "trace"}]},
        }

    # Patch the send_command_with_retry function in the tools module
    import services.tools.read_console
    monkeypatch.setattr(
        services.tools.read_console,
        "async_send_command_with_retry",
        fake_send,
    )

    resp = await read_console(ctx=DummyContext(), action="get", count=10, include_stacktrace=False)
    assert resp == {"success": True, "data": {
        "lines": [{"level": "error", "message": "oops"}]}}
    assert captured["params"]["includeStacktrace"] is False


@pytest.mark.asyncio
async def test_read_console_default_count(monkeypatch):
    """Test that read_console defaults to count=10 when not specified."""
    tools = setup_tools()
    read_console = tools["read_console"]

    captured = {}

    async def fake_send(_cmd, params, **_kwargs):
        captured["params"] = params
        return {
            "success": True,
            "data": {"lines": [{"level": "error", "message": f"error {i}"} for i in range(15)]},
        }

    # Patch the send_command_with_retry function in the tools module
    import services.tools.read_console
    monkeypatch.setattr(
        services.tools.read_console,
        "async_send_command_with_retry",
        fake_send,
    )

    # Call without specifying count - should default to 10
    resp = await read_console(ctx=DummyContext(), action="get")
    assert resp["success"] is True
    # Verify that the default count of 10 was used
    assert captured["params"]["count"] == 10


@pytest.mark.asyncio
async def test_read_console_paging(monkeypatch):
    """Test that read_console paging works with page_size and cursor."""
    tools = setup_tools()
    read_console = tools["read_console"]

    captured = {}

    async def fake_send(_cmd, params, **_kwargs):
        captured["params"] = params
        # Simulate Unity returning paging info matching C# structure
        page_size = params.get("pageSize", 10)
        cursor = params.get("cursor", 0)
        # Simulate 25 total messages
        all_messages = [{"level": "error", "message": f"error {i}"} for i in range(25)]
        
        # Return a page of results
        start = cursor
        end = min(start + page_size, len(all_messages))
        messages = all_messages[start:end]
        
        return {
            "success": True,
            "data": {
                "items": messages,
                "cursor": cursor,
                "pageSize": page_size,
                "nextCursor": str(end) if end < len(all_messages) else None,
                "truncated": end < len(all_messages),
                "total": len(all_messages),
            },
        }

    # Patch the send_command_with_retry function in the tools module
    import services.tools.read_console
    monkeypatch.setattr(
        services.tools.read_console,
        "async_send_command_with_retry",
        fake_send,
    )

    # First page - get first 5 entries
    resp = await read_console(ctx=DummyContext(), action="get", page_size=5, cursor=0)
    assert resp["success"] is True
    assert captured["params"]["pageSize"] == 5
    assert captured["params"]["cursor"] == 0
    assert len(resp["data"]["items"]) == 5
    assert resp["data"]["truncated"] is True
    assert resp["data"]["nextCursor"] == "5"
    assert resp["data"]["total"] == 25
    
    # Second page - get next 5 entries
    resp = await read_console(ctx=DummyContext(), action="get", page_size=5, cursor=5)
    assert resp["success"] is True
    assert captured["params"]["cursor"] == 5
    assert len(resp["data"]["items"]) == 5
    assert resp["data"]["truncated"] is True
    assert resp["data"]["nextCursor"] == "10"
    
    # Last page - get remaining entries
    resp = await read_console(ctx=DummyContext(), action="get", page_size=5, cursor=20)
    assert resp["success"] is True
    assert len(resp["data"]["items"]) == 5
    assert resp["data"]["truncated"] is False
    assert resp["data"]["nextCursor"] is None


@pytest.mark.asyncio
async def test_read_console_types_json_string(monkeypatch):
    """Test that read_console handles types parameter as JSON string (fixes issue #561)."""
    tools = setup_tools()
    read_console = tools["read_console"]

    captured = {}

    async def fake_send_with_unity_instance(_send_fn, _unity_instance, _command_type, params, **_kwargs):
        captured["params"] = params
        return {
            "success": True,
            "data": {"lines": [{"level": "error", "message": "test error"}]},
        }

    import services.tools.read_console as read_console_mod
    monkeypatch.setattr(
        read_console_mod,
        "send_with_unity_instance",
        fake_send_with_unity_instance,
    )

    # Test with types as JSON string (the problematic case from issue #561)
    resp = await read_console(ctx=DummyContext(), action="get", types='["error", "warning", "all"]')
    assert resp["success"] is True
    # Verify types was parsed correctly and sent as a list
    assert isinstance(captured["params"]["types"], list)
    assert captured["params"]["types"] == ["error", "warning", "all"]
    
    # Test case normalization to lowercase
    captured.clear()
    resp = await read_console(ctx=DummyContext(), action="get", types='["ERROR", "Warning", "LOG"]')
    assert resp["success"] is True
    assert captured["params"]["types"] == ["error", "warning", "log"]

    # Test with types as actual list (should still work)
    captured.clear()
    resp = await read_console(ctx=DummyContext(), action="get", types=["error", "warning"])
    assert resp["success"] is True
    assert isinstance(captured["params"]["types"], list)
    assert captured["params"]["types"] == ["error", "warning"]


@pytest.mark.asyncio
async def test_read_console_types_validation(monkeypatch):
    """Test that read_console validates types entries and rejects invalid values."""
    tools = setup_tools()
    read_console = tools["read_console"]

    captured = {}

    async def fake_send_with_unity_instance(_send_fn, _unity_instance, _command_type, params, **_kwargs):
        captured["params"] = params
        return {"success": True, "data": {"lines": []}}

    import services.tools.read_console as read_console_mod
    monkeypatch.setattr(
        read_console_mod,
        "send_with_unity_instance",
        fake_send_with_unity_instance,
    )

    # Invalid entry in list should return a clear error and not send.
    captured.clear()
    resp = await read_console(ctx=DummyContext(), action="get", types='["error", "nope"]')
    assert resp["success"] is False
    assert "invalid types entry" in resp["message"]
    assert captured == {}

    # Non-string entry should return a clear error and not send.
    captured.clear()
    resp = await read_console(ctx=DummyContext(), action="get", types='[1, "error"]')
    assert resp["success"] is False
    assert "types entries must be strings" in resp["message"]
    assert captured == {}