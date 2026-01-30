"""End-to-end-ish smoke tests for transport routing paths."""
from __future__ import annotations

import pytest

from core.config import config
from transport import unity_transport


@pytest.mark.asyncio
async def test_http_local_smoke(monkeypatch):
    """HTTP local should route through PluginHub without requiring user_id."""
    monkeypatch.setattr(config, "transport_mode", "http")
    monkeypatch.setattr(config, "http_remote_hosted", False)

    async def fake_send_command_for_instance(_instance, _command, _params, **_kwargs):
        return {"status": "success", "result": {"message": "ok", "data": {"via": "http"}}}

    monkeypatch.setattr(
        unity_transport.PluginHub,
        "send_command_for_instance",
        fake_send_command_for_instance,
    )

    async def _unused_send_fn(*_args, **_kwargs):
        raise AssertionError("send_fn should not be used in HTTP mode")

    result = await unity_transport.send_with_unity_instance(
        _unused_send_fn, None, "ping", {}
    )

    assert result["success"] is True
    assert result["data"] == {"via": "http"}


@pytest.mark.asyncio
async def test_http_remote_smoke(monkeypatch):
    """HTTP remote-hosted should route through PluginHub when user_id is provided."""
    monkeypatch.setattr(config, "transport_mode", "http")
    monkeypatch.setattr(config, "http_remote_hosted", True)

    async def fake_send_command_for_instance(_instance, _command, _params, **_kwargs):
        return {"status": "success", "result": {"data": {"via": "http-remote"}}}

    monkeypatch.setattr(
        unity_transport.PluginHub,
        "send_command_for_instance",
        fake_send_command_for_instance,
    )

    async def _unused_send_fn(*_args, **_kwargs):
        raise AssertionError("send_fn should not be used in HTTP mode")

    result = await unity_transport.send_with_unity_instance(
        _unused_send_fn, None, "ping", {}, user_id="user-1"
    )

    assert result["success"] is True
    assert result["data"] == {"via": "http-remote"}


@pytest.mark.asyncio
async def test_stdio_smoke(monkeypatch):
    """Stdio transport should call the legacy send fn with instance_id."""
    monkeypatch.setattr(config, "transport_mode", "stdio")
    monkeypatch.setattr(config, "http_remote_hosted", False)

    async def fake_send_fn(command_type, params, *, instance_id=None, **_kwargs):
        return {
            "success": True,
            "data": {"via": "stdio", "command": command_type, "instance": instance_id, "params": params},
        }

    result = await unity_transport.send_with_unity_instance(
        fake_send_fn, "Project@abcd1234", "ping", {"x": 1}
    )

    assert result["success"] is True
    assert result["data"]["via"] == "stdio"
    assert result["data"]["instance"] == "Project@abcd1234"
