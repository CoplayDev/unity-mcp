"""Tests for search_missing_references tool."""
import asyncio
from types import SimpleNamespace
from unittest.mock import AsyncMock

import pytest

from services.tools.search_missing_references import search_missing_references


@pytest.fixture
def mock_unity(monkeypatch):
    captured: dict[str, object] = {}

    async def fake_send(send_fn, unity_instance, tool_name, params):
        captured["unity_instance"] = unity_instance
        captured["tool_name"] = tool_name
        captured["params"] = params
        return {"success": True, "message": "ok"}

    monkeypatch.setattr(
        "services.tools.search_missing_references.get_unity_instance_from_context",
        AsyncMock(return_value="unity-instance-1"),
    )
    monkeypatch.setattr(
        "services.tools.search_missing_references.send_with_unity_instance",
        fake_send,
    )
    monkeypatch.setattr(
        "services.tools.search_missing_references.preflight",
        AsyncMock(return_value=None),
    )
    return captured


def test_default_scope_is_scene(mock_unity):
    result = asyncio.run(search_missing_references(SimpleNamespace()))
    assert result["success"] is True
    assert mock_unity["params"]["scope"] == "scene"
    assert mock_unity["tool_name"] == "search_missing_references"


def test_project_scope_passes(mock_unity):
    result = asyncio.run(search_missing_references(SimpleNamespace(), scope="project"))
    assert result["success"] is True
    assert mock_unity["params"]["scope"] == "project"
    assert mock_unity["tool_name"] == "search_missing_references"


def test_path_filter_passes(mock_unity):
    result = asyncio.run(
        search_missing_references(
            SimpleNamespace(),
            scope="project",
            path_filter="Assets/Prefabs",
        )
    )
    assert result["success"] is True
    assert mock_unity["params"]["pathFilter"] == "Assets/Prefabs"
    assert mock_unity["tool_name"] == "search_missing_references"


def test_component_filter_passes(mock_unity):
    result = asyncio.run(
        search_missing_references(
            SimpleNamespace(),
            component_filter="MeshRenderer",
        )
    )
    assert result["success"] is True
    assert mock_unity["params"]["componentFilter"] == "MeshRenderer"
    assert mock_unity["tool_name"] == "search_missing_references"


def test_auto_repair_passes(mock_unity):
    result = asyncio.run(
        search_missing_references(
            SimpleNamespace(),
            auto_repair=True,
        )
    )
    assert result["success"] is True
    assert mock_unity["params"]["autoRepair"] is True
    assert mock_unity["tool_name"] == "search_missing_references"


def test_auto_repair_default_omitted(mock_unity):
    result = asyncio.run(search_missing_references(SimpleNamespace()))
    assert result["success"] is True
    assert "autoRepair" not in mock_unity["params"]
    assert mock_unity["tool_name"] == "search_missing_references"


def test_include_flags_pass_through(mock_unity):
    result = asyncio.run(
        search_missing_references(
            SimpleNamespace(),
            include_missing_scripts=False,
        )
    )
    assert result["success"] is True
    assert mock_unity["params"]["includeMissingScripts"] is False
    assert mock_unity["tool_name"] == "search_missing_references"


def test_pagination_passes(mock_unity):
    result = asyncio.run(
        search_missing_references(
            SimpleNamespace(),
            page_size=50,
            cursor=100,
        )
    )
    assert result["success"] is True
    assert mock_unity["params"]["pageSize"] == 50
    assert mock_unity["params"]["cursor"] == 100
    assert mock_unity["tool_name"] == "search_missing_references"


def test_none_params_omitted(mock_unity):
    result = asyncio.run(search_missing_references(SimpleNamespace()))
    assert result["success"] is True
    params = mock_unity["params"]
    assert "pathFilter" not in params
    assert "componentFilter" not in params
    assert "autoRepair" not in params
    assert mock_unity["tool_name"] == "search_missing_references"


def test_path_filter_omitted_for_scene_scope(mock_unity):
    result = asyncio.run(
        search_missing_references(
            SimpleNamespace(),
            scope="scene",
            path_filter=None,
        )
    )
    assert result["success"] is True
    assert "pathFilter" not in mock_unity["params"]
    assert mock_unity["tool_name"] == "search_missing_references"
