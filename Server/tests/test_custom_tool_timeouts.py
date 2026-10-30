"""Timeouts on custom tools must be long-budget, loud, and impossible to misread.

Real-world failure (2026-08-11): a long-running project tool (image comparison)
exceeded the hub's default 30s command budget and the caller got the five words
{"success":false,"error":"TimeoutError","hint":null} — after which the agent
carried on reading stale on-disk artifacts as if the tool had run. Three fixes:
custom tools default to a 300s budget, _normalize_response no longer drops the
hint, and transport-level timeouts explain that the command may not have run.
"""

import asyncio

import pytest

from core.config import config
from models.models import MCPResponse, ToolDefinitionModel
from services.custom_tool_service import CustomToolService


class _NoopMcp:
    def custom_route(self, _path, methods=None):  # noqa: ARG002
        def _decorator(fn):
            return fn

        return _decorator

    def tool(self, name=None, description=None):  # noqa: ARG002
        def _decorator(fn):
            return fn

        return _decorator


def _make_service() -> CustomToolService:
    return CustomToolService(_NoopMcp(), project_scoped_tools=True)


def test_normalize_response_preserves_hint():
    service = _make_service()
    result = service._normalize_response(
        {"success": False, "error": "boom", "hint": "retry", "data": {"reason": "x"}}
    )
    assert isinstance(result, MCPResponse)
    assert result.hint == "retry"
    assert result.data == {"reason": "x"}


@pytest.mark.asyncio
async def test_execute_tool_defaults_long_timeout(monkeypatch):
    service = _make_service()
    definition = ToolDefinitionModel(name="long_running_tool", description="long tool")
    service._register_tool("proj-1", definition)

    seen_params: dict = {}

    async def fake_send(_send_fn, _instance, _command, params, **kwargs):  # noqa: ARG001
        seen_params.update(params)
        return {"success": True, "message": "ok"}

    monkeypatch.setattr(
        "services.custom_tool_service.send_with_unity_instance", fake_send)

    result = await service.execute_tool("proj-1", "long_running_tool", "Playground@abc", {})
    assert result.success is True
    assert seen_params["timeout_seconds"] == 300

    # An explicit caller value must win over the default.
    seen_params.clear()
    await service.execute_tool(
        "proj-1", "long_running_tool", "Playground@abc", {"timeout_seconds": 900})
    assert seen_params["timeout_seconds"] == 900

    # So must the camelCase spelling the hub also honors: no snake_case default beside it.
    seen_params.clear()
    await service.execute_tool(
        "proj-1", "long_running_tool", "Playground@abc", {"timeoutSeconds": 1200})
    assert seen_params == {"timeoutSeconds": 1200}


@pytest.mark.asyncio
async def test_polling_deadline_does_not_suggest_resubmitting(monkeypatch):
    """At the polling deadline the operation may still run: say so, and do not hint a retry."""
    service = _make_service()

    async def still_running(_send_fn, _instance, _command, params, **kwargs):  # noqa: ARG001
        return {"_mcp_status": "pending", "message": "working"}

    monkeypatch.setattr(
        "services.custom_tool_service.send_with_unity_instance", still_running)

    result = await service._poll_until_complete(
        "long_running_tool", "Playground@abc", {}, {"_mcp_status": "pending"}, "status",
        max_poll_seconds=0.01)

    assert result.success is False
    assert "may still be running" in result.message
    assert result.hint is None


@pytest.mark.asyncio
async def test_transport_timeout_error_is_self_explanatory(monkeypatch):
    from transport import unity_transport

    monkeypatch.setattr(config, "transport_mode", "http")

    async def raise_timeout(*args, **kwargs):  # noqa: ARG001
        raise asyncio.TimeoutError()

    monkeypatch.setattr(
        unity_transport.PluginHub, "send_command_for_instance", raise_timeout)

    async def unused_send_fn(*args, **kwargs):  # noqa: ARG001
        raise AssertionError("stdio path should not be used in http mode")

    response = await unity_transport.send_with_unity_instance(
        unused_send_fn, "Playground@abc", "long_running_tool", {})

    assert response["success"] is False
    assert "may or may not have executed" in response["error"]
    assert "timeout_seconds" in response["error"]
    assert response["hint"] == "retry"
    assert response["data"] == {"reason": "unity_timeout"}


