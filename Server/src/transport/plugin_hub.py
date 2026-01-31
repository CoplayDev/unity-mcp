"""WebSocket hub for Unity plugin communication."""

from __future__ import annotations

import asyncio
import logging
import os
import time
import uuid
from typing import Any, ClassVar

from starlette.endpoints import WebSocketEndpoint
from starlette.websockets import WebSocket

from core.config import config
from core.constants import API_KEY_HEADER
from models.models import MCPResponse
from transport.plugin_registry import PluginRegistry
from services.api_key_service import ApiKeyService
from transport.models import (
    WelcomeMessage,
    RegisteredMessage,
    ExecuteCommandMessage,
    PingMessage,
    RegisterMessage,
    RegisterToolsMessage,
    PongMessage,
    CommandResultMessage,
    SessionList,
    SessionDetails,
)

logger = logging.getLogger(__name__)


class PluginDisconnectedError(RuntimeError):
    """Raised when a plugin WebSocket disconnects while commands are in flight."""


class NoUnitySessionError(RuntimeError):
    """Raised when no Unity plugins are available."""


class InstanceSelectionRequiredError(RuntimeError):
    """Raised when the caller must explicitly select a Unity instance."""

    _SELECTION_REQUIRED = (
        "Unity instance selection is required. "
        "Call set_active_instance with Name@hash from mcpforunity://instances."
    )
    _MULTIPLE_INSTANCES = (
        "Multiple Unity instances are connected. "
        "Call set_active_instance with Name@hash from mcpforunity://instances."
    )

    def __init__(self, message: str | None = None):
        super().__init__(message or self._SELECTION_REQUIRED)


