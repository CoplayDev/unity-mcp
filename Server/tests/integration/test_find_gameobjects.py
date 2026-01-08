"""
Tests for the find_gameobjects tool.

This tool provides paginated GameObject search, returning instance IDs only.
"""
import pytest

from .test_helpers import DummyContext
import services.tools.find_gameobjects as find_go_mod


@pytest.mark.asyncio
async def test_find_gameobjects_basic_search(monkeypatch):
    """Test basic search returns instance IDs."""
    captured = {}

    async def fake_send(cmd, params, **kwargs):
        captured["cmd"] = cmd
        captured["params"] = params
        return {
            "success": True,
            "data": {
                "instanceIDs": [12345, 67890],
                "pageSize": 25,
                "cursor": 0,
                "totalCount": 2,
                "hasMore": False,
            },
        }

    monkeypatch.setattr(
        find_go_mod,
        "async_send_command_with_retry",
        fake_send,
    )

    resp = await find_go_mod.find_gameobjects(
        ctx=DummyContext(),
        search_term="Player",
        search_method="by_name",
    )

    assert resp.get("success") is True
    assert captured["cmd"] == "find_gameobjects"
    assert captured["params"]["searchTerm"] == "Player"
    assert captured["params"]["searchMethod"] == "by_name"


@pytest.mark.asyncio
async def test_find_gameobjects_by_component(monkeypatch):
    """Test search by component type."""
    captured = {}

    async def fake_send(cmd, params, **kwargs):
        captured["params"] = params
        return {
            "success": True,
            "data": {
                "instanceIDs": [111, 222, 333],
                "pageSize": 25,
                "cursor": 0,
                "totalCount": 3,
                "hasMore": False,
            },
        }

    monkeypatch.setattr(
        find_go_mod,
        "async_send_command_with_retry",
        fake_send,
    )

    resp = await find_go_mod.find_gameobjects(
        ctx=DummyContext(),
        search_term="Camera",
        search_method="by_component",
    )

    assert resp.get("success") is True
    assert captured["params"]["searchTerm"] == "Camera"
    assert captured["params"]["searchMethod"] == "by_component"


@pytest.mark.asyncio
async def test_find_gameobjects_pagination_params(monkeypatch):
    """Test pagination parameters are passed correctly."""
    captured = {}

    async def fake_send(cmd, params, **kwargs):
        captured["params"] = params
        return {
            "success": True,
            "data": {
                "instanceIDs": [444, 555],
                "pageSize": 10,
                "cursor": 20,
                "totalCount": 50,
                "hasMore": True,
                "nextCursor": "30",
            },
        }

    monkeypatch.setattr(
        find_go_mod,
        "async_send_command_with_retry",
        fake_send,
    )

    resp = await find_go_mod.find_gameobjects(
        ctx=DummyContext(),
        search_term="Enemy",
        search_method="by_tag",
        page_size="10",
        cursor="20",
    )

    assert resp.get("success") is True
    p = captured["params"]
    assert p["pageSize"] == 10
    assert p["cursor"] == 20


@pytest.mark.asyncio
async def test_find_gameobjects_boolean_coercion(monkeypatch):
    """Test boolean string coercion for include_inactive."""
    captured = {}

    async def fake_send(cmd, params, **kwargs):
        captured["params"] = params
        return {"success": True, "data": {"instanceIDs": []}}

    monkeypatch.setattr(
        find_go_mod,
        "async_send_command_with_retry",
        fake_send,
    )

    resp = await find_go_mod.find_gameobjects(
        ctx=DummyContext(),
        search_term="HiddenObject",
        search_method="by_name",
        include_inactive="true",
    )

    assert resp.get("success") is True
    p = captured["params"]
    assert p["includeInactive"] is True


@pytest.mark.asyncio
async def test_find_gameobjects_by_layer(monkeypatch):
    """Test search by layer."""
    captured = {}

    async def fake_send(cmd, params, **kwargs):
        captured["params"] = params
        return {"success": True, "data": {"instanceIDs": [999]}}

    monkeypatch.setattr(
        find_go_mod,
        "async_send_command_with_retry",
        fake_send,
    )

    resp = await find_go_mod.find_gameobjects(
        ctx=DummyContext(),
        search_term="UI",
        search_method="by_layer",
    )

    assert resp.get("success") is True
    assert captured["params"]["searchMethod"] == "by_layer"
    assert captured["params"]["searchTerm"] == "UI"


@pytest.mark.asyncio
async def test_find_gameobjects_by_path(monkeypatch):
    """Test search by hierarchy path."""
    captured = {}

    async def fake_send(cmd, params, **kwargs):
        captured["params"] = params
        return {"success": True, "data": {"instanceIDs": [777]}}

    monkeypatch.setattr(
        find_go_mod,
        "async_send_command_with_retry",
        fake_send,
    )

    resp = await find_go_mod.find_gameobjects(
        ctx=DummyContext(),
        search_term="Canvas/Panel/Button",
        search_method="by_path",
    )

    assert resp.get("success") is True
    assert captured["params"]["searchMethod"] == "by_path"
    assert captured["params"]["searchTerm"] == "Canvas/Panel/Button"

