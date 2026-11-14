"""Idempotent command delivery: one request_id per logical call, reused by every resend."""
from __future__ import annotations

import asyncio
from datetime import datetime, timezone

import pytest

from core.config import config
from transport import idempotency, unity_transport
from transport.idempotency import RetryKeyCache
from transport.plugin_hub import COMMAND_LEDGER_CAPABILITY, PluginDisconnectedError, PluginHub
from transport.plugin_registry import PluginSession


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


def test_overlapping_call_does_not_clear_another_calls_unknown_outcome():
    """Two identical calls overlap with ids A and B; A's reply is lost, then B succeeds."""
    cache = RetryKeyCache()
    fp = idempotency.fingerprint("P@h", "manage_gameobject", {"action": "create"})
    a = cache.request_id_for(fp)
    b = cache.request_id_for(fp)
    assert a != b
    cache.record_outcome(fp, a, {"success": False, "hint": "retry"})
    cache.record_outcome(fp, b, {"success": True})
    assert cache.request_id_for(fp) == a, "B's success must not erase A's unknown outcome"
    cache.record_outcome(fp, a, {"success": True})
    assert cache.request_id_for(fp) != a, "the owner clears its own entry"


@pytest.mark.parametrize("response", [
    {"success": False, "code": "outcome_unknown", "error": "Command ... was still running when the Unity domain reloaded"},
    {"success": False, "error": "x", "data": {"code": "outcome_unknown"}},
])
def test_outcome_unknown_keeps_the_request_id(response):
    cache = RetryKeyCache()
    fp = idempotency.fingerprint(None, "execute_code", {"code": "x"})
    rid = cache.request_id_for(fp)
    cache.record_outcome(fp, rid, response)
    assert cache.request_id_for(fp) == rid, "a retry must reach the ledger with the same id"


def test_fingerprint_is_scoped_to_the_user():
    assert idempotency.fingerprint("P@h", "x", {}, scope="u1") != idempotency.fingerprint("P@h", "x", {}, scope="u2")
    assert idempotency.fingerprint("P@h", "x", {}) == idempotency.fingerprint("P@h", "x", {}, scope=None)


@pytest.mark.asyncio
async def test_http_retry_ids_are_not_shared_between_users(monkeypatch):
    monkeypatch.setattr(config, "transport_mode", "http")
    monkeypatch.setattr(config, "http_remote_hosted", False)
    current_user = {"id": "user-a"}
    sent: list[str] = []

    async def resolve_user():
        return current_user["id"]

    async def fake_send_for_instance(_instance, _command, _params, **kwargs):
        sent.append(kwargs["request_id"])
        return {"success": False, "hint": "retry", "error": "lost"} if len(sent) == 1 else {"status": "success", "result": {}}

    monkeypatch.setattr(unity_transport, "_resolve_user_id_from_request", resolve_user)
    monkeypatch.setattr(PluginHub, "send_command_for_instance", fake_send_for_instance)

    async def unused(*_a, **_k):
        raise AssertionError("stdio path used in HTTP mode")

    await unity_transport.send_with_unity_instance(unused, "P@h", "manage_gameobject", {"action": "create"})
    current_user["id"] = "user-b"
    await unity_transport.send_with_unity_instance(unused, "P@h", "manage_gameobject", {"action": "create"})
    assert sent[0] != sent[1], "user B must not reuse user A's unknown-outcome id"
    current_user["id"] = "user-a"
    await unity_transport.send_with_unity_instance(unused, "P@h", "manage_gameobject", {"action": "create"})
    assert sent[2] == sent[0], "user A's retry still reuses its id"


# ---------------------------------------------------------------- PluginHub resend

class _FakeRegistry:
    def __init__(self, capabilities):
        now = datetime.now(timezone.utc)
        self.session = PluginSession(
            session_id="s1", project_name="P", project_hash="h", unity_version="6000.6",
            registered_at=now, connected_at=now, capabilities=tuple(capabilities))

    async def get_session(self, session_id):
        return self.session


