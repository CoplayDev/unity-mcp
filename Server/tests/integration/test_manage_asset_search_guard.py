import pytest

from .test_helpers import DummyContext
from services.tools.manage_asset import manage_asset


@pytest.mark.asyncio
async def test_search_without_scope_or_filter_returns_error(monkeypatch):
    async def fail_if_dispatched(*args, **kwargs):
        raise AssertionError("empty asset search should not dispatch to Unity")

    monkeypatch.setattr(
        "services.tools.manage_asset.send_with_unity_instance",
        fail_if_dispatched,
    )

    result = await manage_asset(
        ctx=DummyContext(),
        action="search",
        path="",
    )

    assert result["success"] is False
    assert "search_pattern" in result["message"]


@pytest.mark.asyncio
async def test_search_date_filter_without_scope_or_filter_returns_error(monkeypatch):
    async def fail_if_dispatched(*args, **kwargs):
        raise AssertionError("date-only asset search should not dispatch to Unity")

    monkeypatch.setattr(
        "services.tools.manage_asset.send_with_unity_instance",
        fail_if_dispatched,
    )

    result = await manage_asset(
        ctx=DummyContext(),
        action="search",
        path="",
        filter_date_after="2026-01-01T00:00:00Z",
    )

    assert result["success"] is False
    assert "filter_type" in result["message"]


@pytest.mark.asyncio
async def test_search_treats_non_folder_path_as_search_pattern(monkeypatch):
    captured = {}

    async def fake_send(send_fn, unity_instance, command_type, params, **kwargs):
        captured["command_type"] = command_type
        captured["params"] = params
        return {"success": True, "data": {"assets": []}}

    monkeypatch.setattr(
        "services.tools.manage_asset.send_with_unity_instance",
        fake_send,
    )

    result = await manage_asset(
        ctx=DummyContext(),
        action="search",
        path="PlayerController",
    )

    assert result["success"] is True
    assert captured["command_type"] == "manage_asset"
    assert captured["params"]["path"] == "Assets"
    assert captured["params"]["searchPattern"] == "PlayerController"


@pytest.mark.asyncio
async def test_search_keeps_folder_scope_when_filter_is_present(monkeypatch):
    captured = {}

    async def fake_send(send_fn, unity_instance, command_type, params, **kwargs):
        captured["params"] = params
        return {"success": True, "data": {"assets": []}}

    monkeypatch.setattr(
        "services.tools.manage_asset.send_with_unity_instance",
        fake_send,
    )

    result = await manage_asset(
        ctx=DummyContext(),
        action="search",
        path="Assets/Prefabs",
        filter_type="Prefab",
    )

    assert result["success"] is True
    assert captured["params"]["path"] == "Assets/Prefabs"
    assert captured["params"]["filterType"] == "Prefab"
