"""
Tests for the manage_lighting tool.
Validates parameter routing, action dispatch, and error handling
for Unity lighting operations.
"""
import pytest

from .test_helpers import DummyContext
import services.tools.manage_lighting as lighting_mod


def _fake_send_factory(captured: dict, response: dict = None):
    if response is None:
        response = {"success": True, "message": "OK", "data": {}}

    async def fake_send(cmd, params, **kwargs):
        captured["cmd"] = cmd
        captured["params"] = params
        return response

    return fake_send


@pytest.mark.asyncio
async def test_list_lights(monkeypatch):
    """list_lights sends page_size and type_filter."""
    captured = {}
    monkeypatch.setattr(
        lighting_mod, "async_send_command_with_retry",
        _fake_send_factory(captured),
    )

    resp = await lighting_mod.manage_lighting(
        ctx=DummyContext(), action="list_lights",
        type_filter="Point", page_size=10,
    )

    assert resp["success"] is True
    assert captured["params"]["type_filter"] == "Point"


@pytest.mark.asyncio
async def test_get_light(monkeypatch):
    """get_light sends target."""
    captured = {}
    monkeypatch.setattr(
        lighting_mod, "async_send_command_with_retry",
        _fake_send_factory(captured),
    )

    resp = await lighting_mod.manage_lighting(
        ctx=DummyContext(), action="get_light", target="Sun",
    )

    assert resp["success"] is True
    assert captured["params"]["target"] == "Sun"


@pytest.mark.asyncio
async def test_set_light(monkeypatch):
    """set_light sends target and properties."""
    captured = {}
    monkeypatch.setattr(
        lighting_mod, "async_send_command_with_retry",
        _fake_send_factory(captured),
    )

    resp = await lighting_mod.manage_lighting(
        ctx=DummyContext(), action="set_light",
        target="Sun", properties='{"intensity": 2.0}',
    )

    assert resp["success"] is True
    assert captured["params"]["properties"] == '{"intensity": 2.0}'


@pytest.mark.asyncio
async def test_bake(monkeypatch):
    """bake routes correctly with no params."""
    captured = {}
    monkeypatch.setattr(
        lighting_mod, "async_send_command_with_retry",
        _fake_send_factory(captured),
    )

    resp = await lighting_mod.manage_lighting(
        ctx=DummyContext(), action="bake",
    )

    assert resp["success"] is True
    assert captured["params"]["action"] == "bake"


@pytest.mark.asyncio
async def test_get_bake_status(monkeypatch):
    """get_bake_status routes correctly."""
    captured = {}
    monkeypatch.setattr(
        lighting_mod, "async_send_command_with_retry",
        _fake_send_factory(captured, {
            "success": True, "message": "Bake status.",
            "data": {"is_running": False, "build_progress": 0.0},
        }),
    )

    resp = await lighting_mod.manage_lighting(
        ctx=DummyContext(), action="get_bake_status",
    )

    assert resp["success"] is True


@pytest.mark.asyncio
async def test_list_probes(monkeypatch):
    """list_probes sends page_size."""
    captured = {}
    monkeypatch.setattr(
        lighting_mod, "async_send_command_with_retry",
        _fake_send_factory(captured),
    )

    resp = await lighting_mod.manage_lighting(
        ctx=DummyContext(), action="list_probes", page_size=20,
    )

    assert resp["success"] is True
    assert captured["params"]["page_size"] == 20


@pytest.mark.asyncio
async def test_get_environment(monkeypatch):
    """get_environment routes with no params."""
    captured = {}
    monkeypatch.setattr(
        lighting_mod, "async_send_command_with_retry",
        _fake_send_factory(captured),
    )

    resp = await lighting_mod.manage_lighting(
        ctx=DummyContext(), action="get_environment",
    )

    assert resp["success"] is True
    assert set(captured["params"].keys()) == {"action"}


@pytest.mark.asyncio
async def test_set_environment(monkeypatch):
    """set_environment sends properties."""
    captured = {}
    monkeypatch.setattr(
        lighting_mod, "async_send_command_with_retry",
        _fake_send_factory(captured),
    )

    resp = await lighting_mod.manage_lighting(
        ctx=DummyContext(), action="set_environment",
        properties='{"fog": true}',
    )

    assert resp["success"] is True
    assert captured["params"]["properties"] == '{"fog": true}'


@pytest.mark.asyncio
async def test_get_lightmap_settings(monkeypatch):
    """get_lightmap_settings routes correctly."""
    captured = {}
    monkeypatch.setattr(
        lighting_mod, "async_send_command_with_retry",
        _fake_send_factory(captured),
    )

    resp = await lighting_mod.manage_lighting(
        ctx=DummyContext(), action="get_lightmap_settings",
    )

    assert resp["success"] is True


@pytest.mark.asyncio
async def test_none_params_stripped(monkeypatch):
    """None-valued optional params are not sent."""
    captured = {}
    monkeypatch.setattr(
        lighting_mod, "async_send_command_with_retry",
        _fake_send_factory(captured),
    )

    await lighting_mod.manage_lighting(
        ctx=DummyContext(), action="list_lights",
    )

    assert set(captured["params"].keys()) == {"action"}


@pytest.mark.asyncio
async def test_python_exception_caught(monkeypatch):
    """Python-side exceptions are caught and returned as failure."""

    async def raising_send(cmd, params, **kwargs):
        raise ConnectionError("Unity not connected")

    monkeypatch.setattr(
        lighting_mod, "async_send_command_with_retry",
        raising_send,
    )

    resp = await lighting_mod.manage_lighting(
        ctx=DummyContext(), action="list_lights",
    )

    assert resp["success"] is False
    assert "Unity not connected" in resp["message"]
