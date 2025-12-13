"""
Middleware for managing Unity instance selection per session.

This middleware intercepts all tool calls and injects the active Unity instance
into the request-scoped state, allowing tools to access it via ctx.get_state("unity_instance").
"""
from threading import RLock
import logging

from fastmcp.server.middleware import Middleware, MiddlewareContext

from transport.plugin_hub import PluginHub

logger = logging.getLogger("mcp-for-unity-server")

# Store a global reference to the middleware instance so tools can interact
# with it to set or clear the active unity instance.
_unity_instance_middleware = None
_middleware_lock = RLock()


def get_unity_instance_middleware() -> 'UnityInstanceMiddleware':
    """Get the global Unity instance middleware."""
    global _unity_instance_middleware
    if _unity_instance_middleware is None:
        with _middleware_lock:
            if _unity_instance_middleware is None:
                # Auto-initialize if not set (lazy singleton) to handle import order or test cases
                _unity_instance_middleware = UnityInstanceMiddleware()
        
    return _unity_instance_middleware


def set_unity_instance_middleware(middleware: 'UnityInstanceMiddleware') -> None:
    """Set the global Unity instance middleware (called during server initialization)."""
    global _unity_instance_middleware
    _unity_instance_middleware = middleware


class UnityInstanceMiddleware(Middleware):
    """
    Middleware that manages per-session Unity instance selection.

    Stores active instance per session_id and injects it into request state
    for all tool and resource calls.
    """

    def __init__(self):
        super().__init__()
        self._active_by_key: dict[str, str] = {}
        self._lock = RLock()

    def get_session_key(self, ctx) -> str:
        """
        Derive a stable key for the calling session.

        Prioritizes client_id for stability.
        If client_id is missing, falls back to 'global' (assuming single-user local mode),
        ignoring session_id which can be unstable in some transports/clients.
        """
        client_id = getattr(ctx, "client_id", None)
        if isinstance(client_id, str) and client_id:
            return client_id

        # Fallback to global for local dev stability
        return "global"

    def set_active_instance(self, ctx, instance_id: str) -> None:
        """Store the active instance for this session."""
        key = self.get_session_key(ctx)
        with self._lock:
            self._active_by_key[key] = instance_id

    def get_active_instance(self, ctx) -> str | None:
        """Retrieve the active instance for this session."""
        key = self.get_session_key(ctx)
        with self._lock:
            return self._active_by_key.get(key)

    def clear_active_instance(self, ctx) -> None:
        """Clear the stored instance for this session."""
        key = self.get_session_key(ctx)
        with self._lock:
            self._active_by_key.pop(key, None)

    async def _inject_unity_instance(self, context: MiddlewareContext) -> None:
        """Inject active Unity instance into context if available."""
        ctx = context.fastmcp_context

        active_instance = self.get_active_instance(ctx)
        if active_instance:
            # If using HTTP transport (PluginHub configured), validate session
            # But for stdio transport (no PluginHub needed or maybe partially configured),
            # we should be careful not to clear instance just because PluginHub can't resolve it.
            # The 'active_instance' (Name@hash) might be valid for stdio even if PluginHub fails.
            
            session_id: str | None = None
            # Only validate via PluginHub if we are actually using HTTP transport
            # OR if we want to support hybrid mode. For now, let's be permissive.
            if PluginHub.is_configured():
                try:
                    # resolving session_id might fail if the plugin disconnected
                    # We only need session_id for HTTP transport routing.
                    # For stdio, we just need the instance ID.
                    session_id = await PluginHub._resolve_session_id(active_instance)
                except (ConnectionError, ValueError, KeyError, TimeoutError) as exc:
                    # If resolution fails, it means the Unity instance is not reachable via HTTP/WS.
                    # If we are in stdio mode, this might still be fine if the user is just setting state?
                    # But usually if PluginHub is configured, we expect it to work.
                    # Let's LOG the error but NOT clear the instance immediately to avoid flickering,
                    # or at least debug why it's failing.
                    logger.debug(
                        "PluginHub session resolution failed for %s: %s; leaving active_instance unchanged",
                        active_instance,
                        exc,
                        exc_info=True,
                    )
                except Exception as exc:
                    # Re-raise unexpected system exceptions to avoid swallowing critical failures
                    if isinstance(exc, (SystemExit, KeyboardInterrupt)):
                        raise
                    logger.error(
                        "Unexpected error during PluginHub session resolution for %s: %s",
                        active_instance,
                        exc,
                        exc_info=True
                    )

            ctx.set_state("unity_instance", active_instance)
            if session_id is not None:
                ctx.set_state("unity_session_id", session_id)

    async def on_call_tool(self, context: MiddlewareContext, call_next):
        """Inject active Unity instance into tool context if available."""
        await self._inject_unity_instance(context)
        return await call_next(context)

    async def on_read_resource(self, context: MiddlewareContext, call_next):
        """Inject active Unity instance into resource context if available."""
        await self._inject_unity_instance(context)
        return await call_next(context)
