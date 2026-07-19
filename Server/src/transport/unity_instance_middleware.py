"""
Middleware for managing Unity instance selection per session.

This middleware intercepts all tool calls and injects the active Unity instance
into the request-scoped state, allowing tools to access it via ctx.get_state("unity_instance").
"""
from copy import copy, deepcopy
import logging
import time
from threading import RLock
from typing import Any, Mapping, Sequence
from urllib.parse import unquote_plus, urlsplit, urlunsplit

from fastmcp.server.middleware import Middleware, MiddlewareContext

from core.config import config
from services.registry import UNITY_TARGETABLE_TAG, get_registered_tools
from transport.plugin_hub import PluginHub

logger = logging.getLogger("mcp-for-unity-server")
# Separate logger that propagates to root -> stderr so diagnostics show in console
_diag = logging.getLogger("transport.unity_instance_middleware")

# Store a global reference to the middleware instance so tools can interact
# with it to set or clear the active unity instance.
_unity_instance_middleware = None
_middleware_lock = RLock()

_UNITY_INSTANCE_PARAMETER_SCHEMA = {
    "type": "string",
    "minLength": 1,
    "description": (
        "Advanced: route only this call to a Unity Editor identified by exact "
        "Name@hash, a unique hash prefix, or a stdio port. Usually omit this "
        "parameter and use the session's active instance."
    ),
}


class InstanceTargetError(ValueError):
    """A user-supplied Unity instance target could not be resolved."""

    def __init__(self, message: str, *, status_code: int = 404):
        """Create an instance-target error with its HTTP status code."""
        super().__init__(message)
        self.status_code = status_code


def _instance_attribute(instance: Any, name: str, default: Any = None) -> Any:
    """Read an instance field from either a mapping or an object."""
    if isinstance(instance, Mapping):
        return instance.get(name, default)
    return getattr(instance, name, default)


def _instance_id(instance: Any) -> str | None:
    """Return an instance's canonical ``Name@hash`` identifier when available."""
    value = _instance_attribute(instance, "id")
    if isinstance(value, str) and value:
        return value

    name = _instance_attribute(instance, "name") or _instance_attribute(instance, "project")
    hash_value = _instance_attribute(instance, "hash")
    if name and hash_value:
        return f"{name}@{hash_value}"
    return None


def _instance_name(instance: Any) -> str | None:
    """Return an instance's project name."""
    value = _instance_attribute(instance, "name") or _instance_attribute(instance, "project")
    if isinstance(value, str) and value:
        return value
    instance_id = _instance_id(instance)
    if instance_id and "@" in instance_id:
        return instance_id.rsplit("@", 1)[0]
    return None


def _instance_hash(instance: Any) -> str | None:
    """Return an instance's project hash."""
    value = _instance_attribute(instance, "hash")
    if isinstance(value, str) and value:
        return value
    instance_id = _instance_id(instance)
    if instance_id and "@" in instance_id:
        return instance_id.rsplit("@", 1)[1]
    return None


