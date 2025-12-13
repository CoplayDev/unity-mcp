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

    async def fake_send(cmd, params, **kwargs):
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
        "data": {"lines": [{"level": "error", "message": "oops", "stacktrace": "trace", "time": "t"}]},
    }
    assert captured["params"]["count"] == 10
    assert captured["params"]["includeStacktrace"] is True


@pytest.mark.asyncio
async def test_read_console_truncated(monkeypatch):
    tools = setup_tools()
    read_console = tools["read_console"]

    captured = {}

    async def fake_send(cmd, params, **kwargs):
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
