import pytest

from .test_helpers import DummyContext
import services.tools.manage_gameobject as manage_go_mod


@pytest.mark.asyncio
async def test_manage_gameobject_boolean_and_tag_mapping(monkeypatch):
    captured = {}

    async def fake_send(cmd, params, **kwargs):
        captured["params"] = params
        return {"success": True, "data": {}}

    monkeypatch.setattr(
        manage_go_mod,
        "async_send_command_with_retry",
        fake_send,
    )

    # find by tag: allow tag to map to searchTerm
    resp = await manage_go_mod.manage_gameobject(
        ctx=DummyContext(),
        action="find",
        search_method="by_tag",
        tag="Player",
        find_all="true",
        search_inactive="0",
    )
    # Loosen equality: wrapper may include a diagnostic message
    assert resp.get("success") is True
    assert "data" in resp
    # ensure tag mapped to searchTerm and booleans passed through; C# side coerces true/false already
    assert captured["params"]["searchTerm"] == "Player"
    assert captured["params"]["findAll"] is True
    assert captured["params"]["searchInactive"] is False


@pytest.mark.asyncio
async def test_manage_gameobject_get_components_paging_params_pass_through(monkeypatch):
    captured = {}

    async def fake_send(cmd, params, **kwargs):
        captured["params"] = params
        return {"success": True, "data": {}}

    monkeypatch.setattr(
        manage_go_mod,
        "async_send_command_with_retry",
        fake_send,
    )

    resp = await manage_go_mod.manage_gameobject(
        ctx=DummyContext(),
        action="get_components",
        target="Player",
        search_method="by_name",
        page_size="25",
        cursor="50",
        max_components="100",
        include_properties="true",
    )

    assert resp.get("success") is True
    p = captured["params"]
    assert p["action"] == "get_components"
    assert p["target"] == "Player"
    assert p["searchMethod"] == "by_name"
    assert p["pageSize"] == 25
    assert p["cursor"] == 50
    assert p["maxComponents"] == 100
    assert p["includeProperties"] is True
