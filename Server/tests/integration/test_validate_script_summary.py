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
    import services.tools.manage_script
    # Get the registered tools from the registry
    from services.registry import get_registered_tools
    registered_tools = get_registered_tools()
    # Add all script-related tools to our dummy MCP
    for tool_info in registered_tools:
        tool_name = tool_info['name']
        if any(keyword in tool_name for keyword in ['script', 'apply_text', 'create_script', 'delete_script', 'validate_script', 'get_sha']):
            mcp.tools[tool_name] = tool_info['func']
    return mcp.tools


@pytest.mark.asyncio
async def test_validate_script_returns_counts(monkeypatch):
    tools = setup_tools()
    validate_script = tools["validate_script"]

    async def fake_send(cmd, params, **kwargs):
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

    # Patch the send_command_with_retry function at the module level where it's imported
    import transport.legacy.unity_connection
    monkeypatch.setattr(transport.legacy.unity_connection,
                        "async_send_command_with_retry", fake_send)
    # No need to patch tools.manage_script; it now calls unity_connection.send_command_with_retry

    resp = await validate_script(DummyContext(), uri="unity://path/Assets/Scripts/A.cs")
    assert resp == {"success": True, "data": {"warnings": 1, "errors": 2}}
