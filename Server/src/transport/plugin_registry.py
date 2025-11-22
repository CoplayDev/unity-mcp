"""In-memory registry for connected Unity plugin sessions."""

from __future__ import annotations

from dataclasses import dataclass, field
from datetime import datetime, timezone

import asyncio

from models.models import ToolDefinitionModel


@dataclass(slots=True)
class PluginSession:
    """Represents a single Unity plugin connection."""

    session_id: str
    project_name: str
    project_hash: str
    unity_version: str
    registered_at: datetime
    connected_at: datetime
    tools: dict[str, ToolDefinitionModel] = field(default_factory=dict)
    project_id: str | None = None


class PluginRegistry:
    """Stores active plugin sessions in-memory.

    The registry is optimised for quick lookup by either ``session_id`` or
    ``project_hash`` (which is used as the canonical "instance id" across the
    HTTP command routing stack).
    """

    def __init__(self) -> None:
        self._sessions: dict[str, PluginSession] = {}
        self._hash_to_session: dict[str, str] = {}
        self._lock = asyncio.Lock()

    async def register(
        self,
        session_id: str,
        project_name: str,
        project_hash: str,
        unity_version: str,
    ) -> PluginSession:
        """Register (or replace) a plugin session.

        If an existing session already claims the same ``project_hash`` it will be
        replaced, ensuring that reconnect scenarios always map to the latest
        WebSocket connection.
        """

        async with self._lock:
            now = datetime.now(timezone.utc)
            session = PluginSession(
                session_id=session_id,
                project_name=project_name,
                project_hash=project_hash,
                unity_version=unity_version,
                registered_at=now,
                connected_at=now,
            )

            # Remove old mapping for this hash if it existed under a different session
            previous_session_id = self._hash_to_session.get(project_hash)
            if previous_session_id and previous_session_id != session_id:
                self._sessions.pop(previous_session_id, None)

            self._sessions[session_id] = session
            self._hash_to_session[project_hash] = session_id
            return session

    async def touch(self, session_id: str) -> None:
        """Update the ``connected_at`` timestamp when a heartbeat is received."""

        async with self._lock:
            session = self._sessions.get(session_id)
            if session:
                session.connected_at = datetime.now(timezone.utc)

    async def unregister(self, session_id: str) -> None:
        """Remove a plugin session from the registry."""

        async with self._lock:
            session = self._sessions.pop(session_id, None)
            if session and session.project_hash in self._hash_to_session:
                # Only delete the mapping if it still points at the removed session.
                mapped = self._hash_to_session.get(session.project_hash)
                if mapped == session_id:
                    del self._hash_to_session[session.project_hash]

    async def register_tools_for_session(self, session_id: str, tools: list[ToolDefinitionModel]) -> None:
        """Register tools for a specific session."""
        async with self._lock:
            session = self._sessions.get(session_id)
            if session:
                # Replace existing tools or merge? Usually replace for "set state".
                # We will replace the dict but keep the field.
                session.tools.clear()
                for tool in tools:
                    session.tools[tool.name] = tool

    async def get_session(self, session_id: str) -> PluginSession | None:
        """Fetch a session by its ``session_id``."""

        async with self._lock:
            return self._sessions.get(session_id)

    async def get_session_id_by_hash(self, project_hash: str) -> str | None:
        """Resolve a ``project_hash`` (Unity instance id) to a session id."""

        async with self._lock:
            return self._hash_to_session.get(project_hash)

    async def list_sessions(self) -> dict[str, PluginSession]:
        """Return a shallow copy of all known sessions."""

        async with self._lock:
            return dict(self._sessions)


__all__ = ["PluginRegistry", "PluginSession"]