@pytest.mark.asyncio
async def test_each_poll_waits_no_longer_than_the_remaining_budget(monkeypatch):
    """A poll must not inherit the tool's 300 s command budget past the polling deadline."""
    service = _make_service()
    seen: list[dict] = []

    async def complete(_send_fn, _instance, _command, params, **kwargs):  # noqa: ARG001
        seen.append(dict(params))
        return {"_mcp_status": "complete", "success": True, "message": "done"}

    monkeypatch.setattr(
        "services.custom_tool_service.send_with_unity_instance", complete)

    result = await service._poll_until_complete(
        "long_running_tool", "Playground@abc", {"timeout_seconds": 300, "timeoutSeconds": 300},
        {"_mcp_status": "pending", "_mcp_poll_interval": 0.01}, "status", max_poll_seconds=5)

    assert result.success is True
    assert seen and 0 < seen[0]["timeout_seconds"] <= 5
    assert "timeoutSeconds" not in seen[0]
    assert seen[0]["action"] == "status"


@pytest.mark.asyncio
async def test_a_hung_poll_is_cut_off_at_the_polling_deadline(monkeypatch):
    """The hub waits at least 30 s for any command; a poll must still end at the polling deadline."""
    service = _make_service()

    async def hang(_send_fn, _instance, _command, _params, **kwargs):  # noqa: ARG001
        await asyncio.sleep(30)

    monkeypatch.setattr(
        "services.custom_tool_service.send_with_unity_instance", hang)

    loop = asyncio.get_running_loop()
    started = loop.time()
    result = await service._poll_until_complete(
        "long_running_tool", "Playground@abc", {},
        {"_mcp_status": "pending", "_mcp_poll_interval": 0.01}, "status", max_poll_seconds=1)
    elapsed = loop.time() - started

    assert result.success is False
    assert "Timeout waiting for long_running_tool" in result.message
    assert elapsed < 2.0


@pytest.mark.asyncio
async def test_a_stdio_poll_stops_its_worker_thread_at_the_polling_deadline(monkeypatch):
    """On stdio the poll runs in an executor thread that a cancelled wait cannot stop. It must be given the
    remaining budget, so the thread itself ends at the polling deadline and releases the connection."""
    import time

    import transport.legacy.unity_connection as unity_connection

    calls: list[dict] = []

    class _BlockingConnection:
        def send_command(self, command_type, params, max_attempts=None, deadline=None):  # noqa: ARG002
            call = {"deadline": deadline, "ended": None}
            calls.append(call)
            started = time.monotonic()
            # Block like a wedged socket would, until the deadline the caller allows (5 s at most here).
            time.sleep(min(5.0, max(0.0, (deadline or started + 5.0) - started)))
            call["ended"] = time.monotonic()
            return {"_mcp_status": "pending", "_mcp_poll_interval": 0.01}

    monkeypatch.setattr(config, "transport_mode", "stdio")
    monkeypatch.setattr(unity_connection, "get_unity_connection", lambda instance_id=None, deadline=None: _BlockingConnection())

    service = _make_service()
    started = time.monotonic()
    result = await service._poll_until_complete(
        "long_running_tool", None, {},
        {"_mcp_status": "pending", "_mcp_poll_interval": 0.01}, "status", max_poll_seconds=1)
    await asyncio.sleep(0.2)  # let a thread still blocked past the deadline (the bug) record itself

    assert result.success is False
    assert calls, "the poll must have reached the stdio connection"
    assert all(c["deadline"] is not None and c["deadline"] <= started + 1.5 for c in calls)
    assert all(c["ended"] is not None and c["ended"] <= started + 1.5 for c in calls), "a poll thread outlived the deadline"


def test_budget_only_shortens_the_configured_command_deadline(monkeypatch):
    import time

    import transport.legacy.unity_connection as unity_connection

    deadlines: list[float] = []

    class _Connection:
        def send_command(self, command_type, params, max_attempts=None, deadline=None):  # noqa: ARG002
            deadlines.append(deadline - time.monotonic())
            return {"success": True}

    monkeypatch.setattr(unity_connection, "get_unity_connection", lambda instance_id=None, deadline=None: _Connection())
    monkeypatch.setattr(config, "command_total_timeout", 90.0)

    unity_connection.send_command_with_retry("x", {}, budget_s=2.0)
    unity_connection.send_command_with_retry("x", {}, budget_s=500.0)
    unity_connection.send_command_with_retry("x", {})

    assert deadlines[0] <= 2.0
    assert 85.0 < deadlines[1] <= 90.0
    assert 85.0 < deadlines[2] <= 90.0


