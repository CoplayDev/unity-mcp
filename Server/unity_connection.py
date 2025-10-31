from config import config
import contextlib
from dataclasses import dataclass
import errno
import json
import logging
import os
from pathlib import Path
from port_discovery import PortDiscovery
import random
import socket
import struct
import threading
import time
from typing import Any, Dict, Optional, List

from models import MCPResponse, UnityInstanceInfo


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
                connect_timeout = float(
                    getattr(config, "connect_timeout", getattr(config, "connection_timeout", 1.0)))
                self.sock = socket.create_connection(
                    (self.host, self.port), connect_timeout)
                # Disable Nagle's algorithm to reduce small RPC latency
                with contextlib.suppress(Exception):
                    self.sock.setsockopt(
                        socket.IPPROTO_TCP, socket.TCP_NODELAY, 1)
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
                        logger.debug(
                            'MCP for Unity handshake received: FRAMING=1 (strict)')
                    else:
                        if require_framing:
                            # Best-effort plain-text advisory for legacy peers
                            with contextlib.suppress(Exception):
                                self.sock.sendall(
                                    b'MCP for Unity requires FRAMING=1\n')
                            raise ConnectionError(
                                f'MCP for Unity requires FRAMING=1, got: {text!r}')
                        else:
                            self.use_framing = False
                            logger.warning(
                                'MCP for Unity handshake missing FRAMING=1; proceeding in legacy mode by configuration')
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
                raise ConnectionError(
                    "Connection closed before reading expected bytes")
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
                            logger.debug(
                                "Heartbeat threshold reached; returning empty response")
                            return b""
                        continue
                    if payload_len > FRAMED_MAX:
                        raise ValueError(
                            f"Invalid framed length: {payload_len}")
                    payload = self._read_exact(sock, payload_len)
                    logger.debug(
                        f"Received framed response ({len(payload)} bytes)")
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
                        raise Exception(
                            "Connection closed before receiving data")
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
                            decoded_data = decoded_data[:content_start] + \
                                content + decoded_data[content_end:]

                    # Validate JSON format
                    json.loads(decoded_data)

                    # If we get here, we have valid JSON
                    logger.info(
                        f"Received complete response ({len(data)} bytes)")
                    return data
                except json.JSONDecodeError:
                    # We haven't received a complete valid JSON response yet
                    continue
                except Exception as e:
                    logger.warning(
                        f"Error processing response chunk: {str(e)}")
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
            return MCPResponse(success=False, error="MCP call received with no parameters (client placeholder?)")
        attempts = max(config.max_retries, 5)
        base_backoff = max(0.5, config.retry_delay)

        def read_status_file() -> dict | None:
            try:
                status_files = sorted(Path.home().joinpath(
                    '.unity-mcp').glob('unity-mcp-status-*.json'), key=lambda p: p.stat().st_mtime, reverse=True)
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
                return MCPResponse(
                    success=False,
                    error="Unity domain reload in progress, please try again shortly",
                    data={"state": "reloading", "retry_after_ms": int(
                        config.reload_retry_ms)}
                )
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
                    payload = json.dumps(
                        command, ensure_ascii=False).encode('utf-8')

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
                            logger.debug("recv %d bytes; mode=%s",
                                         len(response_data), mode)
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
                    err = resp.get('error') or resp.get(
                        'message', 'Unknown Unity error')
                    raise Exception(err)
                return resp.get('result', {})
            except Exception as e:
                logger.warning(
                    f"Unity communication attempt {attempt+1} failed: {e}")
                try:
                    if self.sock:
                        self.sock.close()
                finally:
                    self.sock = None

                # Re-discover port each time
                try:
                    new_port = PortDiscovery.discover_unity_port()
                    if new_port != self.port:
                        logger.info(
                            f"Unity port changed {self.port} -> {new_port}")
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
                    fast_error = isinstance(
                        e, (ConnectionRefusedError, ConnectionResetError, TimeoutError))
                    if not fast_error:
                        try:
                            err_no = getattr(e, 'errno', None)
                            fast_error = err_no in (
                                errno.ECONNREFUSED, errno.ECONNRESET, errno.ETIMEDOUT)
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


# -----------------------------
# Connection Pool for Multiple Unity Instances
# -----------------------------

