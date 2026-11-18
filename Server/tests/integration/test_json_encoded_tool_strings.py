"""Exercise MCP validation in a fresh interpreter, outside integration SDK stubs."""
import os
from pathlib import Path
import subprocess
import sys


def test_json_encoded_strings_through_real_mcp():
    # integration/conftest.py installs FastMCP stubs. A child interpreter lets this
    # regression use the real SDK without changing the shared test environment.
    script = r'''
import asyncio
import json
from fastmcp import Client, FastMCP
import services.tools.read_console as console
import services.tools.execute_menu_item as menu

calls = []

async def instance(_ctx):
    return "test-editor"

async def send(_sender, unity_instance, command, params):
    assert unity_instance == "test-editor"
    calls.append((command, dict(params)))
    return {"success": True, "message": "test response", "data": {"lines": []}}

async def main():
    for module in (console, menu):
        module.get_unity_instance_from_context = instance
        module.send_with_unity_instance = send
    server = FastMCP("JSON string argument tests")
    server.tool(console.read_console)
    server.tool(menu.execute_menu_item)
    async with Client(server) as client:
        tools = await client.list_tools()
        schema = next(tool.inputSchema for tool in tools if tool.name == "read_console")
        options = schema["properties"]["action"]["anyOf"]
        assert next(option["enum"] for option in options if "enum" in option) == ["get", "clear"]

        filter_text = '\"build failed\"'
        await client.call_tool("read_console", {
            "action": "get", "format": "json", "filter_text": filter_text,
        })
        assert calls[-1][1]["action"] == "get"
        assert calls[-1][1]["format"] == "json"
        assert calls[-1][1]["filterText"] == filter_text

        await client.call_tool("read_console", {
            "action": json.dumps("get"), "format": json.dumps("plain"),
        })
        assert calls[-1][0] == "read_console"
        assert calls[-1][1]["action"] == "get"
        assert calls[-1][1]["format"] == "plain"
        assert calls[-1][1]["count"] == 10

        await client.call_tool("read_console", {"action": json.dumps("clear")})
        assert calls[-1][1]["action"] == "clear"

        count = len(calls)
        for action in ['\"delete\"', '\"get', json.dumps(json.dumps("get")), 42]:
            result = await client.call_tool("read_console", {"action": action}, raise_on_error=False)
            assert result.is_error
        assert len(calls) == count, "Invalid actions must never reach Unity"

        path = "Tools/MCPForUnityTests/Quoted argument probe"
        for supplied in [path, json.dumps(path), json.dumps(json.dumps(path))]:
            await client.call_tool("execute_menu_item", {"menu_path": supplied})
            assert calls[-1] == ("execute_menu_item", {"menuPath": supplied})

    # Direct Python callers bypass Pydantic; they must obey the same one-layer limit.
    await console.read_console(ctx=None, action=json.dumps("get"), format=json.dumps("plain"))
    assert calls[-1][1]["action"] == "get"
    count = len(calls)
    for action in [json.dumps(json.dumps("get")), json.dumps("delete")]:
        result = await console.read_console(ctx=None, action=action)
        assert result["success"] is False
    assert len(calls) == count

asyncio.run(main())
print("real MCP argument validation passed")
'''
    server_root = Path(__file__).resolve().parents[2]
    env = dict(os.environ, PYTHONPATH=str(server_root / "src"),
               DISABLE_TELEMETRY="true", UNITY_MCP_DISABLE_TELEMETRY="true")
    result = subprocess.run([sys.executable, "-c", script], cwd=server_root,
                            env=env, capture_output=True, text=True, timeout=45)
    assert result.returncode == 0, result.stdout + result.stderr
    assert "real MCP argument validation passed" in result.stdout