class PluginHub(WebSocketEndpoint):
    """Manages persistent WebSocket connections to Unity plugins."""

    encoding = "json"
    KEEP_ALIVE_INTERVAL = 15
    SERVER_TIMEOUT = 30
    COMMAND_TIMEOUT = 30
    # Server-side ping interval (seconds) - how often to send pings to Unity
    PING_INTERVAL = 10
    # Max time (seconds) to wait for pong before considering connection dead
    PING_TIMEOUT = 20
    # Timeout (seconds) for fast-fail commands like ping/read_console/get_editor_state.
    # Keep short so MCP clients aren't blocked during Unity compilation/reload/unfocused throttling.
    FAST_FAIL_TIMEOUT = 2.0
    # Fast-path commands should never block the client for long; return a retry hint instead.
    # This helps avoid the Cursor-side ~30s tool-call timeout when Unity is compiling/reloading
    # or is throttled while unfocused.
    _FAST_FAIL_COMMANDS: set[str] = {
        "read_console", "get_editor_state", "ping"}

    _registry: PluginRegistry | None = None
    _connections: dict[str, WebSocket] = {}
    # command_id -> {"future": Future, "session_id": str}
    _pending: dict[str, dict[str, Any]] = {}
    _lock: asyncio.Lock | None = None
    _loop: asyncio.AbstractEventLoop | None = None
    # session_id -> last pong timestamp (monotonic)
    _last_pong: ClassVar[dict[str, float]] = {}
    # session_id -> ping task
    _ping_tasks: ClassVar[dict[str, asyncio.Task]] = {}

    @classmethod
    def configure(
        cls,
        registry: PluginRegistry,
        loop: asyncio.AbstractEventLoop | None = None,
    ) -> None:
        cls._registry = registry
        cls._loop = loop or asyncio.get_running_loop()
        # Ensure coordination primitives are bound to the configured loop
        cls._lock = asyncio.Lock()

    @classmethod
    def is_configured(cls) -> bool:
        return cls._registry is not None and cls._lock is not None

    async def on_connect(self, websocket: WebSocket) -> None:
        # Validate API key in remote-hosted mode (fail closed)
        if config.http_remote_hosted:
            if not ApiKeyService.is_initialized():
                logger.debug(
                    "WebSocket connection rejected: auth service not initialized")
                await websocket.close(code=1013, reason="Try again later")
                return

            api_key = websocket.headers.get(API_KEY_HEADER)

            if not api_key:
                logger.debug("WebSocket connection rejected: API key required")
                await websocket.close(code=4401, reason="API key required")
                return

            service = ApiKeyService.get_instance()
            result = await service.validate(api_key)

            if not result.valid:
                # Transient auth failures are retryable (1013)
                if result.error and any(
                    indicator in result.error.lower()
                    for indicator in ("unavailable", "timeout", "service error")
                ):
                    logger.debug(
                        "WebSocket connection rejected: auth service unavailable")
                    await websocket.close(code=1013, reason="Try again later")
                    return

                logger.debug("WebSocket connection rejected: invalid API key")
                await websocket.close(code=4403, reason="Invalid API key")
                return

            # Both valid and user_id must be present to accept
            if not result.user_id:
                logger.debug(
                    "WebSocket connection rejected: validated key missing user_id")
                await websocket.close(code=4403, reason="Invalid API key")
                return

            # Store user_id in websocket state for later use during registration
            websocket.state.user_id = result.user_id
            websocket.state.api_key_metadata = result.metadata

        await websocket.accept()
        msg = WelcomeMessage(
            serverTimeout=self.SERVER_TIMEOUT,
            keepAliveInterval=self.KEEP_ALIVE_INTERVAL,
        )
        await websocket.send_json(msg.model_dump())

    async def on_receive(self, websocket: WebSocket, data: Any) -> None:
        if not isinstance(data, dict):
            logger.warning(f"Received non-object payload from plugin: {data}")
            return

        message_type = data.get("type")
        try:
            if message_type == "register":
                await self._handle_register(websocket, RegisterMessage(**data))
            elif message_type == "register_tools":
                await self._handle_register_tools(websocket, RegisterToolsMessage(**data))
            elif message_type == "pong":
                await self._handle_pong(PongMessage(**data))
            elif message_type == "command_result":
                await self._handle_command_result(CommandResultMessage(**data))
            else:
                logger.debug(f"Ignoring plugin message: {data}")
        except Exception as e:
            logger.error(f"Error handling message type {message_type}: {e}")

    async def on_disconnect(self, websocket: WebSocket, close_code: int) -> None:
        cls = type(self)
        lock = cls._lock
        if lock is None:
            return
        async with lock:
            session_id = next(
                (sid for sid, ws in cls._connections.items() if ws is websocket), None)
            if session_id:
                cls._connections.pop(session_id, None)
                # Stop the ping loop for this session
                ping_task = cls._ping_tasks.pop(session_id, None)
                if ping_task and not ping_task.done():
                    ping_task.cancel()
                # Clean up last pong tracking
                cls._last_pong.pop(session_id, None)
                # Fail-fast any in-flight commands for this session to avoid waiting for COMMAND_TIMEOUT.
                pending_ids = [
                    command_id
                    for command_id, entry in cls._pending.items()
                    if entry.get("session_id") == session_id
                ]
                if pending_ids:
                    logger.debug(f"Cancelling {len(pending_ids)} pending commands for disconnected session")
                for command_id in pending_ids:
                    entry = cls._pending.get(command_id)
                    future = entry.get("future") if isinstance(
                        entry, dict) else None
                    if future and not future.done():
                        future.set_exception(
                            PluginDisconnectedError(
                                f"Unity plugin session {session_id} disconnected while awaiting command_result"
                            )
                        )
                if cls._registry:
                    await cls._registry.unregister(session_id)
                logger.info(
                    f"Plugin session {session_id} disconnected ({close_code})")

    # ------------------------------------------------------------------
    # Public API
    # ------------------------------------------------------------------
    @classmethod
    async def send_command(cls, session_id: str, command_type: str, params: dict[str, Any]) -> dict[str, Any]:
        websocket = await cls._get_connection(session_id)
        command_id = str(uuid.uuid4())
        future: asyncio.Future = asyncio.get_running_loop().create_future()
        # Compute a per-command timeout:
        # - fast-path commands: short timeout (encourage retry)
        # - long-running commands: allow caller to request a longer timeout via params
        unity_timeout_s = float(cls.COMMAND_TIMEOUT)
        server_wait_s = float(cls.COMMAND_TIMEOUT)
        if command_type in cls._FAST_FAIL_COMMANDS:
            fast_timeout = float(cls.FAST_FAIL_TIMEOUT)
            unity_timeout_s = fast_timeout
            server_wait_s = fast_timeout
        else:
            # Common tools pass a requested timeout in seconds (e.g., timeout_seconds=900).
            requested = None
            try:
                if isinstance(params, dict):
                    requested = params.get("timeout_seconds", None)
                    if requested is None:
                        requested = params.get("timeoutSeconds", None)
            except Exception:
                requested = None

            if requested is not None:
                try:
                    requested_s = float(requested)
                    # Clamp to a sane upper bound to avoid accidental infinite hangs.
                    requested_s = max(1.0, min(requested_s, 60.0 * 60.0))
                    unity_timeout_s = max(unity_timeout_s, requested_s)
                    # Give the server a small cushion beyond the Unity-side timeout to account for transport overhead.
                    server_wait_s = max(server_wait_s, requested_s + 5.0)
                except Exception:
                    pass

        lock = cls._lock
        if lock is None:
            raise RuntimeError("PluginHub not configured")

        async with lock:
            if command_id in cls._pending:
                raise RuntimeError(
                    f"Duplicate command id generated: {command_id}")
            cls._pending[command_id] = {
                "future": future, "session_id": session_id}

        try:
            msg = ExecuteCommandMessage(
                id=command_id,
                name=command_type,
                params=params,
                timeout=unity_timeout_s,
            )
            try:
                await websocket.send_json(msg.model_dump())
            except Exception as exc:
                # If send fails (socket already closing), fail the future so callers don't hang.
                if not future.done():
                    future.set_exception(exc)
                raise
            try:
                result = await asyncio.wait_for(future, timeout=server_wait_s)
                return result
            except PluginDisconnectedError as exc:
                return MCPResponse(success=False, error=str(exc), hint="retry").model_dump()
            except asyncio.TimeoutError:
                if command_type in cls._FAST_FAIL_COMMANDS:
                    return MCPResponse(
                        success=False,
                        error=f"Unity did not respond to '{command_type}' within {server_wait_s:.1f}s; please retry",
                        hint="retry",
                    ).model_dump()
                raise
        finally:
            async with lock:
                cls._pending.pop(command_id, None)

    @classmethod
    async def get_sessions(cls, user_id: str | None = None) -> SessionList:
        """Get all active plugin sessions.

        Args:
            user_id: If provided (remote-hosted mode), only return sessions for this user.
        """
        if cls._registry is None:
            return SessionList(sessions={})
        sessions = await cls._registry.list_sessions(user_id=user_id)
        return SessionList(
            sessions={
                session_id: SessionDetails(
                    project=session.project_name,
                    hash=session.project_hash,
                    unity_version=session.unity_version,
                    connected_at=session.connected_at.isoformat(),
                )
                for session_id, session in sessions.items()
            }
        )

    @classmethod
    async def get_tools_for_project(cls, project_hash: str) -> list[Any]:
        """Retrieve tools registered for a active project hash."""
        if cls._registry is None:
            return []

        session_id = await cls._registry.get_session_id_by_hash(project_hash)
        if not session_id:
            return []

        session = await cls._registry.get_session(session_id)
        if not session:
            return []

        return list(session.tools.values())

    @classmethod
    async def get_tool_definition(cls, project_hash: str, tool_name: str) -> Any | None:
        """Retrieve a specific tool definition for an active project hash."""
        if cls._registry is None:
            return None

        session_id = await cls._registry.get_session_id_by_hash(project_hash)
        if not session_id:
            return None

        session = await cls._registry.get_session(session_id)
        if not session:
            return None

        return session.tools.get(tool_name)

    # ------------------------------------------------------------------
    # Internal helpers
    # ------------------------------------------------------------------
    async def _handle_register(self, websocket: WebSocket, payload: RegisterMessage) -> None:
        cls = type(self)
        registry = cls._registry
        lock = cls._lock
        if registry is None or lock is None:
            await websocket.close(code=1011)
            raise RuntimeError("PluginHub not configured")

        project_name = payload.project_name
        project_hash = payload.project_hash
        unity_version = payload.unity_version
        project_path = payload.project_path

        if not project_hash:
            await websocket.close(code=4400)
            raise ValueError(
                "Plugin registration missing project_hash")

        # Get user_id from websocket state (set during API key validation)
        user_id = getattr(websocket.state, "user_id", None)

        session_id = str(uuid.uuid4())
        # Inform the plugin of its assigned session ID
        response = RegisteredMessage(session_id=session_id)
        await websocket.send_json(response.model_dump())

        session = await registry.register(session_id, project_name, project_hash, unity_version, project_path, user_id=user_id)
        async with lock:
            cls._connections[session.session_id] = websocket
            # Initialize last pong time and start ping loop for this session
            cls._last_pong[session_id] = time.monotonic()
            # Cancel any existing ping task for this session (shouldn't happen, but be safe)
            old_task = cls._ping_tasks.pop(session_id, None)
            if old_task and not old_task.done():
                old_task.cancel()
            # Start the server-side ping loop
            ping_task = asyncio.create_task(cls._ping_loop(session_id, websocket))
            cls._ping_tasks[session_id] = ping_task

        if user_id:
            logger.info(f"Plugin registered: {project_name} ({project_hash}) for user {user_id}")
        else:
            logger.info(f"Plugin registered: {project_name} ({project_hash})")

    async def _handle_register_tools(self, websocket: WebSocket, payload: RegisterToolsMessage) -> None:
        cls = type(self)
        registry = cls._registry
        lock = cls._lock
        if registry is None or lock is None:
            return

        # Find session_id for this websocket
        async with lock:
            session_id = next(
                (sid for sid, ws in cls._connections.items() if ws is websocket), None)

        if not session_id:
            logger.warning("Received register_tools from unknown connection")
            return

        await registry.register_tools_for_session(session_id, payload.tools)
        logger.info(
            f"Registered {len(payload.tools)} tools for session {session_id}")

        try:
            from services.custom_tool_service import CustomToolService

            service = CustomToolService.get_instance()
            service.register_global_tools(payload.tools)
        except RuntimeError as exc:
            logger.debug(
                "Skipping global custom tool registration: CustomToolService not initialized yet (%s)",
                exc,
            )
        except Exception as exc:
            logger.warning(
                "Unexpected error during global custom tool registration; "
                "custom tools may not be available globally",
                exc_info=exc,
            )

    async def _handle_command_result(self, payload: CommandResultMessage) -> None:
        cls = type(self)
        lock = cls._lock
        if lock is None:
            return
        command_id = payload.id
        result = payload.result

        if not command_id:
            logger.warning(f"Command result missing id: {payload}")
            return

        async with lock:
            entry = cls._pending.get(command_id)
        future = entry.get("future") if isinstance(entry, dict) else None
        if future and not future.done():
            future.set_result(result)

    async def _handle_pong(self, payload: PongMessage) -> None:
        cls = type(self)
        registry = cls._registry
        lock = cls._lock
        if registry is None:
            return
        session_id = payload.session_id
        if session_id:
            await registry.touch(session_id)
            # Record last pong time for staleness detection (under lock for consistency)
            if lock is not None:
                async with lock:
                    cls._last_pong[session_id] = time.monotonic()

    @classmethod
    async def _ping_loop(cls, session_id: str, websocket: WebSocket) -> None:
        """Server-initiated ping loop to detect dead connections.

        Sends periodic pings to the Unity client. If no pong is received within
        PING_TIMEOUT seconds, the connection is considered dead and closed.
        This helps detect connections that die silently (e.g., Windows OSError 64).
        """
        logger.debug(f"[Ping] Starting ping loop for session {session_id}")
        try:
            while True:
                await asyncio.sleep(cls.PING_INTERVAL)

                # Check if we're still supposed to be running and get last pong time (under lock)
                lock = cls._lock
                if lock is None:
                    break
                async with lock:
                    if session_id not in cls._connections:
                        logger.debug(f"[Ping] Session {session_id} no longer in connections, stopping ping loop")
                        break
                    # Read last pong time under lock for consistency
                    last_pong = cls._last_pong.get(session_id, 0)

                # Check staleness: has it been too long since we got a pong?
                elapsed = time.monotonic() - last_pong
                if elapsed > cls.PING_TIMEOUT:
                    logger.warning(
                        f"[Ping] Session {session_id} stale: no pong for {elapsed:.1f}s "
                        f"(timeout={cls.PING_TIMEOUT}s). Closing connection."
                    )
                    try:
                        await websocket.close(code=1001)  # Going away
                    except Exception as close_ex:
                        logger.debug(f"[Ping] Error closing stale websocket: {close_ex}")
                    break

                # Send a ping to the client
                try:
                    ping_msg = PingMessage()
                    await websocket.send_json(ping_msg.model_dump())
                    logger.debug(f"[Ping] Sent ping to session {session_id}")
                except Exception as send_ex:
                    # Send failed - connection is dead
                    logger.warning(
                        f"[Ping] Failed to send ping to session {session_id}: {send_ex}. "
                        "Connection likely dead."
                    )
                    try:
                        await websocket.close(code=1006)  # Abnormal closure
                    except Exception:
                        pass
                    break

        except asyncio.CancelledError:
            logger.debug(f"[Ping] Ping loop cancelled for session {session_id}")
        except Exception as ex:
            logger.warning(f"[Ping] Ping loop error for session {session_id}: {ex}")
        finally:
            logger.debug(f"[Ping] Ping loop ended for session {session_id}")

    @classmethod
    async def _get_connection(cls, session_id: str) -> WebSocket:
        lock = cls._lock
        if lock is None:
            raise RuntimeError("PluginHub not configured")
        async with lock:
            websocket = cls._connections.get(session_id)
        if websocket is None:
            raise RuntimeError(f"Plugin session {session_id} not connected")
        return websocket

    # ------------------------------------------------------------------
    # Session resolution helpers
    # ------------------------------------------------------------------
    @classmethod
    async def _resolve_session_id(cls, unity_instance: str | None, user_id: str | None = None) -> str:
        """Resolve a project hash (Unity instance id) to an active plugin session.

        During Unity domain reloads the plugin's WebSocket session is torn down
        and reconnected shortly afterwards. Instead of failing immediately when
        no sessions are available, we wait for a bounded period for a plugin
        to reconnect so in-flight MCP calls can succeed transparently.

        Args:
            unity_instance: Target instance (Name@hash or hash)
            user_id: User ID from API key validation (for remote-hosted mode session isolation)
        """
        if cls._registry is None:
            raise RuntimeError("Plugin registry not configured")

        # Bound waiting for Unity sessions. Default to 20s to handle domain reloads
        # (which can take 10-20s after test runs or script changes).
        #
        # NOTE: This wait can impact agentic workflows where domain reloads happen
        # frequently (e.g., after test runs, script compilation). The 20s default
        # balances handling slow reloads vs. avoiding unnecessary delays.
        #
        # TODO: Make this more deterministic by detecting Unity's actual reload state
        # (e.g., via status file, heartbeat, or explicit "reloading" signal from Unity)
        # rather than blindly waiting up to 20s. See Issue #657.
        #
        # Configurable via: UNITY_MCP_SESSION_RESOLVE_MAX_WAIT_S (default: 20.0, max: 20.0)
        try:
            max_wait_s = float(
                os.environ.get("UNITY_MCP_SESSION_RESOLVE_MAX_WAIT_S", "20.0"))
        except ValueError as e:
            raw_val = os.environ.get(
                "UNITY_MCP_SESSION_RESOLVE_MAX_WAIT_S", "20.0")
            logger.warning(
                "Invalid UNITY_MCP_SESSION_RESOLVE_MAX_WAIT_S=%r, using default 20.0: %s",
                raw_val, e)
            max_wait_s = 20.0
        # Clamp to [0, 20] to prevent misconfiguration from causing excessive waits
        max_wait_s = max(0.0, min(max_wait_s, 20.0))
        retry_ms = float(getattr(config, "reload_retry_ms", 250))
        sleep_seconds = max(0.05, min(0.25, retry_ms / 1000.0))

        # Allow callers to provide either just the hash or Name@hash
        target_hash: str | None = None
        if unity_instance:
            if "@" in unity_instance:
                _, _, suffix = unity_instance.rpartition("@")
                target_hash = suffix or None
            else:
                target_hash = unity_instance

        async def _try_once() -> tuple[str | None, int, bool]:
            explicit_required = config.http_remote_hosted
            # Prefer a specific Unity instance if one was requested
            if target_hash:
                # In remote-hosted mode with user_id, use user-scoped lookup
                if config.http_remote_hosted and user_id:
                    session_id = await cls._registry.get_session_id_by_hash(target_hash, user_id)
                    sessions = await cls._registry.list_sessions(user_id=user_id)
                else:
                    session_id = await cls._registry.get_session_id_by_hash(target_hash)
                    sessions = await cls._registry.list_sessions(user_id=user_id)
                return session_id, len(sessions), explicit_required

            # No target provided: determine if we can auto-select
            # In remote-hosted mode, filter sessions by user_id
            sessions = await cls._registry.list_sessions(user_id=user_id)
            count = len(sessions)
            if count == 0:
                return None, count, explicit_required
            if explicit_required:
                return None, count, explicit_required
            if count == 1:
                return next(iter(sessions.keys())), count, explicit_required
            # Multiple sessions but no explicit target is ambiguous
            return None, count, explicit_required

        session_id, session_count, explicit_required = await _try_once()
        if session_id is None and explicit_required and not target_hash and session_count > 0:
            raise InstanceSelectionRequiredError()
        deadline = time.monotonic() + max_wait_s
        wait_started = None

        # If there is no active plugin yet (e.g., Unity starting up or reloading),
        # wait politely for a session to appear before surfacing an error.
        while session_id is None and time.monotonic() < deadline:
            if not target_hash and session_count > 1:
                raise InstanceSelectionRequiredError(
                    InstanceSelectionRequiredError._MULTIPLE_INSTANCES)
            if session_id is None and explicit_required and not target_hash and session_count > 0:
                raise InstanceSelectionRequiredError()
            if wait_started is None:
                wait_started = time.monotonic()
                logger.debug(
                    "No plugin session available (instance=%s); waiting up to %.2fs",
                    unity_instance or "default",
                    max_wait_s,
                )
            await asyncio.sleep(sleep_seconds)
            session_id, session_count, explicit_required = await _try_once()

        if session_id is not None and wait_started is not None:
            logger.debug(
                "Plugin session restored after %.3fs (instance=%s)",
                time.monotonic() - wait_started,
                unity_instance or "default",
            )
        if session_id is None and not target_hash and session_count > 1:
            raise InstanceSelectionRequiredError(
                InstanceSelectionRequiredError._MULTIPLE_INSTANCES)

        if session_id is None and explicit_required and not target_hash and session_count > 0:
            raise InstanceSelectionRequiredError()

        if session_id is None:
            logger.warning(
                "No Unity plugin reconnected within %.2fs (instance=%s)",
                max_wait_s,
                unity_instance or "default",
            )
            # At this point we've given the plugin ample time to reconnect; surface
            # a clear error so the client can prompt the user to open Unity.
            raise NoUnitySessionError(
                "No Unity plugins are currently connected")

        return session_id

    @classmethod
    async def send_command_for_instance(
        cls,
        unity_instance: str | None,
        command_type: str,
        params: dict[str, Any],
        user_id: str | None = None,
    ) -> dict[str, Any]:
        """Send a command to a Unity instance.

        Args:
            unity_instance: Target instance (Name@hash or hash)
            command_type: Command type to execute
            params: Command parameters
            user_id: User ID for session isolation in remote-hosted mode
        """
        try:
            session_id = await cls._resolve_session_id(unity_instance, user_id=user_id)
        except NoUnitySessionError:
            logger.debug(
                "Unity session unavailable; returning retry: command=%s instance=%s",
                command_type,
                unity_instance or "default",
            )
            return MCPResponse(
                success=False,
                error="Unity session not available; please retry",
                hint="retry",
                data={"reason": "no_unity_session", "retry_after_ms": 250},
            ).model_dump()

        # During domain reload / immediate reconnect windows, the plugin may be connected but not yet
        # ready to process execute commands on the Unity main thread (which can be further delayed when
        # the Unity Editor is unfocused). For fast-path commands, we do a bounded readiness probe using
        # a main-thread ping command (handled by TransportCommandDispatcher) rather than waiting on
        # register_tools (which can be delayed by EditorApplication.delayCall).
        if command_type in cls._FAST_FAIL_COMMANDS and command_type != "ping":
            try:
                max_wait_s = float(os.environ.get(
                    "UNITY_MCP_SESSION_READY_WAIT_SECONDS", "6"))
            except ValueError as e:
                raw_val = os.environ.get(
                    "UNITY_MCP_SESSION_READY_WAIT_SECONDS", "6")
                logger.warning(
                    "Invalid UNITY_MCP_SESSION_READY_WAIT_SECONDS=%r, using default 6.0: %s",
                    raw_val, e)
                max_wait_s = 6.0
            max_wait_s = max(0.0, min(max_wait_s, 20.0))
            if max_wait_s > 0:
                deadline = time.monotonic() + max_wait_s
                while time.monotonic() < deadline:
                    try:
                        probe = await cls.send_command(session_id, "ping", {})
                    except Exception:
                        probe = None

                    # The Unity-side dispatcher responds with {status:"success", result:{message:"pong"}}
                    if isinstance(probe, dict) and probe.get("status") == "success":
                        result = probe.get("result") if isinstance(
                            probe.get("result"), dict) else {}
                        if result.get("message") == "pong":
                            break
                    await asyncio.sleep(0.1)
                else:
                    # Not ready within the bounded window: return retry hint without sending.
                    return MCPResponse(
                        success=False,
                        error=f"Unity session not ready for '{command_type}' (ping not answered); please retry",
                        hint="retry",
                    ).model_dump()

        return await cls.send_command(session_id, command_type, params)

    # ------------------------------------------------------------------
    # Blocking helpers for synchronous tool code
    # ------------------------------------------------------------------
    @classmethod
    def _run_coroutine_sync(cls, coro: "asyncio.Future[Any]") -> Any:
        if cls._loop is None:
            raise RuntimeError("PluginHub event loop not configured")
        loop = cls._loop
        if loop.is_running():
            try:
                running_loop = asyncio.get_running_loop()
            except RuntimeError:
                running_loop = None
            else:
                if running_loop is loop:
                    raise RuntimeError(
                        "Cannot wait synchronously for PluginHub coroutine from within the event loop"
                    )
        future = asyncio.run_coroutine_threadsafe(coro, loop)
        return future.result()

    @classmethod
    def send_command_blocking(
        cls,
        unity_instance: str | None,
        command_type: str,
        params: dict[str, Any],
    ) -> dict[str, Any]:
        return cls._run_coroutine_sync(
            cls.send_command_for_instance(unity_instance, command_type, params)
        )

    @classmethod
    def list_sessions_sync(cls) -> SessionList:
        return cls._run_coroutine_sync(cls.get_sessions())


def send_command_to_plugin(
    *,
    unity_instance: str | None,
    command_type: str,
    params: dict[str, Any],
) -> dict[str, Any]:
    return PluginHub.send_command_blocking(unity_instance, command_type, params)
