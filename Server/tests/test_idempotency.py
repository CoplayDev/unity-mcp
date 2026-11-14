"""Idempotent command delivery: one request_id per logical call, reused by every resend."""
from __future__ import annotations


import pytest

from core.config import config
from transport import idempotency, unity_transport
from transport.idempotency import RetryKeyCache
from transport.plugin_hub import PluginHub


@pytest.fixture(autouse=True)
def _fresh_retry_keys():
    idempotency.retry_keys.clear()
    yield
    idempotency.retry_keys.clear()


# ---------------------------------------------------------------- RetryKeyCache

def test_retry_key_reused_only_after_unknown_outcome():
    cache = RetryKeyCache()
    fp = idempotency.fingerprint("P@h", "manage_gameobject", {"action": "create"})

    first = cache.request_id_for(fp)
    cache.record_outcome(fp, first, {"success": False, "hint": "retry", "error": "disconnected"})
    assert cache.request_id_for(fp) == first

    cache.record_outcome(fp, first, {"success": True})
    assert cache.request_id_for(fp) != first


def test_retry_key_not_reused_after_definite_error():
    cache = RetryKeyCache()
    fp = idempotency.fingerprint(None, "manage_scene", {})
    rid = cache.request_id_for(fp)
    cache.record_outcome(fp, rid, {"success": False, "error": "bad params"})
    assert cache.request_id_for(fp) != rid


def test_retry_key_expires(monkeypatch):
    cache = RetryKeyCache(window_s=10.0)
    fp = idempotency.fingerprint(None, "x", {})
    now = [1000.0]
    monkeypatch.setattr(idempotency.time, "monotonic", lambda: now[0])
    rid = cache.request_id_for(fp)
    cache.record_outcome(fp, rid, {"success": False, "hint": "retry"})
    now[0] += 11.0
    assert cache.request_id_for(fp) != rid


def test_fingerprint_depends_on_params_and_instance():
    a = idempotency.fingerprint("A@1", "manage_gameobject", {"name": "Cube"})
    assert a == idempotency.fingerprint("A@1", "manage_gameobject", {"name": "Cube"})
    assert a != idempotency.fingerprint("A@1", "manage_gameobject", {"name": "Sphere"})
    assert a != idempotency.fingerprint("B@2", "manage_gameobject", {"name": "Cube"})


# ---------------------------------------------------------------- send_with_unity_instance

@pytest.mark.asyncio
async def test_stdio_client_retry_reuses_request_id(monkeypatch):
    monkeypatch.setattr(config, "transport_mode", "stdio")
    seen: list[str] = []
    replies = [
        {"success": False, "hint": "retry", "error": "Unity disconnected"},
        {"success": True, "data": {"created": 1}},
        {"success": True, "data": {"created": 2}},
    ]

    async def fake_send(command_type, params, *, instance_id=None, request_id=None, **_kw):
        seen.append(request_id)
        return replies[len(seen) - 1]

    params = {"action": "create", "name": "Cube"}
    await unity_transport.send_with_unity_instance(fake_send, "P@h", "manage_gameobject", params)
    await unity_transport.send_with_unity_instance(fake_send, "P@h", "manage_gameobject", params)
    await unity_transport.send_with_unity_instance(fake_send, "P@h", "manage_gameobject", params)

    assert seen[0] and seen[0] == seen[1], "client retry after hint=retry must keep the key"
    assert seen[2] != seen[1], "a new call after success must get a new key"


@pytest.mark.asyncio
async def test_explicit_request_id_is_forwarded(monkeypatch):
    monkeypatch.setattr(config, "transport_mode", "stdio")
    captured = {}

    async def fake_send(command_type, params, **kwargs):
        captured.update(kwargs)
        return {"success": True}

    await unity_transport.send_with_unity_instance(fake_send, None, "ping", {}, request_id="abc")
    assert captured["request_id"] == "abc"


@pytest.mark.asyncio
async def test_http_passes_request_id_to_plugin_hub(monkeypatch):
    monkeypatch.setattr(config, "transport_mode", "http")
    monkeypatch.setattr(config, "http_remote_hosted", False)
    captured = {}

    async def fake_send_for_instance(_instance, _command, _params, **kwargs):
        captured.update(kwargs)
        return {"status": "success", "result": {}}

    monkeypatch.setattr(PluginHub, "send_command_for_instance", fake_send_for_instance)

    async def unused(*_a, **_k):
        raise AssertionError("stdio path used in HTTP mode")

    await unity_transport.send_with_unity_instance(unused, None, "manage_scene", {"action": "get_active"})
    assert isinstance(captured.get("request_id"), str) and len(captured["request_id"]) == 32