def resolve_instance_identifier(
    value: str,
    instances: Sequence[Any],
    *,
    transport_mode: str = "stdio",
) -> str:
    """Resolve an explicit instance token to its canonical ``Name@hash`` id.

    This helper is intentionally limited to explicit-target resolution.  It does
    not implement the no-target default/auto-select policy used by the transports.
    """
    if not isinstance(value, str):
        raise InstanceTargetError(
            "unity_instance must be a string.",
            status_code=400,
        )
    value = value.strip()
    if not value:
        raise InstanceTargetError("unity_instance value must not be empty.", status_code=400)

    transport = (transport_mode or "stdio").lower()
    ids = [resolved_id for resolved_id in (_instance_id(item) for item in instances) if resolved_id]

    # A composite target is deliberately exact. In particular, a wrong hash
    # must never fall back to a matching project name.
    if "@" in value:
        for instance in instances:
            instance_id = _instance_id(instance)
            if instance_id and instance_id.lower() == value.lower():
                return instance_id
        available = ", ".join(ids) or "none"
        raise InstanceTargetError(
            f"Instance '{value}' not found. Available: {available}. "
            "Read mcpforunity://instances for current sessions."
        )

    # Project names take precedence over numeric hash/port shorthand, matching
    # the legacy stdio connection-pool resolution order.
    name_matches = [
        instance for instance in instances
        if (_instance_name(instance) or "") == value
    ]
    if len(name_matches) == 1:
        resolved_id = _instance_id(name_matches[0])
        if resolved_id:
            return resolved_id
    if len(name_matches) > 1:
        ambiguous = ", ".join(_instance_id(instance) or "?" for instance in name_matches)
        raise InstanceTargetError(
            f"Project name '{value}' matches multiple Unity instances ({ambiguous}). "
            "Provide the full Name@hash.",
            status_code=400,
        )

    if value.isdigit():
        if transport == "http":
            exact_hash_matches = [
                instance for instance in instances
                if (_instance_hash(instance) or "") == value
            ]
            if len(exact_hash_matches) == 1:
                resolved_id = _instance_id(exact_hash_matches[0])
                if resolved_id:
                    return resolved_id
            if len(exact_hash_matches) > 1:
                ambiguous = ", ".join(
                    _instance_id(instance) or "?"
                    for instance in exact_hash_matches
                )
                raise InstanceTargetError(
                    f"Hash '{value}' is ambiguous ({ambiguous}). "
                    "Provide the full Name@hash.",
                    status_code=400,
                )
            hash_prefix_matches = [
                instance for instance in instances
                if (_instance_hash(instance) or "").startswith(value)
            ]
            if len(hash_prefix_matches) == 1:
                resolved_id = _instance_id(hash_prefix_matches[0])
                if resolved_id:
                    return resolved_id
            if len(hash_prefix_matches) > 1:
                ambiguous = ", ".join(
                    _instance_id(instance) or "?"
                    for instance in hash_prefix_matches
                )
                raise InstanceTargetError(
                    f"Hash prefix '{value}' is ambiguous ({ambiguous}). "
                    "Provide the full Name@hash from mcpforunity://instances.",
                    status_code=400,
                )
            raise InstanceTargetError(
                f"Port-based targeting ('{value}') is not supported in HTTP transport mode. "
                "Use Name@hash or a hash prefix. Read mcpforunity://instances for available instances.",
                status_code=400,
            )

        port_matches = [
            instance for instance in instances
            if str(_instance_attribute(instance, "port", "")) == value
        ]
        if len(port_matches) == 1:
            resolved_id = _instance_id(port_matches[0])
            if resolved_id:
                return resolved_id
        available = ", ".join(
            f"{_instance_id(instance) or '?'} (port {_instance_attribute(instance, 'port', '?')})"
            for instance in instances
        ) or "none"
        if len(port_matches) > 1:
            raise InstanceTargetError(
                f"Port '{value}' is ambiguous. Available: {available}.",
                status_code=400,
            )
        raise InstanceTargetError(
            f"No Unity instance found on port {value}. Available: {available}.",
            status_code=404,
        )

    # Match hash prefixes after exact IDs and project names, as the stdio
    # connection pool does.
    lookup = value.lower()
    hash_matches = [
        instance for instance in instances
        if (_instance_hash(instance) or "").lower().startswith(lookup)
    ]
    if len(hash_matches) == 1:
        resolved_id = _instance_id(hash_matches[0])
        if resolved_id:
            return resolved_id
    if len(hash_matches) > 1:
        ambiguous = ", ".join(_instance_id(instance) or "?" for instance in hash_matches)
        raise InstanceTargetError(
            f"Hash prefix '{value}' is ambiguous ({ambiguous}). "
            "Provide the full Name@hash from mcpforunity://instances.",
            status_code=400,
        )

    available = ", ".join(ids) or "none"
    raise InstanceTargetError(
        f"No running Unity instance matches '{value}'. Available: {available}. "
        "Read mcpforunity://instances for current sessions."
    )


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

    # Key used in FastMCP's session-scoped state store for the active instance.
    _ACTIVE_INSTANCE_STATE_KEY = "mcpforunity.active_instance"

    def __init__(self):
        super().__init__()
        self._metadata_lock = RLock()
        self._unity_managed_tool_names: set[str] = set()
        self._tool_alias_to_unity_target: dict[str, str] = {}
        self._server_only_tool_names: set[str] = set()
        self._unity_targetable_tool_names: set[str] = set()
        self._tool_visibility_signature: tuple[tuple[str, str], ...] = ()
        self._last_tool_visibility_refresh = 0.0
        self._tool_visibility_refresh_interval_seconds = 0.5
        self._has_logged_empty_registry_warning = False

    async def set_active_instance(self, ctx: Any, instance_id: str) -> None:
        """Store the active instance for this MCP session.

        Persisted via FastMCP's session-scoped state store, which keys by
        ``ctx.session_id`` (the MCP-Session-Id header on HTTP, a per-subprocess
        UUID on stdio). Two MCP sessions cannot share state — see #1023 for the
        bug this replaces, which keyed on the peer-supplied ``client_id`` and
        collapsed multiple clients onto the same record.
        """
        await ctx.set_state(self._ACTIVE_INSTANCE_STATE_KEY, instance_id)

    async def get_active_instance(self, ctx: Any) -> str | None:
        """Retrieve the active instance for this MCP session."""
        return await ctx.get_state(self._ACTIVE_INSTANCE_STATE_KEY)

    async def clear_active_instance(self, ctx: Any) -> None:
        """Clear the stored instance for this MCP session.

        Overwrites with None rather than calling ``delete_state``: the read
        path already treats None as "no active instance", and this keeps the
        method usable from minimal context shims that don't implement
        ``delete_state``.
        """
        await ctx.set_state(self._ACTIVE_INSTANCE_STATE_KEY, None)

    async def _discover_instances(self, ctx: Any) -> list[Any]:
        """
        Return running Unity instances across both HTTP (PluginHub) and stdio transports.

        Returns a list of objects with .id (Name@hash) and .hash attributes.
        """
        from types import SimpleNamespace
        transport = (config.transport_mode or "stdio").lower()
        results: list[Any] = []

        if PluginHub.is_configured():
            try:
                user_id = None
                get_state_fn = getattr(ctx, "get_state", None)
                if callable(get_state_fn) and config.http_remote_hosted:
                    user_id = await get_state_fn("user_id")
                sessions_data = await PluginHub.get_sessions(user_id=user_id)
                sessions = sessions_data.sessions or {}
                for session_info in sessions.values():
                    project = getattr(session_info, "project", None) or "Unknown"
                    hash_value = getattr(session_info, "hash", None)
                    if hash_value:
                        results.append(SimpleNamespace(
                            id=f"{project}@{hash_value}",
                            hash=hash_value,
                            name=project,
                        ))
            except Exception as exc:
                if isinstance(exc, (SystemExit, KeyboardInterrupt)):
                    raise
                logger.debug("PluginHub instance discovery failed (%s)", type(exc).__name__, exc_info=True)

        if not results and transport != "http":
            try:
                from transport.legacy.unity_connection import get_unity_connection_pool
                pool = get_unity_connection_pool()
                results = pool.discover_all_instances(force_refresh=True)
            except Exception as exc:
                if isinstance(exc, (SystemExit, KeyboardInterrupt)):
                    raise
                logger.debug("Stdio instance discovery failed (%s)", type(exc).__name__, exc_info=True)

        return results

    async def _resolve_instance_value(self, value: str, ctx: Any) -> str:
        """
        Resolve a unity_instance string to a validated instance identifier.

        Accepts:
          - Bare port number like "6401" (stdio only) -> resolved Name@hash
          - "Name@hash" exact match
          - Hash prefix (unique prefix match against running instances)

        Raises ValueError with a user-friendly message on failure.
        """
        transport = (config.transport_mode or "stdio").lower()
        instances = await self._discover_instances(ctx)
        return resolve_instance_identifier(
            value,
            instances,
            transport_mode=transport,
        )

    async def _maybe_autoselect_instance(self, ctx: Any) -> str | None:
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
                        await self.set_active_instance(ctx, chosen)
                        logger.info(
                            "Auto-selected sole Unity instance via PluginHub: %s",
                            chosen,
                        )
                        return chosen
                    if len(ids) > 1:
                        logger.info(
                            "Multiple Unity instances found (%d). Pass unity_instance on any tool call "
                            "or call set_active_instance to choose one. Available: %s",
                            len(ids), ", ".join(ids),
                        )
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
                        await self.set_active_instance(ctx, chosen)
                        logger.info(
                            "Auto-selected sole Unity instance via stdio discovery: %s",
                            chosen,
                        )
                        return chosen
                    if len(ids) > 1:
                        logger.info(
                            "Multiple Unity instances found (%d). Pass unity_instance on any tool call "
                            "or call set_active_instance to choose one. Available: %s",
                            len(ids), ", ".join(ids),
                        )
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

    @staticmethod
    async def _set_request_routing_state(ctx: Any, instance_id: str | None) -> None:
        """Set routing state for this request without shadowing session state."""
        set_state = getattr(ctx, "set_state", None)
        if not callable(set_state):
            return

        try:
            await set_state("unity_instance", instance_id, serializable=False)
        except TypeError:
            # Keep minimal test contexts and older Context shims usable. FastMCP
            # 3.x accepts serializable=False and uses request-scoped state.
            await set_state("unity_instance", instance_id)

    @staticmethod
    def _consume_resource_target(context: MiddlewareContext) -> tuple[str | None, MiddlewareContext]:
        """Extract unity_instance from a Resource URI and return a clean context."""
        message = getattr(context, "message", None)
        uri = getattr(message, "uri", None)
        if uri is None:
            return None, context

        uri_text = str(uri)
        parsed = urlsplit(uri_text)
        if not parsed.query:
            return None, context

        target_values: list[str] = []
        clean_query_parts: list[str] = []
        for part in parsed.query.split("&"):
            key, separator, value = part.partition("=")
            if unquote_plus(key) == "unity_instance":
                target_values.append(unquote_plus(value) if separator else "")
            else:
                clean_query_parts.append(part)

        if not target_values:
            return None, context
        if len(target_values) > 1:
            raise ValueError(
                "Resource URI must contain at most one unity_instance query parameter."
            )

        clean_uri = urlunsplit(
            (
                parsed.scheme,
                parsed.netloc,
                parsed.path,
                "&".join(clean_query_parts),
                parsed.fragment,
            )
        )

        # MiddlewareContext is immutable, so carry a copied MCP request message
        # into FastMCP's resource matcher. The fallback keeps direct test shims
        # that expose a mutable SimpleNamespace compatible.
        if clean_uri == uri_text:
            return target_values[0], context
        if hasattr(message, "model_copy"):
            clean_message = message.model_copy(update={"uri": clean_uri})
        else:
            clean_message = copy(message)
            setattr(clean_message, "uri", clean_uri)
        if hasattr(context, "copy"):
            return target_values[0], context.copy(message=clean_message)
        return target_values[0], context

    async def _inject_unity_instance(self, context: MiddlewareContext) -> MiddlewareContext:
        """Inject active Unity instance and user_id into request-scoped state."""
        ctx = context.fastmcp_context
        if ctx is None:
            return context

        # Always shadow the routing key for this request, including no-target
        # calls, so a reused context cannot retain a prior per-call selection.
        await self._set_request_routing_state(ctx, None)

        # Resolve user_id from the HTTP request's API key header
        user_id = await self._resolve_user_id()
        if config.http_remote_hosted and user_id is None:
            raise RuntimeError(
                "API key authentication required. Provide a valid X-API-Key header."
            )
        if user_id:
            await ctx.set_state("user_id", user_id)

        # Per-call routing: consume the Tool argument before FastMCP/Pydantic
        # validation, or consume the Resource URI query before URI matching.
        requested_instance: str | None = None
        clean_context = context
        msg_args = getattr(getattr(context, "message", None), "arguments", None)
        if isinstance(msg_args, dict) and "unity_instance" in msg_args:
            raw = msg_args.pop("unity_instance")
            if raw is not None:
                raw_str = str(raw).strip()
                if raw_str:
                    requested_instance = await self._resolve_instance_value(raw_str, ctx)
                    logger.debug("Per-call unity_instance resolved to: %s", requested_instance)
        elif getattr(getattr(context, "message", None), "uri", None) is not None:
            raw, clean_context = self._consume_resource_target(context)
            if raw:
                requested_instance = await self._resolve_instance_value(raw, ctx)
                logger.debug("Per-call resource unity_instance resolved to: %s", requested_instance)

        # Explicit target > session active instance > the existing auto/default
        # behavior. Only the auto-select path is allowed to persist a selection.
        effective_instance = requested_instance
        if not effective_instance:
            effective_instance = await self.get_active_instance(ctx)
        if not effective_instance:
            effective_instance = await self._maybe_autoselect_instance(ctx)

        await self._set_request_routing_state(ctx, effective_instance)
        return clean_context

    async def on_call_tool(self, context: MiddlewareContext, call_next):
        """Inject active Unity instance into tool context if available."""
        await self._inject_unity_instance(context)
        return await call_next(context)

    async def on_read_resource(self, context: MiddlewareContext, call_next):
        """Inject active Unity instance into resource context if available."""
        clean_context = await self._inject_unity_instance(context)
        return await call_next(clean_context)

    async def on_list_tools(self, context: MiddlewareContext, call_next):
        """Filter MCP tool listing to the Unity-enabled set when session data is available."""
        try:
            await self._inject_unity_instance(context)
        except Exception as exc:
            # Re-raise authentication errors so callers get a proper auth failure
            if isinstance(exc, RuntimeError) and "authentication" in str(exc).lower():
                raise
            _diag.warning(
                "on_list_tools: _inject_unity_instance failed (%s: %s), continuing without instance",
                type(exc).__name__, exc,
            )

        tools = await call_next(context)

        self._refresh_tool_visibility_metadata_from_registry()

        tool_names_from_fastmcp = sorted(getattr(t, "name", "?") for t in tools)
        _diag.debug(
            "on_list_tools: FastMCP returned %d tools: %s",
            len(tools), tool_names_from_fastmcp,
        )

        if not self._should_filter_tool_listing():
            _diag.debug("on_list_tools: skipping middleware filter (not HTTP or PluginHub not configured)")
            return [self._with_unity_instance_schema(tool) for tool in tools]

        enabled_tool_names = await self._resolve_enabled_tool_names_for_context(context)
        if enabled_tool_names is None:
            _diag.debug("on_list_tools: no Unity session data, returning %d tools from FastMCP as-is", len(tools))
            return [self._with_unity_instance_schema(tool) for tool in tools]

        filtered = []
        for tool in tools:
            tool_name = getattr(tool, "name", None)
            if self._is_tool_visible(tool_name, enabled_tool_names):
                filtered.append(tool)

        _diag.debug(
            "on_list_tools: filtered %d/%d tools visible (Unity register_tools). "
            "enabled_names=%s",
            len(filtered), len(tools), sorted(enabled_tool_names),
        )
        return [self._with_unity_instance_schema(tool) for tool in filtered]

    def _should_filter_tool_listing(self) -> bool:
        transport = (config.transport_mode or "stdio").lower()
        return transport == "http" and PluginHub.is_configured()

    async def _resolve_enabled_tool_names_for_context(
        self,
        context: MiddlewareContext,
    ) -> set[str] | None:
        ctx = context.fastmcp_context
        user_id = (await ctx.get_state("user_id")) if config.http_remote_hosted else None
        active_instance = await ctx.get_state("unity_instance")
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
            unity_targetable_tool_names: set[str] = set()
            signature_entries: list[tuple[str, str]] = []

            for tool_info in registry_tools:
                tool_name = tool_info.get("name")
                if not isinstance(tool_name, str) or not tool_name:
                    continue

                unity_target = tool_info.get("unity_target", tool_name)
                unity_targetable = tool_info.get(
                    "unity_targetable",
                    unity_target is not None,
                )
                if unity_targetable is True:
                    unity_targetable_tool_names.add(tool_name)

                if unity_target is None:
                    server_only_tool_names.add(tool_name)
                    signature_entries.append((
                        tool_name,
                        f"<server-only>|targetable={unity_targetable is True}",
                    ))
                    continue

                if not isinstance(unity_target, str) or not unity_target:
                    logger.debug(
                        "Skipping tool visibility metadata with invalid unity_target: %s",
                        tool_info,
                    )
                    continue

                if unity_target == tool_name:
                    unity_managed_tool_names.add(tool_name)
                    signature_entries.append((
                        tool_name,
                        f"{unity_target}|targetable={unity_targetable is True}",
                    ))
                    continue

                tool_alias_to_unity_target[tool_name] = unity_target
                unity_managed_tool_names.add(unity_target)
                signature_entries.append((
                    tool_name,
                    f"{unity_target}|targetable={unity_targetable is True}",
                ))

            signature = tuple(sorted(signature_entries, key=lambda item: item[0]))
            if signature == self._tool_visibility_signature:
                self._last_tool_visibility_refresh = now
                return

            self._unity_managed_tool_names = unity_managed_tool_names
            self._tool_alias_to_unity_target = tool_alias_to_unity_target
            self._server_only_tool_names = server_only_tool_names
            self._unity_targetable_tool_names = unity_targetable_tool_names
            self._tool_visibility_signature = signature
            self._last_tool_visibility_refresh = now

    def _is_tool_targetable(self, tool: Any) -> bool:
        """Return whether a registered tool accepts per-call routing."""
        tool_name = getattr(tool, "name", None)
        tags = getattr(tool, "tags", None) or set()
        return (
            UNITY_TARGETABLE_TAG in tags
            or (
                isinstance(tool_name, str)
                and tool_name in self._unity_targetable_tool_names
            )
        )

    def _with_unity_instance_schema(self, tool: Any) -> Any:
        """Return a tool copy advertising the optional routing envelope."""
        if not self._is_tool_targetable(tool):
            return tool

        parameters = getattr(tool, "parameters", None)
        model_copy = getattr(tool, "model_copy", None)
        if not isinstance(parameters, dict) or not callable(model_copy):
            return tool

        updated_parameters = deepcopy(parameters)
        properties = updated_parameters.get("properties")
        if not isinstance(properties, dict):
            properties = {}
            updated_parameters["properties"] = properties
        properties["unity_instance"] = deepcopy(_UNITY_INSTANCE_PARAMETER_SCHEMA)
        return model_copy(update={"parameters": updated_parameters})

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