def test_finding_the_instance_spends_the_budget(monkeypatch):
    """The budget starts before the connection lookup: a slow lookup leaves less for the command, and a
    lookup that spends all of it means the command is not sent at all."""
    import time

    import transport.legacy.unity_connection as unity_connection

    sent: list[float] = []
    lookup_deadlines: list[float | None] = []

    class _Connection:
        def send_command(self, command_type, params, max_attempts=None, deadline=None):  # noqa: ARG002
            sent.append(deadline - time.monotonic())
            return {"success": True}

    def slow_lookup(instance_id=None, deadline=None):  # noqa: ARG001
        lookup_deadlines.append(deadline)
        time.sleep(0.3)
        return _Connection()

    monkeypatch.setattr(unity_connection, "get_unity_connection", slow_lookup)
    monkeypatch.setattr(config, "command_total_timeout", 90.0)

    started = time.monotonic()
    unity_connection.send_command_with_retry("x", {}, budget_s=1.0)
    assert lookup_deadlines[0] is not None and lookup_deadlines[0] <= started + 1.05
    assert sent and sent[0] <= 0.75

    result = unity_connection.send_command_with_retry("x", {}, budget_s=0.2)
    assert len(sent) == 1, "a command whose budget the lookup spent must not be sent"
    assert result.success is False


def test_connect_caps_its_timeouts_to_the_deadline(monkeypatch):
    import socket
    import time

    import transport.legacy.unity_connection as unity_connection

    timeouts: list[float] = []

    def refuse(address, timeout=None):  # noqa: ARG001
        timeouts.append(timeout)
        raise ConnectionRefusedError("no editor here")

    monkeypatch.setattr(socket, "create_connection", refuse)
    monkeypatch.setattr(config, "connection_timeout", 30.0)

    conn = unity_connection.UnityConnection(port=1, instance_id="Playground@abc")
    assert conn.connect(deadline=time.monotonic() + 0.5) is False
    assert timeouts and timeouts[0] <= 0.5


def test_a_timed_out_custom_tool_carries_no_retry_hint():
    """A timeout may follow a command that ran: the caller is told to check, not to resubmit."""
    service = _make_service()
    result = service._normalize_response(
        {"success": False, "error": "late", "hint": "retry", "data": {"reason": "unity_timeout"}})
    assert result.hint is None
    assert result.data == {"reason": "unity_timeout"}


def _call_in_thread(fn, limit_s):
    """Run fn in a thread; return (elapsed, outcome) or fail if it is still blocked after limit_s."""
    import threading
    import time

    outcome: dict = {}

    def run():
        try:
            outcome["result"] = fn()
        except Exception as exc:  # noqa: BLE001
            outcome["error"] = exc

    started = time.monotonic()
    worker = threading.Thread(target=run, daemon=True)
    worker.start()
    worker.join(limit_s)
    assert not worker.is_alive(), f"still blocked after {limit_s} s"
    return time.monotonic() - started, outcome


def _stdio_connection(monkeypatch, tmp_path):
    import socket

    import transport.legacy.unity_connection as unity_connection

    # No status files: the reload preflight must not see a real editor's.
    monkeypatch.setattr(unity_connection.Path, "home", lambda: tmp_path)
    monkeypatch.setattr(unity_connection.stdio_port_registry, "get_instances", lambda **_: [])
    monkeypatch.setattr(unity_connection.stdio_port_registry, "get_instance", lambda *_: None)
    monkeypatch.setattr(unity_connection.stdio_port_registry, "get_port", lambda *_: 1)
    ours, unity = socket.socketpair()
    ours.settimeout(30.0)
    conn = unity_connection.UnityConnection(port=1, instance_id="Playground@abc")
    conn.sock = ours
    conn.use_framing = True
    return conn, unity


def test_a_command_waits_for_the_connection_only_while_its_budget_lasts(monkeypatch, tmp_path):
    import time

    conn, unity = _stdio_connection(monkeypatch, tmp_path)
    unity.setblocking(False)
    conn._io_lock.acquire()  # another command is using the connection
    try:
        elapsed, outcome = _call_in_thread(
            lambda: conn.send_command("x", {}, max_attempts=0, deadline=time.monotonic() + 0.3), 3.0)
    finally:
        conn._io_lock.release()
    assert isinstance(outcome.get("error"), TimeoutError)
    assert elapsed < 1.0
    assert conn.sock is not None, "the other command's socket must be left alone"
    try:
        sent = unity.recv(64)
    except BlockingIOError:
        sent = b""
    assert sent == b"", "a command whose budget ran out waiting must not be sent"


