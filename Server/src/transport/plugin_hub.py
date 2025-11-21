"""WebSocket hub for Unity plugin communication."""

from __future__ import annotations

import asyncio
import logging
import time
import uuid
from typing import Any, Dict

from starlette.endpoints import WebSocketEndpoint
from starlette.websockets import WebSocket

from core.config import config
from transport.plugin_registry import PluginRegistry

logger = logging.getLogger("mcp-for-unity-server")


class PluginHub(WebSocketEndpoint):
    """Manages persistent WebSocket connections to Unity plugins."""

    encoding = "json"
    KEEP_ALIVE_INTERVAL = 15
    SERVER_TIMEOUT = 30
    COMMAND_TIMEOUT = 30

    _registry: PluginRegistry | None = None
    _connections: Dict[str, WebSocket] = {}
    _pending: Dict[str, asyncio.Future] = {}
    _lock: asyncio.Lock | None = None
    _loop: asyncio.AbstractEventLoop | None = None

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
        await websocket.accept()
        await websocket.send_json(
            {
                "type": "welcome",
                "serverTimeout": self.SERVER_TIMEOUT,
                "keepAliveInterval": self.KEEP_ALIVE_INTERVAL,
            }
        )

    async def on_receive(self, websocket: WebSocket, data: Any) -> None:
        if not isinstance(data, dict):
            logger.warning("Received non-object payload from plugin: %r", data)
            return

        message_type = data.get("type")
        if message_type == "register":
            await self._handle_register(websocket, data)
        elif message_type == "pong":
            await self._handle_pong(data)
        elif message_type == "command_result":
            await self._handle_command_result(data)
        else:
            logger.debug("Ignoring plugin message: %s", data)

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
                if cls._registry:
                    await cls._registry.unregister(session_id)
                logger.info("Plugin session %s disconnected (%s)",
                            session_id, close_code)

    # ------------------------------------------------------------------
    # Public API
    # ------------------------------------------------------------------
    @classmethod
    async def send_command(cls, session_id: str, command_type: str, params: Dict[str, Any]) -> Dict[str, Any]:
        websocket = await cls._get_connection(session_id)
        command_id = str(uuid.uuid4())
        future: asyncio.Future = asyncio.get_running_loop().create_future()

        lock = cls._lock
        if lock is None:
            raise RuntimeError("PluginHub not configured")

        async with lock:
            if command_id in cls._pending:
                raise RuntimeError(
                    f"Duplicate command id generated: {command_id}")
            cls._pending[command_id] = future

        try:
            await websocket.send_json(
                {
                    "type": "execute",
                    "id": command_id,
                    "name": command_type,
                    "params": params,
                    "timeout": cls.COMMAND_TIMEOUT,
                }
            )
            result = await asyncio.wait_for(future, timeout=cls.COMMAND_TIMEOUT)
            return result
        finally:
            async with lock:
                cls._pending.pop(command_id, None)

    @classmethod
    async def get_sessions(cls) -> Dict[str, Any]:
        if cls._registry is None:
            return {"sessions": {}}
        sessions = await cls._registry.list_sessions()
        return {
            "sessions": {
                session_id: {
                    "project": session.project_name,
                    "hash": session.project_hash,
                    "unity_version": session.unity_version,
                    "connected_at": session.connected_at.isoformat(),
                }
                for session_id, session in sessions.items()
            }
        }

    # ------------------------------------------------------------------
    # Internal helpers
    # ------------------------------------------------------------------
    async def _handle_register(self, websocket: WebSocket, payload: Dict[str, Any]) -> None:
        cls = type(self)
        registry = cls._registry
        lock = cls._lock
        if registry is None or lock is None:
            await websocket.close(code=1011)
            raise RuntimeError("PluginHub not configured")

        project_name = payload.get("project_name", "Unknown Project")
        project_hash = payload.get("project_hash")
        unity_version = payload.get("unity_version", "Unknown")

        if not project_hash:
            await websocket.close(code=4400)
            raise ValueError(
                "Plugin registration missing project_hash")

        session_id = str(uuid.uuid4())
        # Inform the plugin of its assigned session ID
        await websocket.send_json({
            "type": "registered",
            "session_id": session_id
        })

        session = await registry.register(session_id, project_name, project_hash, unity_version)
        async with lock:
            cls._connections[session.session_id] = websocket
        logger.info("Plugin registered: %s (%s)", project_name, project_hash)

    async def _handle_command_result(self, payload: Dict[str, Any]) -> None:
        cls = type(self)
        lock = cls._lock
        if lock is None:
            return
        command_id = payload.get("id")
        result = payload.get("result", {})

        if not command_id:
            logger.warning("Command result missing id: %s", payload)
            return

        async with lock:
            future = cls._pending.get(command_id)
        if future and not future.done():
            future.set_result(result)

    async def _handle_pong(self, payload: Dict[str, Any]) -> None:
        cls = type(self)
        registry = cls._registry
        if registry is None:
            return
        session_id = payload.get("session_id")
        if session_id:
            await registry.touch(session_id)

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
    async def _resolve_session_id(cls, unity_instance: str | None) -> str:
        """Resolve a project hash (Unity instance id) to an active plugin session.

        During Unity domain reloads the plugin's WebSocket session is torn down
        and reconnected shortly afterwards. Instead of failing immediately when
        no sessions are available, we wait for a bounded period for a plugin
        to reconnect so in-flight MCP calls can succeed transparently.
        """
        if cls._registry is None:
            raise RuntimeError("Plugin registry not configured")

        # Use the same defaults as the stdio transport reload handling so that
        # HTTP/WebSocket and TCP behave consistently without per-project env.
        max_retries = max(1, int(getattr(config, "reload_max_retries", 40)))
        retry_ms = float(getattr(config, "reload_retry_ms", 250))
        sleep_seconds = max(0.05, retry_ms / 1000.0)

        # Allow callers to provide either just the hash or Name@hash
        target_hash: str | None = None
        if unity_instance:
            if "@" in unity_instance:
                _, _, suffix = unity_instance.rpartition("@")
                target_hash = suffix or None
            else:
                target_hash = unity_instance

        async def _try_once() -> tuple[str | None, int]:
            # Prefer a specific Unity instance if one was requested
            if target_hash:
                session_id = await cls._registry.get_session_id_by_hash(target_hash)
                sessions = await cls._registry.list_sessions()
                return session_id, len(sessions)

            # No target provided: determine if we can auto-select
            sessions = await cls._registry.list_sessions()
            count = len(sessions)
            if count == 0:
                return None, count
            if count == 1:
                return next(iter(sessions.keys())), count
            # Multiple sessions but no explicit target is ambiguous
            return None, count

        session_id, session_count = await _try_once()
        deadline = time.monotonic() + (max_retries * sleep_seconds)
        wait_started = None

        # If there is no active plugin yet (e.g., Unity starting up or reloading),
        # wait politely for a session to appear before surfacing an error.
        while session_id is None and time.monotonic() < deadline:
            if not target_hash and session_count > 1:
                raise RuntimeError(
                    "Multiple Unity instances are connected. "
                    "Call set_active_instance with Name@hash from unity://instances."
                )
            if wait_started is None:
                wait_started = time.monotonic()
                logger.debug(
                    "No plugin session available (instance=%s); waiting up to %.2fs",
                    unity_instance or "default",
                    deadline - wait_started,
                )
            await asyncio.sleep(sleep_seconds)
            session_id, session_count = await _try_once()

        if session_id is not None and wait_started is not None:
            logger.debug(
                "Plugin session restored after %.3fs (instance=%s)",
                time.monotonic() - wait_started,
                unity_instance or "default",
            )
        if session_id is None and not target_hash and session_count > 1:
            raise RuntimeError(
                "Multiple Unity instances are connected. "
                "Call set_active_instance with Name@hash from unity://instances."
            )

        if session_id is None:
            logger.warning(
                "No Unity plugin reconnected within %.2fs (instance=%s)",
                max_retries * sleep_seconds,
                unity_instance or "default",
            )
            # At this point we've given the plugin ample time to reconnect; surface
            # a clear error so the client can prompt the user to open Unity.
            raise RuntimeError("No Unity plugins are currently connected")

        return session_id

    @classmethod
    async def send_command_for_instance(
        cls,
        unity_instance: str | None,
        command_type: str,
        params: Dict[str, Any],
    ) -> Dict[str, Any]:
        session_id = await cls._resolve_session_id(unity_instance)
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
        params: Dict[str, Any],
    ) -> Dict[str, Any]:
        return cls._run_coroutine_sync(
            cls.send_command_for_instance(unity_instance, command_type, params)
        )

    @classmethod
    def list_sessions_sync(cls) -> Dict[str, Any]:
        return cls._run_coroutine_sync(cls.get_sessions())


def send_command_to_plugin(
    *,
    unity_instance: str | None,
    command_type: str,
    params: Dict[str, Any],
) -> Dict[str, Any]:
    return PluginHub.send_command_blocking(unity_instance, command_type, params)
