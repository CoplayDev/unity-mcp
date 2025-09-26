import contextlib
import errno
import json
import logging
import random
import socket
import struct
import threading
import time
from dataclasses import dataclass
from pathlib import Path
from typing import Any, Dict
from config import config
from port_discovery import PortDiscovery

# Configure logging using settings from config
logging.basicConfig(
    level=getattr(logging, config.log_level),
    format=config.log_format
)
logger = logging.getLogger("mcp-for-unity-server")

# Module-level lock to guard global connection initialization
_connection_lock = threading.Lock()

# Maximum allowed framed payload size (64 MiB)
FRAMED_MAX = 64 * 1024 * 1024

@dataclass
class UnityConnection:
    """Manages the socket connection to the Unity Editor."""
    host: str = config.unity_host
    port: int = None  # Will be set dynamically
    sock: socket.socket = None  # Socket for Unity communication
    use_framing: bool = False  # Negotiated per-connection
    
    def __post_init__(self):
        """Set port from discovery if not explicitly provided"""
        if self.port is None:
            self.port = PortDiscovery.discover_unity_port()
        self._io_lock = threading.Lock()
        self._conn_lock = threading.Lock()

    def connect(self) -> bool:
        """Establish a connection to the Unity Editor."""
        if self.sock:
            return True
        with self._conn_lock:
            if self.sock:
                return True
            try:
                # Bounded connect to avoid indefinite blocking
                connect_timeout = float(getattr(config, "connect_timeout", getattr(config, "connection_timeout", 1.0)))
                self.sock = socket.create_connection((self.host, self.port), connect_timeout)
                # Disable Nagle's algorithm to reduce small RPC latency
                with contextlib.suppress(Exception):
                    self.sock.setsockopt(socket.IPPROTO_TCP, socket.TCP_NODELAY, 1)
                logger.debug(f"Connected to Unity at {self.host}:{self.port}")

                # Strict handshake: require FRAMING=1
                try:
                    require_framing = getattr(config, "require_framing", True)
                    timeout = float(getattr(config, "handshake_timeout", 1.0))
                    self.sock.settimeout(timeout)
                    buf = bytearray()
                    deadline = time.monotonic() + timeout
                    while time.monotonic() < deadline and len(buf) < 512:
                        try:
                            chunk = self.sock.recv(256)
                            if not chunk:
                                break
                            buf.extend(chunk)
                            if b"\n" in buf:
                                break
                        except socket.timeout:
                            break
                    text = bytes(buf).decode('ascii', errors='ignore').strip()

                    if 'FRAMING=1' in text:
                        self.use_framing = True
                        logger.debug('Unity MCP handshake received: FRAMING=1 (strict)')
                    else:
                        if require_framing:
                            # Best-effort plain-text advisory for legacy peers
                            with contextlib.suppress(Exception):
                                self.sock.sendall(b'Unity MCP requires FRAMING=1\n')
                            raise ConnectionError(f'Unity MCP requires FRAMING=1, got: {text!r}')
                        else:
                            self.use_framing = False
                            logger.warning('Unity MCP handshake missing FRAMING=1; proceeding in legacy mode by configuration')
                finally:
                    self.sock.settimeout(config.connection_timeout)
                return True
            except Exception as e:
                logger.error(f"Failed to connect to Unity: {str(e)}")
                try:
                    if self.sock:
                        self.sock.close()
                except Exception:
                    pass
                self.sock = None
                return False

    def disconnect(self):
        """Close the connection to the Unity Editor."""
        if self.sock:
            try:
                self.sock.close()
            except Exception as e:
                logger.error(f"Error disconnecting from Unity: {str(e)}")
            finally:
                self.sock = None

    def _read_exact(self, sock: socket.socket, count: int) -> bytes:
        data = bytearray()
        while len(data) < count:
            chunk = sock.recv(count - len(data))
            if not chunk:
                raise ConnectionError("Connection closed before reading expected bytes")
            data.extend(chunk)
        return bytes(data)

    def receive_full_response(self, sock, buffer_size=config.buffer_size) -> bytes:
        """Receive a complete response from Unity, handling chunked data."""
        if self.use_framing:
            try:
                # Consume heartbeats, but do not hang indefinitely if only zero-length frames arrive
                heartbeat_count = 0
                deadline = time.monotonic() + getattr(config, 'framed_receive_timeout', 2.0)
                while True:
                    header = self._read_exact(sock, 8)
                    payload_len = struct.unpack('>Q', header)[0]
                    if payload_len == 0:
                        # Heartbeat/no-op frame: consume and continue waiting for a data frame
                        logger.debug("Received heartbeat frame (length=0)")
                        heartbeat_count += 1
                        if heartbeat_count >= getattr(config, 'max_heartbeat_frames', 16) or time.monotonic() > deadline:
                            # Treat as empty successful response to match C# server behavior
                            logger.debug("Heartbeat threshold reached; returning empty response")
                            return b""
                        continue
                    if payload_len > FRAMED_MAX:
                        raise ValueError(f"Invalid framed length: {payload_len}")
                    payload = self._read_exact(sock, payload_len)
                    logger.debug(f"Received framed response ({len(payload)} bytes)")
                    return payload
            except socket.timeout as e:
                logger.warning("Socket timeout during framed receive")
                raise TimeoutError("Timeout receiving Unity response") from e
            except Exception as e:
                logger.error(f"Error during framed receive: {str(e)}")
                raise

        chunks = []
        # Respect the socket's currently configured timeout
        try:
            while True:
                chunk = sock.recv(buffer_size)
                if not chunk:
                    if not chunks:
                        raise Exception("Connection closed before receiving data")
                    break
                chunks.append(chunk)
                
                # Process the data received so far
                data = b''.join(chunks)
                decoded_data = data.decode('utf-8')
                
                # Check if we've received a complete response
                try:
                    # Special case for ping-pong
                    if decoded_data.strip().startswith('{"status":"success","result":{"message":"pong"'):
                        logger.debug("Received ping response")
                        return data
                    
                    # Handle escaped quotes in the content
                    if '"content":' in decoded_data:
                        # Find the content field and its value
                        content_start = decoded_data.find('"content":') + 9
                        content_end = decoded_data.rfind('"', content_start)
                        if content_end > content_start:
                            # Replace escaped quotes in content with regular quotes
                            content = decoded_data[content_start:content_end]
                            content = content.replace('\\"', '"')
                            decoded_data = decoded_data[:content_start] + content + decoded_data[content_end:]
                    
                    # Validate JSON format
                    json.loads(decoded_data)
                    
                    # If we get here, we have valid JSON
                    logger.info(f"Received complete response ({len(data)} bytes)")
                    return data
                except json.JSONDecodeError:
                    # We haven't received a complete valid JSON response yet
                    continue
                except Exception as e:
                    logger.warning(f"Error processing response chunk: {str(e)}")
                    # Continue reading more chunks as this might not be the complete response
                    continue
        except socket.timeout:
            logger.warning("Socket timeout during receive")
            raise Exception("Timeout receiving Unity response")
        except Exception as e:
            logger.error(f"Error during receive: {str(e)}")
            raise

    def send_command(self, command_type: str, params: Dict[str, Any] = None) -> Dict[str, Any]:
        """Send a command with retry/backoff and port rediscovery. Pings only when requested."""
        # Defensive guard: catch empty/placeholder invocations early
        if not command_type:
            raise ValueError("MCP call missing command_type")
        if params is None:
            # Return a fast, structured error that clients can display without hanging
            return {"success": False, "error": "MCP call received with no parameters (client placeholder?)"}
        attempts = max(config.max_retries, 5)
        base_backoff = max(0.5, config.retry_delay)

        def read_status_file() -> dict | None:
            try:
                status_files = sorted(Path.home().joinpath('.unity-mcp').glob('unity-mcp-status-*.json'), key=lambda p: p.stat().st_mtime, reverse=True)
                if not status_files:
                    return None
                latest = status_files[0]
                with latest.open('r') as f:
                    return json.load(f)
            except Exception:
                return None

        last_short_timeout = None

        # Preflight: if Unity reports reloading, return a structured hint so clients can retry politely
        try:
            status = read_status_file()
            if status and (status.get('reloading') or status.get('reason') == 'reloading'):
                return {
                    "success": False,
                    "state": "reloading",
                    "retry_after_ms": int(config.reload_retry_ms),
                    "error": "Unity domain reload in progress",
                    "message": "Unity is reloading scripts; please retry shortly"
                }
        except Exception:
            pass

        for attempt in range(attempts + 1):
            try:
                # Ensure connected (handshake occurs within connect())
                if not self.sock and not self.connect():
                    raise Exception("Could not connect to Unity")

                # Build payload
                if command_type == 'ping':
                    payload = b'ping'
                else:
                    command = {"type": command_type, "params": params or {}}
                    payload = json.dumps(command, ensure_ascii=False).encode('utf-8')

                # Send/receive are serialized to protect the shared socket
                with self._io_lock:
                    mode = 'framed' if self.use_framing else 'legacy'
                    with contextlib.suppress(Exception):
                        logger.debug(
                            "send %d bytes; mode=%s; head=%s",
                            len(payload),
                            mode,
                            (payload[:32]).decode('utf-8', 'ignore'),
                        )
                    if self.use_framing:
                        header = struct.pack('>Q', len(payload))
                        self.sock.sendall(header)
                        self.sock.sendall(payload)
                    else:
                        self.sock.sendall(payload)

                    # During retry bursts use a short receive timeout and ensure restoration
                    restore_timeout = None
                    if attempt > 0 and last_short_timeout is None:
                        restore_timeout = self.sock.gettimeout()
                        self.sock.settimeout(1.0)
                    try:
                        response_data = self.receive_full_response(self.sock)
                        with contextlib.suppress(Exception):
                            logger.debug("recv %d bytes; mode=%s", len(response_data), mode)
                    finally:
                        if restore_timeout is not None:
                            self.sock.settimeout(restore_timeout)
                            last_short_timeout = None

                # Parse
                if command_type == 'ping':
                    resp = json.loads(response_data.decode('utf-8'))
                    if resp.get('status') == 'success' and resp.get('result', {}).get('message') == 'pong':
                        return {"message": "pong"}
                    raise Exception("Ping unsuccessful")

                resp = json.loads(response_data.decode('utf-8'))
                if resp.get('status') == 'error':
                    err = resp.get('error') or resp.get('message', 'Unknown Unity error')
                    raise Exception(err)
                return resp.get('result', {})
            except Exception as e:
                logger.warning(f"Unity communication attempt {attempt+1} failed: {e}")
                try:
                    if self.sock:
                        self.sock.close()
                finally:
                    self.sock = None

                # Re-discover port each time
                try:
                    new_port = PortDiscovery.discover_unity_port()
                    if new_port != self.port:
                        logger.info(f"Unity port changed {self.port} -> {new_port}")
                    self.port = new_port
                except Exception as de:
                    logger.debug(f"Port discovery failed: {de}")

                if attempt < attempts:
                    # Heartbeat-aware, jittered backoff
                    status = read_status_file()
                    # Base exponential backoff
                    backoff = base_backoff * (2 ** attempt)
                    # Decorrelated jitter multiplier
                    jitter = random.uniform(0.1, 0.3)

                    # Fast‑retry for transient socket failures
                    fast_error = isinstance(e, (ConnectionRefusedError, ConnectionResetError, TimeoutError))
                    if not fast_error:
                        try:
                            err_no = getattr(e, 'errno', None)
                            fast_error = err_no in (errno.ECONNREFUSED, errno.ECONNRESET, errno.ETIMEDOUT)
                        except Exception:
                            pass

                    # Cap backoff depending on state
                    if status and status.get('reloading'):
                        cap = 0.8
                    elif fast_error:
                        cap = 0.25
                    else:
                        cap = 3.0

                    sleep_s = min(cap, jitter * (2 ** attempt))
                    time.sleep(sleep_s)
                    continue
                raise