class UnityConnectionPool:
    """Manages connections to multiple Unity Editor instances"""

    def __init__(self):
        self._connections: Dict[str, UnityConnection] = {}
        self._known_instances: Dict[str, UnityInstanceInfo] = {}
        self._last_full_scan: float = 0
        self._scan_interval: float = 5.0  # Cache for 5 seconds
        self._pool_lock = threading.Lock()
        self._default_instance_id: Optional[str] = None

        # Check for default instance from environment
        env_default = os.environ.get("UNITY_MCP_DEFAULT_INSTANCE", "").strip()
        if env_default:
            self._default_instance_id = env_default
            logger.info(f"Default Unity instance set from environment: {env_default}")

    def discover_all_instances(self, force_refresh: bool = False) -> List[UnityInstanceInfo]:
        """
        Discover all running Unity Editor instances.

        Args:
            force_refresh: If True, bypass cache and scan immediately

        Returns:
            List of UnityInstanceInfo objects
        """
        now = time.time()

        # Return cached results if valid
        if not force_refresh and (now - self._last_full_scan) < self._scan_interval:
            logger.debug(f"Returning cached Unity instances (age: {now - self._last_full_scan:.1f}s)")
            return list(self._known_instances.values())

        # Scan for instances
        logger.debug("Scanning for Unity instances...")
        instances = PortDiscovery.discover_all_unity_instances()

        # Update cache
        with self._pool_lock:
            self._known_instances = {inst.id: inst for inst in instances}
            self._last_full_scan = now

        logger.info(f"Found {len(instances)} Unity instances: {[inst.id for inst in instances]}")
        return instances

    def _resolve_instance_id(self, instance_identifier: Optional[str], instances: List[UnityInstanceInfo]) -> UnityInstanceInfo:
        """
        Resolve an instance identifier to a specific Unity instance.

        Args:
            instance_identifier: User-provided identifier (name, hash, name@hash, path, port, or None)
            instances: List of available instances

        Returns:
            Resolved UnityInstanceInfo

        Raises:
            ConnectionError: If instance cannot be resolved
        """
        if not instances:
            raise ConnectionError(
                "No Unity Editor instances found. Please ensure Unity is running with MCP for Unity bridge."
            )

        # Use default instance if no identifier provided
        if instance_identifier is None:
            if self._default_instance_id:
                instance_identifier = self._default_instance_id
                logger.debug(f"Using default instance: {instance_identifier}")
            else:
                # Use the most recently active instance
                sorted_instances = sorted(instances, key=lambda i: i.last_heartbeat or time.time(), reverse=True)
                logger.info(f"No instance specified, using most recent: {sorted_instances[0].id}")
                return sorted_instances[0]

        identifier = instance_identifier.strip()

        # Try exact ID match first
        for inst in instances:
            if inst.id == identifier:
                return inst

        # Try project name match
        name_matches = [inst for inst in instances if inst.name == identifier]
        if len(name_matches) == 1:
            return name_matches[0]
        elif len(name_matches) > 1:
            # Multiple projects with same name - return helpful error
            suggestions = [
                {
                    "id": inst.id,
                    "path": inst.path,
                    "port": inst.port,
                    "suggest": f"Use unity_instance='{inst.id}'"
                }
                for inst in name_matches
            ]
            raise ConnectionError(
                f"Project name '{identifier}' matches {len(name_matches)} instances. "
                f"Please use the full format (e.g., '{name_matches[0].id}'). "
                f"Available instances: {suggestions}"
            )

        # Try hash match
        hash_matches = [inst for inst in instances if inst.hash == identifier or inst.hash.startswith(identifier)]
        if len(hash_matches) == 1:
            return hash_matches[0]
        elif len(hash_matches) > 1:
            raise ConnectionError(
                f"Hash '{identifier}' matches multiple instances: {[inst.id for inst in hash_matches]}"
            )

        # Try composite format: Name@Hash or Name@Port
        if "@" in identifier:
            name_part, hint_part = identifier.split("@", 1)
            composite_matches = [
                inst for inst in instances
                if inst.name == name_part and (
                    inst.hash.startswith(hint_part) or str(inst.port) == hint_part
                )
            ]
            if len(composite_matches) == 1:
                return composite_matches[0]

        # Try port match (as string)
        try:
            port_num = int(identifier)
            port_matches = [inst for inst in instances if inst.port == port_num]
            if len(port_matches) == 1:
                return port_matches[0]
        except ValueError:
            pass

        # Try path match
        path_matches = [inst for inst in instances if inst.path == identifier]
        if len(path_matches) == 1:
            return path_matches[0]

        # Nothing matched
        available_ids = [inst.id for inst in instances]
        raise ConnectionError(
            f"Unity instance '{identifier}' not found. "
            f"Available instances: {available_ids}. "
            f"Use list_unity_instances() to see all instances."
        )

    def get_connection(self, instance_identifier: Optional[str] = None) -> UnityConnection:
        """
        Get or create a connection to a Unity instance.

        Args:
            instance_identifier: Optional identifier (name, hash, name@hash, etc.)
                                If None, uses default or most recent instance

        Returns:
            UnityConnection to the specified instance

        Raises:
            ConnectionError: If instance cannot be found or connected
        """
        # Refresh instance list if cache expired
        instances = self.discover_all_instances()

        # Resolve identifier to specific instance
        target = self._resolve_instance_id(instance_identifier, instances)

        # Return existing connection or create new one
        with self._pool_lock:
            if target.id not in self._connections:
                logger.info(f"Creating new connection to Unity instance: {target.id} (port {target.port})")
                conn = UnityConnection(port=target.port)
                if not conn.connect():
                    raise ConnectionError(
                        f"Failed to connect to Unity instance '{target.id}' on port {target.port}. "
                        f"Ensure the Unity Editor is running."
                    )
                self._connections[target.id] = conn
            else:
                logger.debug(f"Reusing existing connection to: {target.id}")

            return self._connections[target.id]

    def disconnect_all(self):
        """Disconnect all active connections"""
        with self._pool_lock:
            for instance_id, conn in self._connections.items():
                try:
                    logger.info(f"Disconnecting from Unity instance: {instance_id}")
                    conn.disconnect()
                except Exception as e:
                    logger.error(f"Error disconnecting from {instance_id}: {e}")
            self._connections.clear()


