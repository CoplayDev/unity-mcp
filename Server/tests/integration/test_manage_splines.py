"""
Tests for the manage_splines tool.
Validates parameter routing for Unity Splines operations.
"""
import pytest

from .test_helpers import DummyContext
import services.tools.manage_splines as splines_mod


def _fake_send_factory(captured: dict, response: dict = None):
    if response is None:
        response = {"success": True, "message": "OK", "data": {}}

    async def fake_send(cmd, params, **kwargs):
        captured["cmd"] = cmd
        captured["params"] = params
        return response

    return fake_send


@pytest.mark.asyncio
async def test_list_splines(monkeypatch):
    captured = {}
    monkeypatch.setattr(
        splines_mod, "async_send_command_with_retry",
        _fake_send_factory(captured),
    )
    resp = await splines_mod.manage_splines(ctx=DummyContext(), action="list_splines")
    assert resp["success"] is True
    assert captured["cmd"] == "manage_splines"
    assert captured["params"]["action"] == "list_splines"


@pytest.mark.asyncio
async def test_get_spline(monkeypatch):
    captured = {}
    monkeypatch.setattr(
        splines_mod, "async_send_command_with_retry",
        _fake_send_factory(captured),
    )
    resp = await splines_mod.manage_splines(
        ctx=DummyContext(), action="get_spline", target="Road", spline_index=0,
    )
    assert resp["success"] is True
    assert captured["params"]["spline_index"] == 0


@pytest.mark.asyncio
async def test_get_knot(monkeypatch):
    captured = {}
    monkeypatch.setattr(
        splines_mod, "async_send_command_with_retry",
        _fake_send_factory(captured),
    )
    resp = await splines_mod.manage_splines(
        ctx=DummyContext(), action="get_knot", target="Road", spline_index=0, knot_index=2,
    )
    assert resp["success"] is True
    assert captured["params"]["knot_index"] == 2


@pytest.mark.asyncio
async def test_add_knot(monkeypatch):
    captured = {}
    monkeypatch.setattr(
        splines_mod, "async_send_command_with_retry",
        _fake_send_factory(captured),
    )
    resp = await splines_mod.manage_splines(
        ctx=DummyContext(), action="add_knot", target="Road",
        spline_index=0, position="5,0,10", rotation="0,0,0,1",
    )
    assert resp["success"] is True
    assert captured["params"]["position"] == "5,0,10"
    assert captured["params"]["rotation"] == "0,0,0,1"


@pytest.mark.asyncio
async def test_evaluate(monkeypatch):
    captured = {}
    monkeypatch.setattr(
        splines_mod, "async_send_command_with_retry",
        _fake_send_factory(captured),
    )
    resp = await splines_mod.manage_splines(
        ctx=DummyContext(), action="evaluate", target="Road", spline_index=0, t=0.5,
    )
    assert resp["success"] is True
    assert captured["params"]["t"] == 0.5


@pytest.mark.asyncio
async def test_none_params_stripped(monkeypatch):
    captured = {}
    monkeypatch.setattr(
        splines_mod, "async_send_command_with_retry",
        _fake_send_factory(captured),
    )
    await splines_mod.manage_splines(ctx=DummyContext(), action="list_splines")
    assert set(captured["params"].keys()) == {"action"}


@pytest.mark.asyncio
async def test_python_exception_caught(monkeypatch):
    async def raising_send(cmd, params, **kwargs):
        raise ConnectionError("Unity not connected")

    monkeypatch.setattr(
        splines_mod, "async_send_command_with_retry", raising_send,
    )
    resp = await splines_mod.manage_splines(ctx=DummyContext(), action="list_splines")
    assert resp["success"] is False
    assert "Unity not connected" in resp["message"]
