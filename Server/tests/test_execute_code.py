"""Tests for execute_code tool."""
import asyncio
from types import SimpleNamespace
from unittest.mock import AsyncMock

import pytest

from services.tools.execute_code import execute_code


@pytest.fixture
def mock_unity(monkeypatch):
    captured: dict[str, object] = {}

    async def fake_send(send_fn, unity_instance, tool_name, params):
        captured["tool_name"] = tool_name
        captured["params"] = params
        return {"success": True, "message": "Code executed successfully.", "data": {"result": 42}}

    monkeypatch.setattr(
        "services.tools.execute_code.get_unity_instance_from_context",
        AsyncMock(return_value="unity-instance-1"),
    )
    monkeypatch.setattr(
        "services.tools.execute_code.send_with_unity_instance",
        fake_send,
    )
    return captured


@pytest.fixture
def mock_unity_error(monkeypatch):
    async def fake_send(send_fn, unity_instance, tool_name, params):
        return {"success": False, "error": "Compilation failed"}

    monkeypatch.setattr(
        "services.tools.execute_code.get_unity_instance_from_context",
        AsyncMock(return_value="unity-instance-1"),
    )
    monkeypatch.setattr(
        "services.tools.execute_code.send_with_unity_instance",
        fake_send,
    )


# --- execute action ---

def test_execute_forwards_code_to_unity(mock_unity):
    result = asyncio.run(execute_code(SimpleNamespace(), action="execute", code="return 42;"))
    assert result["success"] is True
    assert mock_unity["tool_name"] == "execute_code"
    assert mock_unity["params"]["code"] == "return 42;"
    assert mock_unity["params"]["action"] == "execute"


def test_execute_sends_safety_checks_true_by_default(mock_unity):
    asyncio.run(execute_code(SimpleNamespace(), action="execute", code="return 1;"))
    assert mock_unity["params"]["safety_checks"] is True


def test_execute_sends_safety_checks_false(mock_unity):
    asyncio.run(execute_code(SimpleNamespace(), action="execute", code="x();", safety_checks=False))
    assert mock_unity["params"]["safety_checks"] is False


def test_execute_returns_data(mock_unity):
    result = asyncio.run(execute_code(SimpleNamespace(), action="execute", code="return 42;"))
    assert result["data"]["result"] == 42


def test_execute_requires_code():
    result = asyncio.run(execute_code(SimpleNamespace(), action="execute", code=None))
    assert result["success"] is False
    assert "code" in result["message"].lower()


# --- get_history action ---

def test_get_history_forwards_to_unity(mock_unity):
    asyncio.run(execute_code(SimpleNamespace(), action="get_history", limit=5))
    assert mock_unity["params"]["action"] == "get_history"
    assert mock_unity["params"]["limit"] == 5


def test_get_history_default_limit(mock_unity):
    asyncio.run(execute_code(SimpleNamespace(), action="get_history"))
    assert mock_unity["params"]["limit"] == 10


def test_get_history_clamps_limit(mock_unity):
    asyncio.run(execute_code(SimpleNamespace(), action="get_history", limit=999))
    assert mock_unity["params"]["limit"] == 50


def test_get_history_clamps_negative_limit(mock_unity):
    asyncio.run(execute_code(SimpleNamespace(), action="get_history", limit=-5))
    assert mock_unity["params"]["limit"] == 1


# --- replay action ---

def test_replay_forwards_index(mock_unity):
    asyncio.run(execute_code(SimpleNamespace(), action="replay", index=3))
    assert mock_unity["params"]["action"] == "replay"
    assert mock_unity["params"]["index"] == 3


def test_replay_requires_index():
    result = asyncio.run(execute_code(SimpleNamespace(), action="replay", index=None))
    assert result["success"] is False
    assert "index" in result["message"].lower()


# --- clear_history action ---

def test_clear_history_forwards(mock_unity):
    asyncio.run(execute_code(SimpleNamespace(), action="clear_history"))
    assert mock_unity["params"]["action"] == "clear_history"


# --- error handling ---

def test_error_response_normalized(mock_unity_error):
    result = asyncio.run(execute_code(SimpleNamespace(), action="execute", code="bad"))
    assert result["success"] is False
    assert "Compilation failed" in result["message"]


def test_non_dict_response_handled(monkeypatch):
    async def fake_send(send_fn, unity_instance, tool_name, params):
        return "unexpected"

    monkeypatch.setattr(
        "services.tools.execute_code.get_unity_instance_from_context",
        AsyncMock(return_value="unity-instance-1"),
    )
    monkeypatch.setattr(
        "services.tools.execute_code.send_with_unity_instance",
        fake_send,
    )
    result = asyncio.run(execute_code(SimpleNamespace(), action="execute", code="return 1;"))
    assert result["success"] is False


# --- param isolation ---

def test_execute_omits_irrelevant_params(mock_unity):
    asyncio.run(execute_code(SimpleNamespace(), action="execute", code="return 1;"))
    assert "index" not in mock_unity["params"]
    assert "limit" not in mock_unity["params"]


def test_history_omits_irrelevant_params(mock_unity):
    asyncio.run(execute_code(SimpleNamespace(), action="get_history"))
    assert "code" not in mock_unity["params"]
    assert "index" not in mock_unity["params"]
    assert "safety_checks" not in mock_unity["params"]


def test_replay_omits_irrelevant_params(mock_unity):
    asyncio.run(execute_code(SimpleNamespace(), action="replay", index=0))
    assert "code" not in mock_unity["params"]
    assert "limit" not in mock_unity["params"]
    assert "safety_checks" not in mock_unity["params"]
