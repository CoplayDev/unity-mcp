"""
Middleware for managing Unity instance selection per session.

This middleware intercepts all tool calls and injects the active Unity instance
into the request-scoped state, allowing tools to access it via ctx.get_state("unity_instance").
"""
from threading import RLock
from typing import Optional

from fastmcp.server.middleware import Middleware, MiddlewareContext

# Global instance for access from tools
_unity_instance_middleware: Optional['UnityInstanceMiddleware'] = None


def get_unity_instance_middleware() -> 'UnityInstanceMiddleware':
    """Get the global Unity instance middleware."""
    if _unity_instance_middleware is None:
        raise RuntimeError("UnityInstanceMiddleware not initialized. Call set_unity_instance_middleware first.")
    return _unity_instance_middleware


def set_unity_instance_middleware(middleware: 'UnityInstanceMiddleware') -> None:
    """Set the global Unity instance middleware (called during server initialization)."""
    global _unity_instance_middleware
    _unity_instance_middleware = middleware


class UnityInstanceMiddleware(Middleware):
    """
    Middleware that manages per-session Unity instance selection.

    Stores active instance per session_id and injects it into request state
    for all tool calls.
    """

    def __init__(self):
        super().__init__()
        self._active_by_key: dict[str, str] = {}
        self._lock = RLock()

    def _get_session_key(self, ctx) -> str:
        """
        Derive a stable key for the calling session.

        Uses ctx.session_id if available, falls back to 'global'.
        """
        session_id = getattr(ctx, "session_id", None)
        if isinstance(session_id, str) and session_id:
            return session_id

        client_id = getattr(ctx, "client_id", None)
        if isinstance(client_id, str) and client_id:
            return client_id

        return "global"

    def set_active_instance(self, ctx, instance_id: str) -> None:
        """Store the active instance for this session."""
        key = self._get_session_key(ctx)
        with self._lock:
            self._active_by_key[key] = instance_id

    def get_active_instance(self, ctx) -> Optional[str]:
        """Retrieve the active instance for this session."""
        key = self._get_session_key(ctx)
        with self._lock:
            return self._active_by_key.get(key)

    async def on_call_tool(self, context: MiddlewareContext, call_next):
        """
        Intercept tool calls and inject the active Unity instance into request state.
        """
        # Get the FastMCP context
        ctx = context.fastmcp_context

        # Look up the active instance for this session
        active_instance = self.get_active_instance(ctx)

        # Inject into request-scoped state (accessible via ctx.get_state)
        if active_instance is not None:
            ctx.set_state("unity_instance", active_instance)

        # Continue with tool execution
        return await call_next(context)
