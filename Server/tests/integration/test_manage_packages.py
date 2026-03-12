"""
Tests for the manage_packages tool.
Validates parameter routing for Unity Package Manager operations.
"""
import pytest

from .test_helpers import DummyContext
import services.tools.manage_packages as packages_mod


def _fake_send_factory(captured: dict, response: dict = None):
    if response is None:
        response = {"success": True, "message": "OK", "data": {}}

    async def fake_send(cmd, params, **kwargs):
        captured["cmd"] = cmd
        captured["params"] = params
        return response

    return fake_send


@pytest.mark.asyncio
async def test_list_packages(monkeypatch):
    captured = {}
    monkeypatch.setattr(packages_mod, "async_send_command_with_retry", _fake_send_factory(captured))
    resp = await packages_mod.manage_packages(
        ctx=DummyContext(), action="list", include_built_in=True,
    )
    assert resp["success"] is True
    assert captured["params"]["include_built_in"] is True


@pytest.mark.asyncio
async def test_get_info(monkeypatch):
    captured = {}
    monkeypatch.setattr(packages_mod, "async_send_command_with_retry", _fake_send_factory(captured))
    resp = await packages_mod.manage_packages(
        ctx=DummyContext(), action="get_info", package_name="com.unity.cinemachine",
    )
    assert resp["success"] is True
    assert captured["params"]["package_name"] == "com.unity.cinemachine"


@pytest.mark.asyncio
async def test_add_package(monkeypatch):
    captured = {}
    monkeypatch.setattr(packages_mod, "async_send_command_with_retry", _fake_send_factory(captured))
    resp = await packages_mod.manage_packages(
        ctx=DummyContext(), action="add", package_id="com.unity.timeline",
    )
    assert resp["success"] is True
    assert captured["params"]["package_id"] == "com.unity.timeline"


@pytest.mark.asyncio
async def test_remove_package(monkeypatch):
    captured = {}
    monkeypatch.setattr(packages_mod, "async_send_command_with_retry", _fake_send_factory(captured))
    resp = await packages_mod.manage_packages(
        ctx=DummyContext(), action="remove", package_name="com.unity.timeline",
    )
    assert resp["success"] is True
    assert captured["params"]["package_name"] == "com.unity.timeline"


@pytest.mark.asyncio
async def test_search(monkeypatch):
    captured = {}
    monkeypatch.setattr(packages_mod, "async_send_command_with_retry", _fake_send_factory(captured))
    resp = await packages_mod.manage_packages(
        ctx=DummyContext(), action="search", query="cinemachine",
    )
    assert resp["success"] is True
    assert captured["params"]["query"] == "cinemachine"


@pytest.mark.asyncio
async def test_none_params_stripped(monkeypatch):
    captured = {}
    monkeypatch.setattr(packages_mod, "async_send_command_with_retry", _fake_send_factory(captured))
    await packages_mod.manage_packages(ctx=DummyContext(), action="list")
    assert set(captured["params"].keys()) == {"action"}


@pytest.mark.asyncio
async def test_python_exception_caught(monkeypatch):
    async def raising_send(cmd, params, **kwargs):
        raise ConnectionError("Unity not connected")
    monkeypatch.setattr(packages_mod, "async_send_command_with_retry", raising_send)
    resp = await packages_mod.manage_packages(ctx=DummyContext(), action="list")
    assert resp["success"] is False
