"""WebSocket hub for Unity plugin communication."""

from __future__ import annotations

import asyncio
import logging
import uuid
from typing import Any, Dict, Optional

from starlette.endpoints import WebSocketEndpoint
from starlette.websockets import WebSocket

from plugin_registry import PluginRegistry

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
    _loop: Optional[asyncio.AbstractEventLoop] = None

    @classmethod
    def configure(
        cls,
        registry: PluginRegistry,
        loop: Optional[asyncio.AbstractEventLoop] = None,
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

        session_id = payload.get("session_id")
        project_name = payload.get("project_name", "Unknown Project")
        project_hash = payload.get("project_hash")
        unity_version = payload.get("unity_version", "Unknown")

        if not session_id or not project_hash:
            await websocket.close(code=4400)
            raise ValueError(
                "Plugin registration missing session_id or project_hash")

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
    async def _resolve_session_id(cls, unity_instance: Optional[str]) -> str:
        if cls._registry is None:
            raise RuntimeError("Plugin registry not configured")

        if unity_instance:
            session_id = await cls._registry.get_session_id_by_hash(unity_instance)
            if session_id:
                return session_id

        sessions = await cls._registry.list_sessions()
        if not sessions:
            raise RuntimeError("No Unity plugins are currently connected")
        # Deterministic order: rely on insertion ordering
        return next(iter(sessions.keys()))

    @classmethod
    async def send_command_for_instance(
        cls,
        unity_instance: Optional[str],
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
        unity_instance: Optional[str],
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
    unity_instance: Optional[str],
    command_type: str,
    params: Dict[str, Any],
) -> Dict[str, Any]:
    return PluginHub.send_command_blocking(unity_instance, command_type, params)
