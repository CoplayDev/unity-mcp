"""
Tests for the manage_addressables tool.
Validates parameter routing for Unity Addressables operations.
"""
import pytest

from .test_helpers import DummyContext
import services.tools.manage_addressables as addr_mod


def _fake_send_factory(captured: dict, response: dict = None):
    if response is None:
        response = {"success": True, "message": "OK", "data": {}}

    async def fake_send(cmd, params, **kwargs):
        captured["cmd"] = cmd
        captured["params"] = params
        return response

    return fake_send


@pytest.mark.asyncio
async def test_list_groups(monkeypatch):
    captured = {}
    monkeypatch.setattr(
        addr_mod, "async_send_command_with_retry",
        _fake_send_factory(captured),
    )
    resp = await addr_mod.manage_addressables(ctx=DummyContext(), action="list_groups")
    assert resp["success"] is True
    assert captured["cmd"] == "manage_addressables"
    assert captured["params"]["action"] == "list_groups"


@pytest.mark.asyncio
async def test_get_group(monkeypatch):
    captured = {}
    monkeypatch.setattr(
        addr_mod, "async_send_command_with_retry",
        _fake_send_factory(captured),
    )
    resp = await addr_mod.manage_addressables(
        ctx=DummyContext(), action="get_group", group_name="Default Local Group",
    )
    assert resp["success"] is True
    assert captured["params"]["group_name"] == "Default Local Group"


@pytest.mark.asyncio
async def test_list_entries(monkeypatch):
    captured = {}
    monkeypatch.setattr(
        addr_mod, "async_send_command_with_retry",
        _fake_send_factory(captured),
    )
    resp = await addr_mod.manage_addressables(
        ctx=DummyContext(), action="list_entries", group_name="Prefabs", page_size=10,
    )
    assert resp["success"] is True
    assert captured["params"]["page_size"] == 10


@pytest.mark.asyncio
async def test_get_entry(monkeypatch):
    captured = {}
    monkeypatch.setattr(
        addr_mod, "async_send_command_with_retry",
        _fake_send_factory(captured),
    )
    resp = await addr_mod.manage_addressables(
        ctx=DummyContext(), action="get_entry", address="Prefabs/Player",
    )
    assert resp["success"] is True
    assert captured["params"]["address"] == "Prefabs/Player"


@pytest.mark.asyncio
async def test_build(monkeypatch):
    captured = {}
    monkeypatch.setattr(
        addr_mod, "async_send_command_with_retry",
        _fake_send_factory(captured),
    )
    resp = await addr_mod.manage_addressables(
        ctx=DummyContext(), action="build", clean=True,
    )
    assert resp["success"] is True
    assert captured["params"]["clean"] is True


@pytest.mark.asyncio
async def test_none_params_stripped(monkeypatch):
    captured = {}
    monkeypatch.setattr(
        addr_mod, "async_send_command_with_retry",
        _fake_send_factory(captured),
    )
    await addr_mod.manage_addressables(ctx=DummyContext(), action="list_groups")
    assert set(captured["params"].keys()) == {"action"}


@pytest.mark.asyncio
async def test_python_exception_caught(monkeypatch):
    async def raising_send(cmd, params, **kwargs):
        raise ConnectionError("Unity not connected")

    monkeypatch.setattr(
        addr_mod, "async_send_command_with_retry", raising_send,
    )
    resp = await addr_mod.manage_addressables(ctx=DummyContext(), action="list_groups")
    assert resp["success"] is False
    assert "Unity not connected" in resp["message"]
