"""
Tests for the manage_netcode tool.
Validates parameter routing for Unity Netcode operations.
"""
import pytest

from .test_helpers import DummyContext
import services.tools.manage_netcode as netcode_mod


def _fake_send_factory(captured: dict, response: dict = None):
    if response is None:
        response = {"success": True, "message": "OK", "data": {}}

    async def fake_send(cmd, params, **kwargs):
        captured["cmd"] = cmd
        captured["params"] = params
        return response

    return fake_send


@pytest.mark.asyncio
async def test_get_network_manager(monkeypatch):
    captured = {}
    monkeypatch.setattr(netcode_mod, "async_send_command_with_retry", _fake_send_factory(captured))
    resp = await netcode_mod.manage_netcode(ctx=DummyContext(), action="get_network_manager")
    assert resp["success"] is True
    assert captured["cmd"] == "manage_netcode"


@pytest.mark.asyncio
async def test_list_network_objects(monkeypatch):
    captured = {}
    monkeypatch.setattr(netcode_mod, "async_send_command_with_retry", _fake_send_factory(captured))
    resp = await netcode_mod.manage_netcode(
        ctx=DummyContext(), action="list_network_objects", page_size=20,
    )
    assert resp["success"] is True
    assert captured["params"]["page_size"] == 20


@pytest.mark.asyncio
async def test_get_network_object(monkeypatch):
    captured = {}
    monkeypatch.setattr(netcode_mod, "async_send_command_with_retry", _fake_send_factory(captured))
    resp = await netcode_mod.manage_netcode(
        ctx=DummyContext(), action="get_network_object", target="Player",
    )
    assert resp["success"] is True
    assert captured["params"]["target"] == "Player"


@pytest.mark.asyncio
async def test_start_host(monkeypatch):
    captured = {}
    monkeypatch.setattr(netcode_mod, "async_send_command_with_retry", _fake_send_factory(captured))
    resp = await netcode_mod.manage_netcode(ctx=DummyContext(), action="start_host")
    assert resp["success"] is True
    assert captured["params"]["action"] == "start_host"


@pytest.mark.asyncio
async def test_shutdown(monkeypatch):
    captured = {}
    monkeypatch.setattr(netcode_mod, "async_send_command_with_retry", _fake_send_factory(captured))
    resp = await netcode_mod.manage_netcode(ctx=DummyContext(), action="shutdown")
    assert resp["success"] is True


@pytest.mark.asyncio
async def test_none_params_stripped(monkeypatch):
    captured = {}
    monkeypatch.setattr(netcode_mod, "async_send_command_with_retry", _fake_send_factory(captured))
    await netcode_mod.manage_netcode(ctx=DummyContext(), action="get_network_manager")
    assert set(captured["params"].keys()) == {"action"}


@pytest.mark.asyncio
async def test_python_exception_caught(monkeypatch):
    async def raising_send(cmd, params, **kwargs):
        raise ConnectionError("Unity not connected")
    monkeypatch.setattr(netcode_mod, "async_send_command_with_retry", raising_send)
    resp = await netcode_mod.manage_netcode(ctx=DummyContext(), action="get_network_manager")
    assert resp["success"] is False
    assert "Unity not connected" in resp["message"]