@pytest.fixture
def hub(monkeypatch):
    original = (PluginHub._registry, PluginHub._lock)
    calls: list[tuple[str, str]] = []

    async def resolve(unity_instance, user_id=None, retry_on_reload=True):
        return "s1"

    async def live(session_id):
        return True

    monkeypatch.setattr(PluginHub, "_resolve_session_id", classmethod(lambda cls, *a, **k: resolve(*a, **k)))
    monkeypatch.setattr(PluginHub, "_ensure_live_connection", classmethod(lambda cls, sid: live(sid)))
    PluginHub._lock = asyncio.Lock()
    yield calls
    PluginHub._registry, PluginHub._lock = original


def _raw_sender(calls, failures):
    async def raw(cls, session_id, command_type, params, request_id):
        calls.append((session_id, request_id))
        if len(calls) <= failures:
            raise PluginDisconnectedError("Unity plugin session s1 disconnected while awaiting command_result")
        return {"status": "success", "result": {"ok": True}}
    return classmethod(raw)


@pytest.mark.asyncio
async def test_hub_resends_same_request_id_after_disconnect(hub, monkeypatch):
    PluginHub._registry = _FakeRegistry([COMMAND_LEDGER_CAPABILITY])
    monkeypatch.setattr(PluginHub, "_send_command_raw", _raw_sender(hub, failures=1))

    result = await PluginHub.send_command_for_instance(None, "manage_gameobject", {}, request_id="rid-1")

    assert result["status"] == "success"
    assert [rid for _, rid in hub] == ["rid-1", "rid-1"]


@pytest.mark.asyncio
async def test_hub_does_not_resend_without_ledger_capability(hub, monkeypatch):
    PluginHub._registry = _FakeRegistry([])
    monkeypatch.setattr(PluginHub, "_send_command_raw", _raw_sender(hub, failures=1))

    result = await PluginHub.send_command_for_instance(None, "manage_gameobject", {}, request_id="rid-1")

    assert result["success"] is False and result["hint"] == "retry"
    assert len(hub) == 1


@pytest.mark.asyncio
async def test_hub_does_not_resend_when_retry_on_reload_disabled(hub, monkeypatch):
    PluginHub._registry = _FakeRegistry([COMMAND_LEDGER_CAPABILITY])
    monkeypatch.setattr(PluginHub, "_send_command_raw", _raw_sender(hub, failures=1))

    result = await PluginHub.send_command_for_instance(
        None, "refresh_unity", {}, retry_on_reload=False, request_id="rid-1")

    assert result["hint"] == "retry"
    assert len(hub) == 1


@pytest.mark.asyncio
async def test_hub_resend_is_bounded(hub, monkeypatch):
    PluginHub._registry = _FakeRegistry([COMMAND_LEDGER_CAPABILITY])
    monkeypatch.setattr(PluginHub, "_send_command_raw", _raw_sender(hub, failures=99))

    result = await PluginHub.send_command_for_instance(None, "manage_gameobject", {}, request_id="rid-1")

    assert result["hint"] == "retry"
    assert len(hub) == PluginHub.MAX_RESENDS_AFTER_DISCONNECT + 1


def test_register_message_capabilities_default_to_empty():
    from transport.models import RegisterMessage

    assert RegisterMessage(project_hash="h").capabilities == []
    msg = RegisterMessage(project_hash="h", capabilities=[COMMAND_LEDGER_CAPABILITY])
    assert msg.capabilities == ["command_ledger"]


@pytest.mark.asyncio
async def test_stdio_transport_failure_is_marked_retry(monkeypatch):
    from transport.legacy import unity_connection as uc

    def unreachable(*_a, **_k):
        raise ConnectionError("Could not connect to Unity")

    monkeypatch.setattr(uc, "send_command_with_retry", unreachable)
    resp = await uc.async_send_command_with_retry("manage_scene", {"action": "get_active"})
    assert resp.success is False
    assert resp.hint == "retry"
    assert "Could not connect" in resp.error
