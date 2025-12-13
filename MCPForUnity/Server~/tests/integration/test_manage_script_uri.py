import pytest

from .test_helpers import DummyContext


class DummyMCP:
    def __init__(self):
        self.tools = {}

    def tool(self, *args, **kwargs):  # ignore decorator kwargs like description
        def _decorator(fn):
            self.tools[fn.__name__] = fn
            return fn
        return _decorator


def _register_tools():
    mcp = DummyMCP()
    # Import the tools module to trigger decorator registration
    import services.tools.manage_script  # trigger decorator registration
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
async def test_split_uri_unity_path(monkeypatch):
    test_tools = _register_tools()
    captured = {}

    async def fake_send(cmd, params, **kwargs):  # capture params and return success
        captured['cmd'] = cmd
        captured['params'] = params
        return {"success": True, "message": "ok"}

    # Patch the send_command_with_retry function at the module level where it's imported
    import transport.legacy.unity_connection
    monkeypatch.setattr(
        transport.legacy.unity_connection,
        "async_send_command_with_retry",
        fake_send,
    )
    # No need to patch tools.manage_script; it now calls unity_connection.send_command_with_retry

    fn = test_tools['apply_text_edits']
    uri = "unity://path/Assets/Scripts/MyScript.cs"
    await fn(DummyContext(), uri=uri, edits=[], precondition_sha256=None)

    assert captured['cmd'] == 'manage_script'
    assert captured['params']['name'] == 'MyScript'
    assert captured['params']['path'] == 'Assets/Scripts'


@pytest.mark.asyncio
@pytest.mark.parametrize(
    "uri, expected_name, expected_path",
    [
        ("file:///Users/alex/Project/Assets/Scripts/Foo%20Bar.cs",
         "Foo Bar", "Assets/Scripts"),
        ("file://localhost/Users/alex/Project/Assets/Hello.cs", "Hello", "Assets"),
        ("file:///C:/Users/Alex/Proj/Assets/Scripts/Hello.cs",
         "Hello", "Assets/Scripts"),
        # outside Assets → fall back to normalized dir
        ("file:///tmp/Other.cs", "Other", "tmp"),
    ],
)
async def test_split_uri_file_urls(monkeypatch, uri, expected_name, expected_path):
    test_tools = _register_tools()
    captured = {}

    async def fake_send(_cmd, params, **kwargs):
        captured['cmd'] = _cmd
        captured['params'] = params
        return {"success": True, "message": "ok"}

    # Patch the send_command_with_retry function at the module level where it's imported
    import transport.legacy.unity_connection
    monkeypatch.setattr(
        transport.legacy.unity_connection,
        "async_send_command_with_retry",
        fake_send,
    )
    # No need to patch tools.manage_script; it now calls unity_connection.send_command_with_retry

    fn = test_tools['apply_text_edits']
    await fn(DummyContext(), uri=uri, edits=[], precondition_sha256=None)

    assert captured['params']['name'] == expected_name
    assert captured['params']['path'] == expected_path


@pytest.mark.asyncio
async def test_split_uri_plain_path(monkeypatch):
    test_tools = _register_tools()
    captured = {}

    async def fake_send(_cmd, params, **kwargs):
        captured['params'] = params
        return {"success": True, "message": "ok"}

    # Patch the send_command_with_retry function at the module level where it's imported
    import transport.legacy.unity_connection
    monkeypatch.setattr(
        transport.legacy.unity_connection,
        "async_send_command_with_retry",
        fake_send,
    )
    # No need to patch tools.manage_script; it now calls unity_connection.send_command_with_retry

    fn = test_tools['apply_text_edits']
    await fn(
        DummyContext(),
        uri="Assets/Scripts/Thing.cs",
        edits=[],
        precondition_sha256=None,
    )

    assert captured['params']['name'] == 'Thing'
    assert captured['params']['path'] == 'Assets/Scripts'
