"""
Tests for the manage_render_pipeline tool.
Validates parameter routing for Unity render pipeline operations.
"""
import pytest

from .test_helpers import DummyContext
import services.tools.manage_render_pipeline as rp_mod


def _fake_send_factory(captured: dict, response: dict = None):
    if response is None:
        response = {"success": True, "message": "OK", "data": {}}

    async def fake_send(cmd, params, **kwargs):
        captured["cmd"] = cmd
        captured["params"] = params
        return response

    return fake_send


@pytest.mark.asyncio
async def test_get_pipeline_info(monkeypatch):
    captured = {}
    monkeypatch.setattr(rp_mod, "async_send_command_with_retry", _fake_send_factory(captured))
    resp = await rp_mod.manage_render_pipeline(ctx=DummyContext(), action="get_pipeline_info")
    assert resp["success"] is True
    assert set(captured["params"].keys()) == {"action"}


@pytest.mark.asyncio
async def test_list_volumes(monkeypatch):
    captured = {}
    monkeypatch.setattr(rp_mod, "async_send_command_with_retry", _fake_send_factory(captured))
    resp = await rp_mod.manage_render_pipeline(ctx=DummyContext(), action="list_volumes", page_size=10)
    assert resp["success"] is True
    assert captured["params"]["page_size"] == 10


@pytest.mark.asyncio
async def test_get_volume(monkeypatch):
    captured = {}
    monkeypatch.setattr(rp_mod, "async_send_command_with_retry", _fake_send_factory(captured))
    resp = await rp_mod.manage_render_pipeline(ctx=DummyContext(), action="get_volume", target="GlobalVolume")
    assert resp["success"] is True
    assert captured["params"]["target"] == "GlobalVolume"


@pytest.mark.asyncio
async def test_set_volume_override(monkeypatch):
    captured = {}
    monkeypatch.setattr(rp_mod, "async_send_command_with_retry", _fake_send_factory(captured))
    resp = await rp_mod.manage_render_pipeline(
        ctx=DummyContext(), action="set_volume_override",
        target="GlobalVolume", override_type="Bloom",
        property="intensity", value="5.0",
    )
    assert resp["success"] is True
    assert captured["params"]["override_type"] == "Bloom"
    assert captured["params"]["property"] == "intensity"


@pytest.mark.asyncio
async def test_toggle_volume_override(monkeypatch):
    captured = {}
    monkeypatch.setattr(rp_mod, "async_send_command_with_retry", _fake_send_factory(captured))
    resp = await rp_mod.manage_render_pipeline(
        ctx=DummyContext(), action="toggle_volume_override",
        target="GlobalVolume", override_type="Bloom", enabled=False,
    )
    assert resp["success"] is True
    assert captured["params"]["enabled"] is False


@pytest.mark.asyncio
async def test_list_renderer_features(monkeypatch):
    captured = {}
    monkeypatch.setattr(rp_mod, "async_send_command_with_retry", _fake_send_factory(captured))
    resp = await rp_mod.manage_render_pipeline(ctx=DummyContext(), action="list_renderer_features")
    assert resp["success"] is True


@pytest.mark.asyncio
async def test_get_render_pipeline_asset(monkeypatch):
    captured = {}
    monkeypatch.setattr(rp_mod, "async_send_command_with_retry", _fake_send_factory(captured))
    resp = await rp_mod.manage_render_pipeline(ctx=DummyContext(), action="get_render_pipeline_asset")
    assert resp["success"] is True


@pytest.mark.asyncio
async def test_list_post_processing(monkeypatch):
    captured = {}
    monkeypatch.setattr(rp_mod, "async_send_command_with_retry", _fake_send_factory(captured))
    resp = await rp_mod.manage_render_pipeline(ctx=DummyContext(), action="list_post_processing")
    assert resp["success"] is True


@pytest.mark.asyncio
async def test_python_exception_caught(monkeypatch):
    async def raising_send(cmd, params, **kwargs):
        raise ConnectionError("Unity not connected")
    monkeypatch.setattr(rp_mod, "async_send_command_with_retry", raising_send)
    resp = await rp_mod.manage_render_pipeline(ctx=DummyContext(), action="get_pipeline_info")
    assert resp["success"] is False
