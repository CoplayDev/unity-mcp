from __future__ import annotations

import asyncio
from types import SimpleNamespace
from unittest.mock import AsyncMock

import pytest

from services.registry.tool_registry import TOOL_GROUPS, DEFAULT_ENABLED_GROUPS
from services.tools.unity_reflect import (
    unity_reflect,
    ALL_ACTIONS,
    VALID_SCOPES,
)


# ---------------------------------------------------------------------------
# Tool group registration
# ---------------------------------------------------------------------------

def test_docs_group_exists():
    assert "docs" in TOOL_GROUPS


def test_docs_group_not_in_defaults():
    assert "docs" not in DEFAULT_ENABLED_GROUPS


# ---------------------------------------------------------------------------
# Fixtures
# ---------------------------------------------------------------------------

@pytest.fixture
def mock_unity(monkeypatch):
    """Patch Unity transport layer and return captured call dict."""
    captured: dict[str, object] = {}

    async def fake_send(send_fn, unity_instance, tool_name, params):
        captured["unity_instance"] = unity_instance
        captured["tool_name"] = tool_name
        captured["params"] = params
        return {"success": True, "message": "ok"}

    monkeypatch.setattr(
        "services.tools.unity_reflect.get_unity_instance_from_context",
        AsyncMock(return_value="unity-instance-1"),
    )
    monkeypatch.setattr(
        "services.tools.unity_reflect.send_with_unity_instance",
        fake_send,
    )
    return captured


@pytest.fixture
def ctx():
    return SimpleNamespace(info=AsyncMock(), warning=AsyncMock())


# ---------------------------------------------------------------------------
# Action list completeness
# ---------------------------------------------------------------------------

def test_all_actions_list():
    assert ALL_ACTIONS == ["get_type", "get_member", "search"]


def test_no_duplicate_actions():
    assert len(ALL_ACTIONS) == len(set(ALL_ACTIONS))


# ---------------------------------------------------------------------------
# Unknown action
# ---------------------------------------------------------------------------

def test_unknown_action_returns_error(mock_unity):
    result = asyncio.run(
        unity_reflect(SimpleNamespace(), action="nonexistent_action")
    )
    assert result["success"] is False
    assert "Unknown action" in result["message"]
    assert "tool_name" not in mock_unity


# ---------------------------------------------------------------------------
# get_type
# ---------------------------------------------------------------------------

def test_get_type_sends_correct_params(mock_unity):
    result = asyncio.run(
        unity_reflect(SimpleNamespace(), action="get_type", class_name="UnityEngine.Transform")
    )
    assert result["success"] is True
    assert mock_unity["tool_name"] == "unity_reflect"
    assert mock_unity["params"]["action"] == "get_type"
    assert mock_unity["params"]["class_name"] == "UnityEngine.Transform"


def test_get_type_requires_class_name(mock_unity):
    result = asyncio.run(
        unity_reflect(SimpleNamespace(), action="get_type")
    )
    assert result["success"] is False
    assert "class_name" in result["message"]
    assert "tool_name" not in mock_unity


# ---------------------------------------------------------------------------
# get_member
# ---------------------------------------------------------------------------

def test_get_member_sends_correct_params(mock_unity):
    result = asyncio.run(
        unity_reflect(
            SimpleNamespace(),
            action="get_member",
            class_name="UnityEngine.Transform",
            member_name="position",
        )
    )
    assert result["success"] is True
    assert mock_unity["tool_name"] == "unity_reflect"
    assert mock_unity["params"]["action"] == "get_member"
    assert mock_unity["params"]["class_name"] == "UnityEngine.Transform"
    assert mock_unity["params"]["member_name"] == "position"


def test_get_member_requires_class_name_and_member_name(mock_unity):
    # Missing both
    result = asyncio.run(
        unity_reflect(SimpleNamespace(), action="get_member")
    )
    assert result["success"] is False
    assert "class_name" in result["message"]
    assert "tool_name" not in mock_unity


def test_get_member_requires_member_name(mock_unity):
    # Has class_name but missing member_name
    result = asyncio.run(
        unity_reflect(
            SimpleNamespace(),
            action="get_member",
            class_name="UnityEngine.Transform",
        )
    )
    assert result["success"] is False
    assert "member_name" in result["message"]
    assert "tool_name" not in mock_unity


# ---------------------------------------------------------------------------
# search
# ---------------------------------------------------------------------------

def test_search_sends_correct_params(mock_unity):
    result = asyncio.run(
        unity_reflect(SimpleNamespace(), action="search", query="Rigidbody")
    )
    assert result["success"] is True
    assert mock_unity["tool_name"] == "unity_reflect"
    assert mock_unity["params"]["action"] == "search"
    assert mock_unity["params"]["query"] == "Rigidbody"


def test_search_default_scope(mock_unity):
    """When scope is not provided, it should not appear in params."""
    result = asyncio.run(
        unity_reflect(SimpleNamespace(), action="search", query="Camera")
    )
    assert result["success"] is True
    assert "scope" not in mock_unity["params"]


def test_search_custom_scope(mock_unity):
    result = asyncio.run(
        unity_reflect(
            SimpleNamespace(),
            action="search",
            query="Camera",
            scope="unity",
        )
    )
    assert result["success"] is True
    assert mock_unity["params"]["scope"] == "unity"


def test_search_requires_query(mock_unity):
    result = asyncio.run(
        unity_reflect(SimpleNamespace(), action="search")
    )
    assert result["success"] is False
    assert "query" in result["message"]
    assert "tool_name" not in mock_unity


# ---------------------------------------------------------------------------
# Scope not sent for non-search actions
# ---------------------------------------------------------------------------

def test_scope_not_sent_for_get_type(mock_unity):
    """scope should only be included for the search action."""
    result = asyncio.run(
        unity_reflect(
            SimpleNamespace(),
            action="get_type",
            class_name="UnityEngine.Transform",
            scope="unity",
        )
    )
    assert result["success"] is True
    assert "scope" not in mock_unity["params"]


# ---------------------------------------------------------------------------
# Case insensitivity
# ---------------------------------------------------------------------------

def test_action_case_insensitive(mock_unity):
    result = asyncio.run(
        unity_reflect(
            SimpleNamespace(),
            action="Get_Type",
            class_name="UnityEngine.Transform",
        )
    )
    assert result["success"] is True
    assert mock_unity["params"]["action"] == "get_type"


# ---------------------------------------------------------------------------
# Non-dict response
# ---------------------------------------------------------------------------

def test_non_dict_response_wrapped(monkeypatch):
    """When Unity returns a non-dict, it should be wrapped."""
    monkeypatch.setattr(
        "services.tools.unity_reflect.get_unity_instance_from_context",
        AsyncMock(return_value="unity-1"),
    )

    async def fake_send(send_fn, unity_instance, tool_name, params):
        return "unexpected string response"

    monkeypatch.setattr(
        "services.tools.unity_reflect.send_with_unity_instance",
        fake_send,
    )

    result = asyncio.run(
        unity_reflect(
            SimpleNamespace(),
            action="get_type",
            class_name="UnityEngine.Transform",
        )
    )
    assert result["success"] is False
    assert "unexpected string response" in result["message"]
