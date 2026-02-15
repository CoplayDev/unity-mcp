"""
Middleware for managing Unity instance selection per session.

This middleware intercepts all tool calls and injects the active Unity instance
into the request-scoped state, allowing tools to access it via ctx.get_state("unity_instance").
"""
from threading import RLock
import logging
import time

from fastmcp.server.middleware import Middleware, MiddlewareContext

from core.config import config
from services.registry import get_registered_tools
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
    """Replace the global middleware instance.

    This is a test seam: production code uses ``get_unity_instance_middleware()``
    which lazy-initialises the singleton.  Tests call this function to inject a
    mock or pre-configured middleware before exercising tool/resource code.
    """
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
        self._metadata_lock = RLock()
        self._unity_managed_tool_names: set[str] = set()
        self._tool_alias_to_unity_target: dict[str, str] = {}
        self._server_only_tool_names: set[str] = set()
        self._tool_visibility_signature: tuple[tuple[str, str], ...] = ()
        self._last_tool_visibility_refresh = 0.0
        self._tool_visibility_refresh_interval_seconds = 0.5
        self._has_logged_empty_registry_warning = False

    def get_session_key(self, ctx) -> str:
        """
        Derive a stable key for the calling session.

        Prioritizes client_id for stability.
        In remote-hosted mode, falls back to user_id for session isolation.
        Otherwise falls back to 'global' (assuming single-user local mode).
        """
        client_id = getattr(ctx, "client_id", None)
        if isinstance(client_id, str) and client_id:
            return client_id

        # In remote-hosted mode, use user_id so different users get isolated instance selections
        user_id = ctx.get_state("user_id")
        if isinstance(user_id, str) and user_id:
            return f"user:{user_id}"

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

    async def _maybe_autoselect_instance(self, ctx) -> str | None:
        """
        Auto-select the sole Unity instance when no active instance is set.

        Note: This method both *discovers* and *persists* the selection via
        `set_active_instance` as a side-effect, since callers expect the selection
        to stick for subsequent tool/resource calls in the same session.
        """
        try:
            transport = (config.transport_mode or "stdio").lower()
            # This implicit behavior works well for solo-users, but is dangerous for multi-user setups
            if transport == "http" and config.http_remote_hosted:
                return None
            if PluginHub.is_configured():
                try:
                    sessions_data = await PluginHub.get_sessions()
                    sessions = sessions_data.sessions or {}
                    ids: list[str] = []
                    for session_info in sessions.values():
                        project = getattr(
                            session_info, "project", None) or "Unknown"
                        hash_value = getattr(session_info, "hash", None)
                        if hash_value:
                            ids.append(f"{project}@{hash_value}")
                    if len(ids) == 1:
                        chosen = ids[0]
                        self.set_active_instance(ctx, chosen)
                        logger.info(
                            "Auto-selected sole Unity instance via PluginHub: %s",
                            chosen,
                        )
                        return chosen
                except (ConnectionError, ValueError, KeyError, TimeoutError, AttributeError) as exc:
                    logger.debug(
                        "PluginHub auto-select probe failed (%s); falling back to stdio",
                        type(exc).__name__,
                        exc_info=True,
                    )
                except Exception as exc:
                    if isinstance(exc, (SystemExit, KeyboardInterrupt)):
                        raise
                    logger.debug(
                        "PluginHub auto-select probe failed with unexpected error (%s); falling back to stdio",
                        type(exc).__name__,
                        exc_info=True,
                    )

            if transport != "http":
                try:
                    # Import here to avoid circular imports in legacy transport paths.
                    from transport.legacy.unity_connection import get_unity_connection_pool

                    pool = get_unity_connection_pool()
                    instances = pool.discover_all_instances(force_refresh=True)
                    ids = [getattr(inst, "id", None) for inst in instances]
                    ids = [inst_id for inst_id in ids if inst_id]
                    if len(ids) == 1:
                        chosen = ids[0]
                        self.set_active_instance(ctx, chosen)
                        logger.info(
                            "Auto-selected sole Unity instance via stdio discovery: %s",
                            chosen,
                        )
                        return chosen
                except (ConnectionError, ValueError, KeyError, TimeoutError, AttributeError) as exc:
                    logger.debug(
                        "Stdio auto-select probe failed (%s)",
                        type(exc).__name__,
                        exc_info=True,
                    )
                except Exception as exc:
                    if isinstance(exc, (SystemExit, KeyboardInterrupt)):
                        raise
                    logger.debug(
                        "Stdio auto-select probe failed with unexpected error (%s)",
                        type(exc).__name__,
                        exc_info=True,
                    )
        except Exception as exc:
            if isinstance(exc, (SystemExit, KeyboardInterrupt)):
                raise
            logger.debug(
                "Auto-select path encountered an unexpected error (%s)",
                type(exc).__name__,
                exc_info=True,
            )

        return None

    async def _resolve_user_id(self) -> str | None:
        """Extract user_id from the current HTTP request's API key."""
        if not config.http_remote_hosted:
            return None
        # Lazy import to avoid circular dependencies (same pattern as _maybe_autoselect_instance).
        from transport.unity_transport import _resolve_user_id_from_request
        return await _resolve_user_id_from_request()

    async def _inject_unity_instance(self, context: MiddlewareContext) -> None:
        """Inject active Unity instance and user_id into context if available."""
        ctx = context.fastmcp_context

        # Resolve user_id from the HTTP request's API key header
        user_id = await self._resolve_user_id()
        if config.http_remote_hosted and user_id is None:
            raise RuntimeError(
                "API key authentication required. Provide a valid X-API-Key header."
            )
        if user_id:
            ctx.set_state("user_id", user_id)

        active_instance = self.get_active_instance(ctx)
        if not active_instance:
            active_instance = await self._maybe_autoselect_instance(ctx)
        if active_instance:
            # If using HTTP transport (PluginHub configured), validate session
            # But for stdio transport (no PluginHub needed or maybe partially configured),
            # we should be careful not to clear instance just because PluginHub can't resolve it.
            # The 'active_instance' (Name@hash) might be valid for stdio even if PluginHub fails.

            session_id: str | None = None
            # Only validate via PluginHub if we are actually using HTTP transport.
            # For stdio transport, skip PluginHub entirely - we only need the instance ID.
            from transport.unity_transport import _is_http_transport
            if _is_http_transport() and PluginHub.is_configured():
                try:
                    # resolving session_id might fail if the plugin disconnected
                    # We only need session_id for HTTP transport routing.
                    # For stdio, we just need the instance ID.
                    # Pass user_id for remote-hosted mode session isolation
                    session_id = await PluginHub._resolve_session_id(active_instance, user_id=user_id)
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

    async def on_list_tools(self, context: MiddlewareContext, call_next):
        """Filter MCP tool listing to the Unity-enabled set when session data is available."""
        await self._inject_unity_instance(context)
        tools = await call_next(context)

        if not self._should_filter_tool_listing():
            return tools

        self._refresh_tool_visibility_metadata_from_registry()
        enabled_tool_names = await self._resolve_enabled_tool_names_for_context(context)
        if enabled_tool_names is None:
            return tools

        filtered = []
        for tool in tools:
            tool_name = getattr(tool, "name", None)
            if self._is_tool_visible(tool_name, enabled_tool_names):
                filtered.append(tool)

        return filtered

    def _should_filter_tool_listing(self) -> bool:
        transport = (config.transport_mode or "stdio").lower()
        return transport == "http" and PluginHub.is_configured()

    async def _resolve_enabled_tool_names_for_context(
        self,
        context: MiddlewareContext,
    ) -> set[str] | None:
        ctx = context.fastmcp_context
        user_id = ctx.get_state("user_id") if config.http_remote_hosted else None
        active_instance = ctx.get_state("unity_instance")
        project_hashes = self._resolve_candidate_project_hashes(active_instance)
        try:
            sessions_data = await PluginHub.get_sessions(user_id=user_id)
            sessions = sessions_data.sessions if sessions_data else {}
        except Exception as exc:
            logger.debug(
                "Failed to fetch sessions for tool filtering (user_id=%s, %s)",
                user_id,
                type(exc).__name__,
                exc_info=True,
            )
            return None

        session_hashes = {
            getattr(session, "hash", None)
            for session in sessions.values()
            if getattr(session, "hash", None)
        }

        if project_hashes:
            active_hash = project_hashes[0]
            # Stale active_instance should not hide all Unity-managed tools.
            if active_hash not in session_hashes:
                return None
        else:
            if not sessions:
                return None

            if len(sessions) == 1:
                only_session = next(iter(sessions.values()))
                only_hash = getattr(only_session, "hash", None)
                if only_hash:
                    project_hashes = [only_hash]
            else:
                # Multiple sessions without explicit selection: use a union so we don't
                # hide tools that are valid in at least one visible Unity instance.
                project_hashes = [hash_value for hash_value in session_hashes if hash_value]

        if not project_hashes:
            return None

        enabled_tool_names: set[str] = set()
        resolved_any_project = False
        for project_hash in project_hashes:
            try:
                registered_tools = await PluginHub.get_tools_for_project(project_hash, user_id=user_id)
                # Only mark as resolved if tools are actually registered.
                # An empty list means register_tools hasn't been sent yet.
                if registered_tools:
                    resolved_any_project = True
            except Exception as exc:
                logger.debug(
                    "Failed to fetch tools for project hash %s (user_id=%s, %s)",
                    project_hash,
                    user_id,
                    type(exc).__name__,
                    exc_info=True,
                )
                continue

            for tool in registered_tools:
                tool_name = getattr(tool, "name", None)
                if isinstance(tool_name, str) and tool_name:
                    enabled_tool_names.add(tool_name)

        if not resolved_any_project:
            return None

        return enabled_tool_names

    def _refresh_tool_visibility_metadata_from_registry(self) -> None:
        now = time.monotonic()
        if now - self._last_tool_visibility_refresh < self._tool_visibility_refresh_interval_seconds:
            return

        with self._metadata_lock:
            now = time.monotonic()
            if now - self._last_tool_visibility_refresh < self._tool_visibility_refresh_interval_seconds:
                return

            try:
                registry_tools = get_registered_tools()
            except Exception:
                logger.warning(
                    "Failed to refresh tool visibility metadata from registry; keeping previous metadata.",
                    exc_info=True,
                )
                self._last_tool_visibility_refresh = now
                return

            if not registry_tools and not self._has_logged_empty_registry_warning:
                logger.warning(
                    "Tool registry is empty during tool-list filtering; treating tools as unknown/visible."
                )
                self._has_logged_empty_registry_warning = True
            elif registry_tools:
                self._has_logged_empty_registry_warning = False

            unity_managed_tool_names: set[str] = set()
            tool_alias_to_unity_target: dict[str, str] = {}
            server_only_tool_names: set[str] = set()
            signature_entries: list[tuple[str, str]] = []

            for tool_info in registry_tools:
                tool_name = tool_info.get("name")
                if not isinstance(tool_name, str) or not tool_name:
                    continue

                unity_target = tool_info.get("unity_target", tool_name)
                if unity_target is None:
                    server_only_tool_names.add(tool_name)
                    signature_entries.append((tool_name, "<server-only>"))
                    continue

                if not isinstance(unity_target, str) or not unity_target:
                    logger.debug(
                        "Skipping tool visibility metadata with invalid unity_target: %s",
                        tool_info,
                    )
                    continue

                if unity_target == tool_name:
                    unity_managed_tool_names.add(tool_name)
                    signature_entries.append((tool_name, unity_target))
                    continue

                tool_alias_to_unity_target[tool_name] = unity_target
                unity_managed_tool_names.add(unity_target)
                signature_entries.append((tool_name, unity_target))

            signature = tuple(sorted(signature_entries, key=lambda item: item[0]))
            if signature == self._tool_visibility_signature:
                self._last_tool_visibility_refresh = now
                return

            self._unity_managed_tool_names = unity_managed_tool_names
            self._tool_alias_to_unity_target = tool_alias_to_unity_target
            self._server_only_tool_names = server_only_tool_names
            self._tool_visibility_signature = signature
            self._last_tool_visibility_refresh = now

    @staticmethod
    def _resolve_candidate_project_hashes(active_instance: str | None) -> list[str]:
        if not active_instance:
            return []

        if "@" in active_instance:
            _, _, suffix = active_instance.rpartition("@")
            return [suffix] if suffix else []

        return [active_instance]

    def _is_tool_visible(self, tool_name: str | None, enabled_tool_names: set[str]) -> bool:
        if not isinstance(tool_name, str) or not tool_name:
            return True

        if tool_name in self._server_only_tool_names:
            return True

        if tool_name in enabled_tool_names:
            return True

        unity_target = self._tool_alias_to_unity_target.get(tool_name)
        if unity_target:
            return unity_target in enabled_tool_names

        # Keep unknown tools visible for forward compatibility.
        if tool_name not in self._unity_managed_tool_names:
            return True

        return False
