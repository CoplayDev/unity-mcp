"""Connection resilience when a domain reload leaves the Unity socket half-open."""
from __future__ import annotations

import json
import socket
import threading
import time
from pathlib import Path

import pytest

from core.config import config
import transport.legacy.unity_connection as uc
from transport.legacy.unity_connection import UnityConnection


def _start_silent_bridge():
    """Accept and handshake, then never answer (a wedged half-open bridge)."""
    srv = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
    srv.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
    srv.bind(("127.0.0.1", 0))
    srv.listen(8)
    port = srv.getsockname()[1]
    accepted: list[socket.socket] = []
    stop = threading.Event()

    def serve():
        srv.settimeout(0.5)
        while not stop.is_set():
            try:
                client, _ = srv.accept()
            except socket.timeout:
                continue
            except OSError:
                break
            accepted.append(client)
            try:
                client.sendall(b"WELCOME UNITY-MCP 1 FRAMING=1\n")
            except OSError:
                pass

    threading.Thread(target=serve, daemon=True).start()
    return srv, port, stop, accepted


def _write_reloading_status(tmp_path):
    status_dir = Path(tmp_path) / ".unity-mcp"
    status_dir.mkdir(parents=True, exist_ok=True)
    (status_dir / "unity-mcp-status-deadbeef.json").write_text(
        json.dumps({"reloading": True, "reason": "reloading", "project_path": "x"})
    )


@pytest.fixture
def silent_bridge(monkeypatch, tmp_path):
    srv, port, stop, accepted = _start_silent_bridge()
    monkeypatch.setattr(uc.Path, "home", lambda: Path(tmp_path))
    monkeypatch.setattr(uc.stdio_port_registry, "get_port", lambda instance_id=None: port)
    monkeypatch.setattr(uc.stdio_port_registry, "get_instance", lambda instance_id: None)
    monkeypatch.setattr(config, "connection_timeout", 1.0)
    monkeypatch.setattr(config, "command_total_timeout", 2.0, raising=False)
    try:
        yield port
    finally:
        stop.set()
        try:
            srv.close()
        except OSError:
            pass
        for client in accepted:
            try:
                client.close()
            except OSError:
                pass


def test_send_command_against_wedged_socket_is_bounded(silent_bridge, monkeypatch):
    """A connection_timeout longer than the total budget must not let a single blocking
    recv overrun the ceiling: the deadline caps each recv, so the call still stops near
    command_total_timeout rather than connection_timeout."""
    monkeypatch.setattr(config, "connection_timeout", 5.0)
    conn = UnityConnection(port=silent_bridge, instance_id="Repro@deadbeef")
    start = time.monotonic()
    with pytest.raises(TimeoutError, match="exceeded total deadline"):
        conn.send_command("get_editor_state", {})
    assert time.monotonic() - start < 4.0


def test_send_command_with_retry_is_bounded_by_deadline(silent_bridge, tmp_path, monkeypatch):
    """The reload-wait loop honours command_total_timeout, not just max_wait_s."""
    conn = UnityConnection(port=silent_bridge, instance_id="Repro@deadbeef")
    monkeypatch.setattr(uc, "get_unity_connection", lambda instance_id=None: conn)
    _write_reloading_status(tmp_path)

    start = time.monotonic()
    resp = uc.send_command_with_retry("get_editor_state", {}, instance_id="Repro@deadbeef")
    assert time.monotonic() - start < 6.0
    assert uc._extract_response_reason(resp) == "reloading"


def test_reload_signal_drops_socket_for_fresh_reconnect(silent_bridge, tmp_path):
    """A 'reloading' status drops the socket so the next command reconnects."""
    conn = UnityConnection(port=silent_bridge, instance_id="Repro@deadbeef")
    assert conn.connect() is True
    assert conn.sock is not None
    _write_reloading_status(tmp_path)

    response = conn.send_command("get_editor_state", {})
    assert conn.sock is None
    assert uc._extract_response_reason(response) == "reloading"