# Global Unity connection pool
_unity_connection_pool: Optional[UnityConnectionPool] = None
_pool_init_lock = threading.Lock()


def get_unity_connection_pool() -> UnityConnectionPool:
    """Get or create the global Unity connection pool"""
    global _unity_connection_pool

    if _unity_connection_pool is not None:
        return _unity_connection_pool

    with _pool_init_lock:
        if _unity_connection_pool is not None:
            return _unity_connection_pool

        logger.info("Initializing Unity connection pool")
        _unity_connection_pool = UnityConnectionPool()
        return _unity_connection_pool


# Backwards compatibility: keep old single-connection function
def get_unity_connection(instance_identifier: Optional[str] = None) -> UnityConnection:
    """Retrieve or establish a Unity connection.

    Args:
        instance_identifier: Optional identifier for specific Unity instance.
                           If None, uses default or most recent instance.

    Returns:
        UnityConnection to the specified or default Unity instance

    Note: This function now uses the connection pool internally.
    """
    pool = get_unity_connection_pool()
    return pool.get_connection(instance_identifier)


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


def send_command_with_retry(
    command_type: str,
    params: Dict[str, Any],
    *,
    instance_id: Optional[str] = None,
    max_retries: int | None = None,
    retry_ms: int | None = None
) -> Dict[str, Any]:
    """Send a command to a Unity instance, waiting politely through Unity reloads.

    Args:
        command_type: The command type to send
        params: Command parameters
        instance_id: Optional Unity instance identifier (name, hash, name@hash, etc.)
        max_retries: Maximum number of retries for reload states
        retry_ms: Delay between retries in milliseconds

    Returns:
        Response dictionary from Unity

    Uses config.reload_retry_ms and config.reload_max_retries by default. Preserves the
    structured failure if retries are exhausted.
    """
    conn = get_unity_connection(instance_id)
    if max_retries is None:
        max_retries = getattr(config, "reload_max_retries", 40)
    if retry_ms is None:
        retry_ms = getattr(config, "reload_retry_ms", 250)

    response = conn.send_command(command_type, params)
    retries = 0
    while _is_reloading_response(response) and retries < max_retries:
        delay_ms = int(response.get("retry_after_ms", retry_ms)
                       ) if isinstance(response, dict) else retry_ms
        time.sleep(max(0.0, delay_ms / 1000.0))
        retries += 1
        response = conn.send_command(command_type, params)
    return response


async def async_send_command_with_retry(
    command_type: str,
    params: dict[str, Any],
    *,
    instance_id: Optional[str] = None,
    loop=None,
    max_retries: int | None = None,
    retry_ms: int | None = None
) -> dict[str, Any] | MCPResponse:
    """Async wrapper that runs the blocking retry helper in a thread pool.

    Args:
        command_type: The command type to send
        params: Command parameters
        instance_id: Optional Unity instance identifier
        loop: Optional asyncio event loop
        max_retries: Maximum number of retries for reload states
        retry_ms: Delay between retries in milliseconds

    Returns:
        Response dictionary or MCPResponse on error
    """
    try:
        import asyncio  # local import to avoid mandatory asyncio dependency for sync callers
        if loop is None:
            loop = asyncio.get_running_loop()
        return await loop.run_in_executor(
            None,
            lambda: send_command_with_retry(
                command_type, params, instance_id=instance_id, max_retries=max_retries, retry_ms=retry_ms),
        )
    except Exception as e:
        return MCPResponse(success=False, error=str(e))
