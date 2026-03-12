"""
Tests for the manage_localization tool.
Validates parameter routing for Unity Localization operations.
"""
import pytest

from .test_helpers import DummyContext
import services.tools.manage_localization as loc_mod


def _fake_send_factory(captured: dict, response: dict = None):
    if response is None:
        response = {"success": True, "message": "OK", "data": {}}

    async def fake_send(cmd, params, **kwargs):
        captured["cmd"] = cmd
        captured["params"] = params
        return response

    return fake_send


@pytest.mark.asyncio
async def test_list_locales(monkeypatch):
    captured = {}
    monkeypatch.setattr(loc_mod, "async_send_command_with_retry", _fake_send_factory(captured))
    resp = await loc_mod.manage_localization(ctx=DummyContext(), action="list_locales")
    assert resp["success"] is True
    assert captured["cmd"] == "manage_localization"


@pytest.mark.asyncio
async def test_set_active_locale(monkeypatch):
    captured = {}
    monkeypatch.setattr(loc_mod, "async_send_command_with_retry", _fake_send_factory(captured))
    resp = await loc_mod.manage_localization(
        ctx=DummyContext(), action="set_active_locale", locale_code="ja",
    )
    assert resp["success"] is True
    assert captured["params"]["locale_code"] == "ja"


@pytest.mark.asyncio
async def test_get_entry(monkeypatch):
    captured = {}
    monkeypatch.setattr(loc_mod, "async_send_command_with_retry", _fake_send_factory(captured))
    resp = await loc_mod.manage_localization(
        ctx=DummyContext(), action="get_entry", table="UI", key="btn_start", locale="en",
    )
    assert resp["success"] is True
    assert captured["params"]["table"] == "UI"
    assert captured["params"]["key"] == "btn_start"


@pytest.mark.asyncio
async def test_set_entry(monkeypatch):
    captured = {}
    monkeypatch.setattr(loc_mod, "async_send_command_with_retry", _fake_send_factory(captured))
    resp = await loc_mod.manage_localization(
        ctx=DummyContext(), action="set_entry", table="UI", key="btn_start",
        locale="ja", value="スタート",
    )
    assert resp["success"] is True
    assert captured["params"]["value"] == "スタート"


@pytest.mark.asyncio
async def test_none_params_stripped(monkeypatch):
    captured = {}
    monkeypatch.setattr(loc_mod, "async_send_command_with_retry", _fake_send_factory(captured))
    await loc_mod.manage_localization(ctx=DummyContext(), action="list_locales")
    assert set(captured["params"].keys()) == {"action"}


@pytest.mark.asyncio
async def test_python_exception_caught(monkeypatch):
    async def raising_send(cmd, params, **kwargs):
        raise ConnectionError("Unity not connected")
    monkeypatch.setattr(loc_mod, "async_send_command_with_retry", raising_send)
    resp = await loc_mod.manage_localization(ctx=DummyContext(), action="list_locales")
    assert resp["success"] is False
    assert "Unity not connected" in resp["message"]
