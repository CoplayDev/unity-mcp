"""
Middleware for managing Unity instance selection per session.

This middleware intercepts all tool calls and injects the active Unity instance
into the request-scoped state, allowing tools to access it via ctx.get_state("unity_instance").
"""
from threading import RLock

from fastmcp.server.middleware import Middleware, MiddlewareContext

from transport.plugin_hub import PluginHub

# Store a global reference to the middleware instance so tools can interact
# with it to set or clear the active unity instance.
_unity_instance_middleware = None


def get_unity_instance_middleware() -> 'UnityInstanceMiddleware':
    """Get the global Unity instance middleware."""
    if _unity_instance_middleware is None:
        raise RuntimeError(
            "UnityInstanceMiddleware not initialized. Call set_unity_instance_middleware first.")
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

    def get_active_instance(self, ctx) -> str | None:
        """Retrieve the active instance for this session."""
        key = self._get_session_key(ctx)
        with self._lock:
            return self._active_by_key.get(key)

    def clear_active_instance(self, ctx) -> None:
        """Clear the stored instance for this session."""
        key = self._get_session_key(ctx)
        with self._lock:
            self._active_by_key.pop(key, None)

    async def on_call_tool(self, context: MiddlewareContext, call_next):
        """Inject active Unity instance into tool context if available."""
        ctx = context.fastmcp_context

        active_instance = self.get_active_instance(ctx)
        if active_instance:
            session_id: str | None = None
            if PluginHub.is_configured():
                try:
                    session_id = await PluginHub._resolve_session_id(active_instance)
                except Exception:
                    self.clear_active_instance(ctx)
                    return await call_next(context)

            ctx.set_state("unity_instance", active_instance)
            if session_id is not None:
                ctx.set_state("unity_session_id", session_id)
        return await call_next(context)
