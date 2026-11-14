"""Stdio delivery against a fake framed bridge: what each command attempt puts on the wire."""
from __future__ import annotations

import json
import socket
import struct
import threading

import pytest

from core.config import config
from transport.legacy import unity_connection as uc
from transport.legacy.unity_connection import UnityCommandError, UnityConnection


def _start_bridge(reply: dict):
    """Framed bridge that records every command payload and answers with ``reply``."""
    srv = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
    srv.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
    srv.bind(("127.0.0.1", 0))
    srv.listen(8)
    received: list[dict] = []
    stop = threading.Event()

    def recv_exact(conn, n):
        buf = b""
        while len(buf) < n:
            chunk = conn.recv(n - len(buf))
            if not chunk:
                raise ConnectionError
            buf += chunk
        return buf

    def serve_client(conn):
        try:
            conn.sendall(b"WELCOME UNITY-MCP 1 FRAMING=1\n")
            while not stop.is_set():
                size = struct.unpack(">Q", recv_exact(conn, 8))[0]
                body = recv_exact(conn, size)
                received.append(json.loads(body))
                out = json.dumps(reply).encode()
                conn.sendall(struct.pack(">Q", len(out)) + out)
        except (ConnectionError, OSError):
            pass

    def serve():
        srv.settimeout(0.5)
        while not stop.is_set():
            try:
                client, _ = srv.accept()
            except socket.timeout:
                continue
            except OSError:
                break
            threading.Thread(target=serve_client, args=(client,), daemon=True).start()

    threading.Thread(target=serve, daemon=True).start()
    return srv, srv.getsockname()[1], stop, received


@pytest.fixture
def bridge(monkeypatch, tmp_path):
    started = {}

    def make(reply):
        srv, port, stop, received = _start_bridge(reply)
        started.update(srv=srv, stop=stop)
        monkeypatch.setattr(uc.Path, "home", lambda: tmp_path)
        monkeypatch.setattr(uc.stdio_port_registry, "get_port", lambda instance_id=None: port)
        monkeypatch.setattr(uc.stdio_port_registry, "get_instance", lambda instance_id: None)
        monkeypatch.setattr(config, "connection_timeout", 2.0)
        return port, received

    yield make
    if started:
        started["stop"].set()
        started["srv"].close()


def test_stdio_unity_error_is_not_resent(bridge):
    port, received = bridge({"status": "error", "error": "GameObject 'X' not found"})
    conn = UnityConnection(port=port)
    with pytest.raises(UnityCommandError, match="not found"):
        conn.send_command("manage_gameobject", {"action": "delete", "target": "X"})
    assert len(received) == 1
