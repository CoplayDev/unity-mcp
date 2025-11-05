from unity_connection import UnityConnection
import sys
import json
import struct
import socket
import threading
import time
import select
from pathlib import Path

import pytest

# locate server src dynamically to avoid hardcoded layout assumptions
ROOT = Path(__file__).resolve().parents[1]
candidates = [
    ROOT / "MCPForUnity" / "UnityMcpServer~" / "src",
    ROOT / "UnityMcpServer~" / "src",
]
SRC = next((p for p in candidates if p.exists()), None)
if SRC is None:
    searched = "\n".join(str(p) for p in candidates)
    pytest.skip(
        "MCP for Unity server source not found. Tried:\n" + searched,
        allow_module_level=True,
    )
# Tests can now import directly from parent package


def start_dummy_server(greeting: bytes, respond_ping: bool = False):
    """Start a minimal TCP server for handshake tests."""
    sock = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
    sock.bind(("127.0.0.1", 0))
    sock.listen(1)
    port = sock.getsockname()[1]
    ready = threading.Event()

    def _run():
        ready.set()
        conn, _ = sock.accept()
        conn.settimeout(1.0)
        if greeting:
            conn.sendall(greeting)
        if respond_ping:
            try:
                # Read exactly n bytes helper
                def _read_exact(n: int) -> bytes:
                    buf = b""
                    while len(buf) < n:
                        chunk = conn.recv(n - len(buf))
                        if not chunk:
                            break
                        buf += chunk
                    return buf

                header = _read_exact(8)
                if len(header) == 8:
                    length = struct.unpack(">Q", header)[0]
                    payload = _read_exact(length)
                    if payload == b'{"type":"ping"}':
                        resp = b'{"type":"pong"}'
                        conn.sendall(struct.pack(">Q", len(resp)) + resp)
            except Exception:
                pass
        time.sleep(0.1)
        try:
            conn.close()
        except Exception:
            pass
        finally:
            sock.close()

    threading.Thread(target=_run, daemon=True).start()
    ready.wait()
    return port


def start_handshake_enforcing_server():
    """Server that drops connection if client sends data before handshake."""
    sock = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
    sock.bind(("127.0.0.1", 0))
    sock.listen(1)
    port = sock.getsockname()[1]
    ready = threading.Event()

    def _run():
        ready.set()
        conn, _ = sock.accept()
        # If client sends any data before greeting, disconnect (poll briefly)
        try:
            conn.setblocking(False)
            deadline = time.time() + 0.15  # short, reduces race with legitimate clients
            while time.time() < deadline:
                r, _, _ = select.select([conn], [], [], 0.01)
                if r:
                    try:
                        peek = conn.recv(1, socket.MSG_PEEK)
                    except BlockingIOError:
                        peek = b""
                    except Exception:
                        peek = b"\x00"
                    if peek:
                        conn.close()
                        sock.close()
                        return
            # No pre-handshake data observed; send greeting
            conn.setblocking(True)
            conn.sendall(b"MCP/0.1 FRAMING=1\n")
            time.sleep(0.1)
        finally:
            try:
                conn.close()
            finally:
                sock.close()

    threading.Thread(target=_run, daemon=True).start()
    ready.wait()
    return port


def test_handshake_requires_framing():
    port = start_dummy_server(b"MCP/0.1\n")
    conn = UnityConnection(host="127.0.0.1", port=port)
    assert conn.connect() is False
    assert conn.sock is None


def test_small_frame_ping_pong():
    port = start_dummy_server(b"MCP/0.1 FRAMING=1\n", respond_ping=True)
    conn = UnityConnection(host="127.0.0.1", port=port)
    try:
        assert conn.connect() is True
        assert conn.use_framing is True
        payload = b'{"type":"ping"}'
        conn.sock.sendall(struct.pack(">Q", len(payload)) + payload)
        resp = conn.receive_full_response(conn.sock)
        assert json.loads(resp.decode("utf-8"))["type"] == "pong"
    finally:
        conn.disconnect()


def test_unframed_data_disconnect():
    port = start_handshake_enforcing_server()
    sock = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
    sock.connect(("127.0.0.1", port))
    sock.settimeout(1.0)
    sock.sendall(b"BAD")
    time.sleep(0.4)
    try:
        data = sock.recv(1024)
        assert data == b""
    except (ConnectionResetError, ConnectionAbortedError):
        # Some platforms raise instead of returning empty bytes when the
        # server closes the connection after detecting pre-handshake data.
        pass
    finally:
        sock.close()


def test_zero_length_payload_heartbeat():
    # Server that sends handshake and a zero-length heartbeat frame followed by a pong payload
    import socket
    import struct
    import threading
    import time

    sock = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
    sock.bind(("127.0.0.1", 0))
    sock.listen(1)
    port = sock.getsockname()[1]
    ready = threading.Event()

    def _run():
        ready.set()
        conn, _ = sock.accept()
        try:
            conn.sendall(b"MCP/0.1 FRAMING=1\n")
            time.sleep(0.02)
            # Heartbeat frame (length=0)
            conn.sendall(struct.pack(">Q", 0))
            time.sleep(0.02)
            # Real payload frame
            payload = b'{"type":"pong"}'
            conn.sendall(struct.pack(">Q", len(payload)) + payload)
            time.sleep(0.02)
        finally:
            try:
                conn.close()
            except Exception:
                pass
            sock.close()

    threading.Thread(target=_run, daemon=True).start()
    ready.wait()

    conn = UnityConnection(host="127.0.0.1", port=port)
    try:
        assert conn.connect() is True
        # Receive should skip heartbeat and return the pong payload (or empty if only heartbeats seen)
        resp = conn.receive_full_response(conn.sock)
        assert resp in (b'{"type":"pong"}', b"")
    finally:
        conn.disconnect()


@pytest.mark.skip(reason="TODO: oversized payload should disconnect")
def test_oversized_payload_rejected():
    pass


@pytest.mark.skip(reason="TODO: partial header/payload triggers timeout and disconnect")
def test_partial_frame_timeout():
    pass


@pytest.mark.skip(reason="TODO: concurrency test with parallel tool invocations")
def test_parallel_invocations_no_interleaving():
    pass


@pytest.mark.skip(reason="TODO: reconnection after drop mid-command")
def test_reconnect_mid_command():
    pass
