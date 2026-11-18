"""Tests for the execute_menu_item tool.

The tool is a thin pass-through to Unity's ``execute_menu_item`` command, so the
contract worth pinning is the payload shape (``menuPath``, camelCase) and the
response normalization (dict -> MCPResponse, anything else returned unchanged).
"""
from __future__ import annotations

import asyncio
from types import SimpleNamespace
from unittest.mock import AsyncMock

import pytest

import services.tools.execute_menu_item as execute_menu_item_mod
from services.tools.execute_menu_item import execute_menu_item
from services.registry import get_registered_tools


@pytest.fixture
def mock_send(monkeypatch):
    """Capture what the tool hands to the transport layer."""
    captured: dict[str, object] = {}

    async def fake_send(send_fn, unity_instance, tool_name, params):
        captured["send_fn"] = send_fn
        captured["unity_instance"] = unity_instance
        captured["tool_name"] = tool_name
        captured["params"] = params
        return {"success": True, "message": "Executed menu item."}

    monkeypatch.setattr(
        execute_menu_item_mod,
        "get_unity_instance_from_context",
        AsyncMock(return_value="MyProject@abcd1234"),
    )
    monkeypatch.setattr(
        execute_menu_item_mod,
        "send_with_unity_instance",
        fake_send,
    )
    return captured


def test_forwards_menu_path_as_camel_case(mock_send):
    """Unity expects ``menuPath``; a snake_case leak would silently no-op."""
    result = asyncio.run(
        execute_menu_item(SimpleNamespace(), menu_path="File/Save Project")
    )

    assert result.success is True
    assert mock_send["tool_name"] == "execute_menu_item"
    assert mock_send["params"] == {"menuPath": "File/Save Project"}


def test_forwards_resolved_unity_instance(mock_send):
    """Multi-instance routing must reach the transport helper."""
    asyncio.run(execute_menu_item(SimpleNamespace(), menu_path="File/Save Project"))

    assert mock_send["unity_instance"] == "MyProject@abcd1234"


def test_omits_menu_path_when_argument_is_absent(mock_send):
    """No path means an empty payload, not ``menuPath: null``."""
    asyncio.run(execute_menu_item(SimpleNamespace()))

    assert mock_send["params"] == {}


def test_omits_menu_path_when_argument_is_none(mock_send):
    asyncio.run(execute_menu_item(SimpleNamespace(), menu_path=None))

    assert mock_send["params"] == {}


def test_wraps_unity_dict_in_mcp_response(mock_send, monkeypatch):
    """Unity's extra keys must not break the MCPResponse construction."""

    async def fake_send(send_fn, unity_instance, tool_name, params):
        return {
            "success": True,
            "message": "Executed.",
            "data": {"menuPath": "File/Save Project"},
            "status": "success",
            "result": {"ignored": True},
        }

    monkeypatch.setattr(
        execute_menu_item_mod, "send_with_unity_instance", fake_send
    )

    result = asyncio.run(
        execute_menu_item(SimpleNamespace(), menu_path="File/Save Project")
    )

    assert result.success is True
    assert result.message == "Executed."
    assert result.data == {"menuPath": "File/Save Project"}


def test_surfaces_unity_error(monkeypatch):
    """A failed menu lookup must stay a failure after normalization."""

    async def fake_send(send_fn, unity_instance, tool_name, params):
        return {
            "success": False,
            "error": "menu_not_found",
            "message": "Menu item 'File/Nope' was not found.",
        }

    monkeypatch.setattr(
        execute_menu_item_mod,
        "get_unity_instance_from_context",
        AsyncMock(return_value=None),
    )
    monkeypatch.setattr(
        execute_menu_item_mod, "send_with_unity_instance", fake_send
    )

    result = asyncio.run(execute_menu_item(SimpleNamespace(), menu_path="File/Nope"))

    assert result.success is False
    assert result.error == "menu_not_found"
    assert "not found" in result.message


def test_non_dict_response_is_returned_unchanged(monkeypatch):
    """The transport may return a plain string; wrapping it would lose data."""

    async def fake_send(send_fn, unity_instance, tool_name, params):
        return "raw-transport-payload"

    monkeypatch.setattr(
        execute_menu_item_mod,
        "get_unity_instance_from_context",
        AsyncMock(return_value=None),
    )
    monkeypatch.setattr(
        execute_menu_item_mod, "send_with_unity_instance", fake_send
    )

    result = asyncio.run(execute_menu_item(SimpleNamespace(), menu_path="File/Save Project"))

    assert result == "raw-transport-payload"


def test_registered_as_core_tool():
    """Visibility defaults depend on group=core; keep that contract pinned."""
    registered = {tool["name"]: tool for tool in get_registered_tools()}
    tool = registered["execute_menu_item"]

    assert tool["group"] == "core"
    assert tool["unity_target"] == "execute_menu_item"
    assert tool["kwargs"]["annotations"].destructiveHint is True
