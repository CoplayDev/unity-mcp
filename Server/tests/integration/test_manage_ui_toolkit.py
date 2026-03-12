"""
Tests for the manage_ui_toolkit tool.
Validates parameter routing for Unity UI Toolkit operations.
"""
import pytest

from .test_helpers import DummyContext
import services.tools.manage_ui_toolkit as uitk_mod


def _fake_send_factory(captured: dict, response: dict = None):
    if response is None:
        response = {"success": True, "message": "OK", "data": {}}

    async def fake_send(cmd, params, **kwargs):
        captured["cmd"] = cmd
        captured["params"] = params
        return response

    return fake_send


@pytest.mark.asyncio
async def test_list_documents(monkeypatch):
    captured = {}
    monkeypatch.setattr(uitk_mod, "async_send_command_with_retry", _fake_send_factory(captured))
    resp = await uitk_mod.manage_ui_toolkit(ctx=DummyContext(), action="list_documents")
    assert resp["success"] is True
    assert captured["cmd"] == "manage_ui_toolkit"


@pytest.mark.asyncio
async def test_query_elements(monkeypatch):
    captured = {}
    monkeypatch.setattr(uitk_mod, "async_send_command_with_retry", _fake_send_factory(captured))
    resp = await uitk_mod.manage_ui_toolkit(
        ctx=DummyContext(), action="query_elements", target="HUD", query=".health-bar",
    )
    assert resp["success"] is True
    assert captured["params"]["query"] == ".health-bar"


@pytest.mark.asyncio
async def test_set_style(monkeypatch):
    captured = {}
    monkeypatch.setattr(uitk_mod, "async_send_command_with_retry", _fake_send_factory(captured))
    resp = await uitk_mod.manage_ui_toolkit(
        ctx=DummyContext(), action="set_style", target="HUD",
        query="#title", property="color", value="red",
    )
    assert resp["success"] is True
    assert captured["params"]["property"] == "color"
    assert captured["params"]["value"] == "red"


@pytest.mark.asyncio
async def test_list_uxml_assets(monkeypatch):
    captured = {}
    monkeypatch.setattr(uitk_mod, "async_send_command_with_retry", _fake_send_factory(captured))
    resp = await uitk_mod.manage_ui_toolkit(
        ctx=DummyContext(), action="list_uxml_assets", filter="Menu",
    )
    assert resp["success"] is True
    assert captured["params"]["filter"] == "Menu"


@pytest.mark.asyncio
async def test_none_params_stripped(monkeypatch):
    captured = {}
    monkeypatch.setattr(uitk_mod, "async_send_command_with_retry", _fake_send_factory(captured))
    await uitk_mod.manage_ui_toolkit(ctx=DummyContext(), action="list_documents")
    assert set(captured["params"].keys()) == {"action"}


@pytest.mark.asyncio
async def test_python_exception_caught(monkeypatch):
    async def raising_send(cmd, params, **kwargs):
        raise ConnectionError("Unity not connected")
    monkeypatch.setattr(uitk_mod, "async_send_command_with_retry", raising_send)
    resp = await uitk_mod.manage_ui_toolkit(ctx=DummyContext(), action="list_documents")
    assert resp["success"] is False
    assert "Unity not connected" in resp["message"]