# Global Unity connection
_unity_connection = None

def get_unity_connection() -> UnityConnection:
    """Retrieve or establish a persistent Unity connection.

    Note: Do NOT ping on every retrieval to avoid connection storms. Rely on
    send_command() exceptions to detect broken sockets and reconnect there.
    """
    global _unity_connection
    if _unity_connection is not None:
        return _unity_connection

    # Double-checked locking to avoid concurrent socket creation
    with _connection_lock:
        if _unity_connection is not None:
            return _unity_connection
        logger.info("Creating new Unity connection")
        _unity_connection = UnityConnection()
        if not _unity_connection.connect():
            _unity_connection = None
            raise ConnectionError("Could not connect to Unity. Ensure the Unity Editor and MCP Bridge are running.")
        logger.info("Connected to Unity on startup")
        return _unity_connection


# -----------------------------
# Centralized retry helpers
# -----------------------------

def _is_reloading_response(resp: dict) -> bool:
    """Return True if the Unity response indicates the editor is reloading."""
    if not isinstance(resp, dict):
        return False
    if resp.get("state") == "reloading":
        return True
    message_text = (resp.get("message") or resp.get("error") or "").lower()
    return "reload" in message_text


def send_command_with_retry(command_type: str, params: Dict[str, Any], *, max_retries: int | None = None, retry_ms: int | None = None) -> Dict[str, Any]:
    """Send a command via the shared connection, waiting politely through Unity reloads.

    Uses config.reload_retry_ms and config.reload_max_retries by default. Preserves the
    structured failure if retries are exhausted.
    """
    conn = get_unity_connection()
    if max_retries is None:
        max_retries = getattr(config, "reload_max_retries", 40)
    if retry_ms is None:
        retry_ms = getattr(config, "reload_retry_ms", 250)

    response = conn.send_command(command_type, params)
    retries = 0
    while _is_reloading_response(response) and retries < max_retries:
        delay_ms = int(response.get("retry_after_ms", retry_ms)) if isinstance(response, dict) else retry_ms
        time.sleep(max(0.0, delay_ms / 1000.0))
        retries += 1
        response = conn.send_command(command_type, params)
    return response


async def async_send_command_with_retry(command_type: str, params: Dict[str, Any], *, loop=None, max_retries: int | None = None, retry_ms: int | None = None) -> Dict[str, Any]:
    """Async wrapper that runs the blocking retry helper in a thread pool."""
    try:
        import asyncio  # local import to avoid mandatory asyncio dependency for sync callers
        if loop is None:
            loop = asyncio.get_running_loop()
        return await loop.run_in_executor(
            None,
            lambda: send_command_with_retry(command_type, params, max_retries=max_retries, retry_ms=retry_ms),
        )
    except Exception as e:
        # Return a structured error dict for consistency with other responses
        return {"success": False, "error": f"Python async retry helper failed: {str(e)}"}
