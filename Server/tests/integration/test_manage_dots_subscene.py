"""
Tests for the manage_dots_subscene tool.

Validates parameter routing, action dispatch, and error handling
for DOTS SubScene management operations.
"""
import pytest

from .test_helpers import DummyContext
import services.tools.manage_dots_subscene as subscene_mod


def _fake_send_factory(captured: dict, response: dict = None):
    if response is None:
        response = {"success": True, "message": "OK", "data": {}}

    async def fake_send(cmd, params, **kwargs):
        captured["cmd"] = cmd
        captured["params"] = params
        return response

    return fake_send


@pytest.mark.asyncio
async def test_list_subscenes(monkeypatch):
    """list_subscenes routes correctly."""
    captured = {}
    monkeypatch.setattr(
        subscene_mod, "async_send_command_with_retry",
        _fake_send_factory(captured, {
            "success": True,
            "message": "Found 3 SubScene(s).",
            "data": {"count": 3, "subscenes": []},
        }),
    )

    resp = await subscene_mod.manage_dots_subscene(
        ctx=DummyContext(), action="list_subscenes"
    )

    assert resp["success"] is True
    assert captured["cmd"] == "manage_dots_subscene"
    assert captured["params"]["action"] == "list_subscenes"


@pytest.mark.asyncio
async def test_load_subscene_params(monkeypatch):
    """load_subscene sends scene_name."""
    captured = {}
    monkeypatch.setattr(
        subscene_mod, "async_send_command_with_retry",
        _fake_send_factory(captured, {
            "success": True,
            "message": "Requested load.",
            "data": {"scene_name": "Environment"},
        }),
    )

    resp = await subscene_mod.manage_dots_subscene(
        ctx=DummyContext(),
        action="load_subscene",
        scene_name="Environment",
    )

    assert resp["success"] is True
    assert captured["params"]["scene_name"] == "Environment"


@pytest.mark.asyncio
async def test_unload_subscene_params(monkeypatch):
    """unload_subscene sends scene_name."""
    captured = {}
    monkeypatch.setattr(
        subscene_mod, "async_send_command_with_retry",
        _fake_send_factory(captured),
    )

    resp = await subscene_mod.manage_dots_subscene(
        ctx=DummyContext(),
        action="unload_subscene",
        scene_name="Enemies",
    )

    assert resp["success"] is True
    assert captured["params"]["scene_name"] == "Enemies"


@pytest.mark.asyncio
async def test_get_subscene_status_params(monkeypatch):
    """get_subscene_status sends scene_name."""
    captured = {}
    monkeypatch.setattr(
        subscene_mod, "async_send_command_with_retry",
        _fake_send_factory(captured, {
            "success": True,
            "message": "Status for SubScene.",
            "data": {"is_loaded": True, "section_count": 2},
        }),
    )

    resp = await subscene_mod.manage_dots_subscene(
        ctx=DummyContext(),
        action="get_subscene_status",
        scene_name="Environment",
    )

    assert resp["success"] is True
    assert captured["params"]["scene_name"] == "Environment"


@pytest.mark.asyncio
async def test_list_sections_params(monkeypatch):
    """list_sections sends scene_name."""
    captured = {}
    monkeypatch.setattr(
        subscene_mod, "async_send_command_with_retry",
        _fake_send_factory(captured),
    )

    resp = await subscene_mod.manage_dots_subscene(
        ctx=DummyContext(),
        action="list_sections",
        scene_name="Level01",
    )

    assert resp["success"] is True
    assert captured["params"]["scene_name"] == "Level01"


@pytest.mark.asyncio
async def test_none_params_stripped(monkeypatch):
    """None-valued optional params are not sent."""
    captured = {}
    monkeypatch.setattr(
        subscene_mod, "async_send_command_with_retry",
        _fake_send_factory(captured),
    )

    await subscene_mod.manage_dots_subscene(
        ctx=DummyContext(),
        action="list_subscenes",
    )

    assert set(captured["params"].keys()) == {"action"}


@pytest.mark.asyncio
async def test_python_exception_caught(monkeypatch):
    """Python-side exceptions are caught and returned as failure."""

    async def raising_send(cmd, params, **kwargs):
        raise ConnectionError("Unity not connected")

    monkeypatch.setattr(
        subscene_mod, "async_send_command_with_retry",
        raising_send,
    )

    resp = await subscene_mod.manage_dots_subscene(
        ctx=DummyContext(),
        action="list_subscenes",
    )

    assert resp["success"] is False
    assert "Unity not connected" in resp["message"]
