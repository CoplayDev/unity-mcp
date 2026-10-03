import asyncio
import copy
import json
import os
from pathlib import Path
import subprocess
import sys
from types import SimpleNamespace
from unittest.mock import AsyncMock

import pytest
from mcp.types import ImageContent, TextContent

from services.tools.manage_editor_windows import manage_editor_windows


@pytest.fixture
def transport(monkeypatch):
    """Isolate instance selection and Unity transport so tool tests cannot reach a running Editor."""
    target = "services.tools.manage_editor_windows"
    send = AsyncMock(return_value={"success": True, "data": {"windows": []}})
    monkeypatch.setattr(target + ".get_unity_instance_from_context", AsyncMock(return_value="test-instance"))
    monkeypatch.setattr(target + ".send_with_unity_instance", send)
    return send


def call(**kwargs):
    """Invoke the asynchronous tool with a minimal context in a private event loop."""
    return asyncio.run(manage_editor_windows(SimpleNamespace(), **kwargs))


def test_list_uses_request_instance_without_capture_side_effects(transport):
    """Listing sends only the list action through the context-selected instance."""
    assert call()["success"]
    assert transport.call_args.args[1:] == ("test-instance", "manage_editor_windows", {"action": "list"})


@pytest.mark.parametrize("selector", [{"window_id": 42}, {"window_title": "Inspector"}, {"window_type": "UnityEditor.ConsoleWindow"}, {}])
def test_capture_routes_selector_and_privacy_defaults(transport, selector):
    """Every selector preserves inline-only output and default focus restoration."""
    call(action="screenshot", **selector)
    params = transport.call_args.args[3]
    assert all(params[key] == value for key, value in selector.items())
    assert params["save_file"] is False
    assert params["include_image"] is True
    assert params["restore_focus"] is True


@pytest.mark.parametrize("kwargs", [
    {"action": "close"}, {"action": "screenshot", "window_id": 1, "window_title": "Inspector"},
    {"action": "screenshot", "max_resolution": 63}, {"action": "screenshot", "max_resolution": 4097},
    {"action": "screenshot", "max_resolution": True},
    {"action": "screenshot", "window_id": True},
    {"action": "screenshot", "window_id": "not-an-id"},
    {"action": "screenshot", "window_title": "  "},
    {"action": "screenshot", "window_type": ""},
    {"action": "screenshot", "include_image": False, "save_file": False},
])
def test_invalid_requests_do_not_reach_unity(transport, kwargs):
    """Invalid actions, selectors, sizes, and output combinations fail before transport."""
    assert call(**kwargs)["success"] is False
    transport.assert_not_called()


def test_image_bytes_appear_once_with_metadata_preserved(transport):
    """One assistant-audience image carries pixels while metadata and the source response remain intact."""
    value = {"success": True, "message": "captured", "data": {
        "imageBase64": "aW1hZ2U=", "mimeType": "image/png", "path": None, "window": {"window_id": 42},
    }}
    transport.return_value = value
    original = copy.deepcopy(value)
    result = call(action="screenshot")
    assert len(result.content) == 2
    assert isinstance(result.content[0], TextContent)
    assert isinstance(result.content[1], ImageContent)
    assert result.content[1].data == "aW1hZ2U="
    annotations = result.content[1].annotations
    audience = annotations["audience"] if isinstance(annotations, dict) else annotations.audience
    assert audience == ["assistant"]
    metadata = json.loads(result.content[0].text)
    assert metadata["data"]["window"]["window_id"] == 42
    assert "imageBase64" not in metadata["data"]
    assert value == original


def test_file_only_capture_and_unity_errors_are_preserved(transport):
    """File-only results and structured Unity errors pass through without image conversion."""
    transport.return_value = {"success": True, "data": {"path": "Library/McpEditorScreenshots/test.png"}}
    assert call(action="screenshot", include_image=False, save_file=True) == transport.return_value
    transport.return_value = {"success": False, "error": "busy"}
    assert call(action="screenshot") == transport.return_value


def test_native_mcp_protocol_returns_image_and_rejects_unknown_arguments():
    # The integration suite installs FastMCP stubs during collection. A child
    # process verifies real wire types without changing that suite's environment.
    """A child process checks real MCP image types, annotations, and rejection of unknown output paths."""
    script = '''
import asyncio
from unittest.mock import AsyncMock, patch
from fastmcp import Client, FastMCP
from mcp.types import ImageContent
from services.registry.tool_registry import get_registered_tools
from services.tools.manage_editor_windows import manage_editor_windows

async def run():
    server = FastMCP("screenshot-tests")
    registration = next(t for t in get_registered_tools() if t["name"] == "manage_editor_windows")
    server.tool(name=registration["name"], description=registration["description"],
                **registration["kwargs"])(registration["func"])
    async with Client(server) as client:
        tool = next(t for t in await client.list_tools() if t.name == "manage_editor_windows")
        assert tool.annotations.readOnlyHint is False
        assert tool.annotations.destructiveHint is False
        result = await client.call_tool("manage_editor_windows", {"action": "screenshot", "window_id": 42})
        assert len(result.content) == 2
        assert isinstance(result.content[1], ImageContent)
        assert result.content[1].annotations.audience == ["assistant"]
        assert result.content[1].model_dump()["annotations"]["audience"] == ["assistant"]
        assert "imageBase64" not in result.content[0].text
        try:
            await client.call_tool("manage_editor_windows", {"action": "screenshot", "output_folder": "../private"})
        except Exception:
            pass
        else:
            raise AssertionError("Unknown output path argument was accepted")

with patch("services.tools.manage_editor_windows.get_unity_instance_from_context", AsyncMock(return_value="test-instance")), \\
     patch("services.tools.manage_editor_windows.send_with_unity_instance", AsyncMock(return_value={"success": True, "data": {"imageBase64": "aW1hZ2U=", "path": None}})):
    asyncio.run(run())
'''
    env = os.environ.copy()
    env["PYTHONPATH"] = str(Path(__file__).resolve().parents[1] / "src")
    result = subprocess.run([sys.executable, "-c", script], env=env, capture_output=True, text=True, timeout=45)
    assert result.returncode == 0, result.stdout + result.stderr


@pytest.mark.parametrize("saved_path", [None, "Library/McpEditorScreenshots/requested.png"])
@pytest.mark.parametrize("failure", [ValueError, TypeError])
def test_image_conversion_failure_reports_saved_path_without_pixels(transport, monkeypatch, saved_path, failure):
    """Response conversion errors must retain cleanup metadata without leaking encoded pixels."""
    value = {"success": True, "message": "captured", "data": {
        "imageBase64": "private-pixels", "path": saved_path, "window": {"window_id": 42},
    }}
    original = copy.deepcopy(value)
    transport.return_value = value

    def fail_conversion(*args, **kwargs):
        """Fail after Unity has returned its successful optional-file result."""
        raise failure("image conversion failed")

    monkeypatch.setattr("services.tools.manage_editor_windows.extract_screenshot_images", fail_conversion)
    result = call(action="screenshot", save_file=saved_path is not None)
    assert result["success"] is False
    assert "image response" in result["message"]
    assert result["data"]["path"] == saved_path
    assert result["data"]["window"]["window_id"] == 42
    assert "imageBase64" not in result["data"]
    assert "private-pixels" not in json.dumps(result)
    assert value == original