def test_a_response_in_pieces_is_cut_off_at_the_deadline(monkeypatch, tmp_path):
    import struct
    import threading
    import time

    conn, unity = _stdio_connection(monkeypatch, tmp_path)
    stop = threading.Event()

    def trickle():
        unity.sendall(struct.pack(">Q", 1000))
        while not stop.is_set():  # one byte every 0.2 s, never the whole payload
            try:
                unity.sendall(b"x")
            except OSError:
                return
            time.sleep(0.2)

    threading.Thread(target=trickle, daemon=True).start()
    try:
        elapsed, outcome = _call_in_thread(
            lambda: conn.receive_full_response(conn.sock, deadline=time.monotonic() + 0.5), 5.0)
    finally:
        stop.set()
    assert isinstance(outcome.get("error"), TimeoutError)
    assert elapsed < 1.0


def test_a_send_unity_does_not_read_is_cut_off_at_the_deadline(monkeypatch, tmp_path):
    import time

    conn, _unity = _stdio_connection(monkeypatch, tmp_path)
    big = {"blob": "x" * (8 * 1024 * 1024)}  # far beyond the socket buffers; Unity reads none of it
    elapsed, outcome = _call_in_thread(
        lambda: conn.send_command("x", big, max_attempts=0, deadline=time.monotonic() + 0.5), 5.0)
    assert isinstance(outcome.get("error"), TimeoutError)
    assert elapsed < 1.5


def test_the_liveness_check_keeps_the_socket_timeout(monkeypatch, tmp_path):
    conn, _unity = _stdio_connection(monkeypatch, tmp_path)
    conn._ensure_live_connection()
    assert conn.sock is not None
    assert conn.sock.gettimeout() == 30.0


def test_a_reconnect_handshake_spends_the_command_budget(monkeypatch, tmp_path):
    import time

    import transport.legacy.unity_connection as unity_connection

    conn, _unity = _stdio_connection(monkeypatch, tmp_path)
    conn.sock = None  # the stale socket was dropped: send_command reconnects
    seen: list = []

    def fake_connect(self, connect_timeout=None, deadline=None):  # noqa: ARG001
        seen.append(deadline)
        return False

    monkeypatch.setattr(unity_connection.UnityConnection, "connect", fake_connect)
    deadline = time.monotonic() + 0.5
    _call_in_thread(lambda: conn.send_command("x", {}, max_attempts=0, deadline=deadline), 3.0)
    assert seen == [deadline]


def test_the_reload_preflight_waits_for_the_connection_only_while_the_budget_lasts(monkeypatch, tmp_path):
    import json
    import time

    conn, _unity = _stdio_connection(monkeypatch, tmp_path)
    status_dir = tmp_path / ".unity-mcp"
    status_dir.mkdir()
    (status_dir / "unity-mcp-status-abc.json").write_text(json.dumps({"reloading": True}))
    conn._io_lock.acquire()  # another command is using the connection
    try:
        elapsed, outcome = _call_in_thread(
            lambda: conn.send_command("x", {}, max_attempts=0, deadline=time.monotonic() + 0.3), 3.0)
    finally:
        conn._io_lock.release()
    assert elapsed < 1.0
    assert outcome["result"].hint == "retry"
    assert conn.sock is not None, "the socket of the command holding the lock is left alone"


def test_a_waiting_command_leaves_the_socket_alone_until_its_turn(monkeypatch, tmp_path):
    """The liveness check reads and resets the socket's timeout: done while another command holds the
    connection, it could put back a longer timeout under that command's send."""
    import time

    conn, _unity = _stdio_connection(monkeypatch, tmp_path)
    checked: list[bool] = []
    monkeypatch.setattr(conn, "_ensure_live_connection", lambda: checked.append(True))
    conn._io_lock.acquire()  # another command is using the connection
    try:
        _call_in_thread(
            lambda: conn.send_command("x", {}, max_attempts=0, deadline=time.monotonic() + 0.3), 3.0)
    finally:
        conn._io_lock.release()
    assert checked == [], "the liveness check ran while another command held the connection"


def test_the_handshake_ends_at_the_earlier_of_its_timeout_and_the_deadline(monkeypatch):
    import socket
    import threading
    import time

    import transport.legacy.unity_connection as unity_connection

    ours, unity = socket.socketpair()
    stop = threading.Event()

    def trickle():  # a byte every 0.45 s, never the end of the handshake line
        while not stop.is_set():
            try:
                unity.sendall(b"F")
            except OSError:
                return
            time.sleep(0.45)

    monkeypatch.setattr(socket, "create_connection", lambda *_a, **_k: ours)
    monkeypatch.setattr(config, "handshake_timeout", 5.0)
    threading.Thread(target=trickle, daemon=True).start()
    conn = unity_connection.UnityConnection(port=1, instance_id="Playground@abc")
    try:
        elapsed, _ = _call_in_thread(lambda: conn.connect(deadline=time.monotonic() + 0.5), 5.0)
    finally:
        stop.set()
    assert elapsed < 0.75
