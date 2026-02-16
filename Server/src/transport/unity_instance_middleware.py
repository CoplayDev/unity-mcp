"""
Middleware for managing Unity instance selection per session.

This middleware intercepts all tool calls and injects the active Unity instance
into the request-scoped state, allowing tools to access it via ctx.get_state("unity_instance").
"""
import asyncio
from threading import RLock
from datetime import datetime, timezone
import json
import logging
import os
import time
from pathlib import Path

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
        self._session_lock = RLock()
        self._unity_managed_tool_names: set[str] = set()
        self._tool_alias_to_unity_target: dict[str, str] = {}
        self._server_only_tool_names: set[str] = set()
        self._tool_visibility_signature: tuple[tuple[str, str], ...] = ()
        self._last_tool_visibility_refresh = 0.0
        self._tool_visibility_refresh_interval_seconds = 0.5
        self._has_logged_empty_registry_warning = False
        self._tracked_sessions: dict[str, object] = {}
        self._stdio_tools_watch_task: asyncio.Task | None = None
        self._last_stdio_tools_state_signature: tuple[tuple[str, tuple[str, ...]], ...] | None = None

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

    @staticmethod
    def _is_stdio_transport() -> bool:
        return (config.transport_mode or "stdio").lower() == "stdio"

    def _track_session_from_context(self, fastmcp_context) -> bool:
        if fastmcp_context is None or fastmcp_context.request_context is None:
            return False

        try:
            session_id = fastmcp_context.session_id
            session = fastmcp_context.session
        except RuntimeError:
            return False

        if not isinstance(session_id, str) or not session_id:
            return False

        with self._session_lock:
            existing = self._tracked_sessions.get(session_id)
            if existing is session:
                return False

            self._tracked_sessions[session_id] = session

        return True

    async def _notify_tool_list_changed_to_sessions(self, reason: str) -> None:
        with self._session_lock:
            session_items = list(self._tracked_sessions.items())

        if not session_items:
            return

        stale_session_ids: list[str] = []
        sent_count = 0
        for session_id, session in session_items:
            try:
                await session.send_tool_list_changed()
                sent_count += 1
            except Exception:
                stale_session_ids.append(session_id)
                logger.debug(
                    "Failed sending tools/list_changed to session %s (reason=%s); session will be removed.",
                    session_id,
                    reason,
                    exc_info=True,
                )

        if stale_session_ids:
            with self._session_lock:
                for session_id in stale_session_ids:
                    self._tracked_sessions.pop(session_id, None)

        if sent_count:
            logger.debug(
                "Sent tools/list_changed notification to %d tracked session(s) (reason=%s).",
                sent_count,
                reason,
            )

    def _build_stdio_tools_state_signature(self) -> tuple[tuple[str, tuple[str, ...]], ...]:
        payloads = self._list_stdio_status_payloads()
        enabled_by_hash: dict[str, tuple[str, ...]] = {}
        for payload in payloads:
            project_hash = payload.get("project_hash")
            if not isinstance(project_hash, str) or not project_hash or project_hash in enabled_by_hash:
                continue

            enabled_raw = payload.get("enabled_tools")
            if isinstance(enabled_raw, set):
                enabled_tools = tuple(
                    sorted(
                        tool_name
                        for tool_name in enabled_raw
                        if isinstance(tool_name, str) and tool_name
                    )
                )
            elif isinstance(enabled_raw, list):
                enabled_tools = tuple(
                    sorted(
                        tool_name
                        for tool_name in enabled_raw
                        if isinstance(tool_name, str) and tool_name
                    )
                )
            else:
                enabled_tools = ()

            enabled_by_hash[project_hash] = enabled_tools

        return tuple(sorted(enabled_by_hash.items(), key=lambda item: item[0]))

    @staticmethod
    def _get_stdio_tools_watch_interval_seconds() -> float:
        raw_interval = os.getenv("UNITY_MCP_STDIO_TOOLS_WATCH_INTERVAL_SECONDS", "1.0")
        try:
            parsed_interval = float(raw_interval)
            if parsed_interval < 0.2:
                return 0.2
            return parsed_interval
        except (TypeError, ValueError):
            return 1.0

    async def _run_stdio_tools_watch_loop(self, interval_seconds: float) -> None:
        while True:
            try:
                await asyncio.sleep(interval_seconds)
                current_signature = self._build_stdio_tools_state_signature()
                if self._last_stdio_tools_state_signature is None:
                    self._last_stdio_tools_state_signature = current_signature
                    continue

                if current_signature != self._last_stdio_tools_state_signature:
                    self._last_stdio_tools_state_signature = current_signature
                    await self._notify_tool_list_changed_to_sessions("stdio_state_changed")
            except asyncio.CancelledError:
                raise
            except Exception:
                logger.debug("stdio tools watcher iteration failed.", exc_info=True)

    async def start_stdio_tools_watcher(self) -> None:
        if not self._is_stdio_transport():
            return

        task = self._stdio_tools_watch_task
        if task is not None and not task.done():
            return

        self._last_stdio_tools_state_signature = self._build_stdio_tools_state_signature()
        interval_seconds = self._get_stdio_tools_watch_interval_seconds()
        self._stdio_tools_watch_task = asyncio.create_task(
            self._run_stdio_tools_watch_loop(interval_seconds),
            name="unity-mcp-stdio-tools-watcher",
        )
        logger.debug("Started stdio tools watcher (interval=%ss).", interval_seconds)

    async def stop_stdio_tools_watcher(self) -> None:
        task = self._stdio_tools_watch_task
        self._stdio_tools_watch_task = None
        self._last_stdio_tools_state_signature = None

        if task is not None:
            task.cancel()
            try:
                await task
            except asyncio.CancelledError:
                pass
            except Exception:
                logger.debug("Error while stopping stdio tools watcher.", exc_info=True)

        with self._session_lock:
            self._tracked_sessions.clear()

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

    async def on_message(self, context: MiddlewareContext, call_next):
        if self._is_stdio_transport():
            is_new_session = self._track_session_from_context(context.fastmcp_context)
            if is_new_session:
                await self._notify_tool_list_changed_to_sessions("session_registered")

        return await call_next(context)

    async def on_notification(self, context: MiddlewareContext, call_next):
        if self._is_stdio_transport():
            self._track_session_from_context(context.fastmcp_context)
            if context.method == "notifications/initialized":
                await self._notify_tool_list_changed_to_sessions("client_initialized")

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
        if transport == "http":
            return PluginHub.is_configured()

        return transport == "stdio"

    async def _resolve_enabled_tool_names_for_context(
        self,
        context: MiddlewareContext,
    ) -> set[str] | None:
        ctx = context.fastmcp_context
        transport = (config.transport_mode or "stdio").lower()

        if transport == "stdio":
            active_instance = ctx.get_state("unity_instance")
            return self._resolve_enabled_tool_names_for_stdio_context(active_instance)

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

    def _resolve_enabled_tool_names_for_stdio_context(self, active_instance: str | None) -> set[str] | None:
        status_payloads = self._list_stdio_status_payloads()
        if not status_payloads:
            return None

        project_hashes = self._resolve_candidate_project_hashes(active_instance)
        if project_hashes:
            active_hash = project_hashes[0]
            for payload in status_payloads:
                if payload["project_hash"] == active_hash:
                    return payload["enabled_tools"]

            logger.debug(
                "No stdio status payload matched active hash '%s'; skipping tools/list filtering.",
                active_hash,
            )
            return None

        # Multi-instance edge case (no active_instance selected): merge enabled tools from
        # all discovered status files so tools/list does not "flicker" between instances.
        # This intentionally favors stability over strict per-instance precision.
        enabled_by_project_hash: dict[str, set[str]] = {}
        for payload in status_payloads:
            project_hash = payload["project_hash"]
            if project_hash in enabled_by_project_hash:
                continue
            enabled_by_project_hash[project_hash] = payload["enabled_tools"]

        if len(enabled_by_project_hash) > 1:
            union_enabled_tools: set[str] = set()
            for enabled_tools in enabled_by_project_hash.values():
                union_enabled_tools.update(enabled_tools)
            return union_enabled_tools

        # status_payloads is non-empty here, and every payload contributes a valid
        # project_hash; after de-duplication this leaves exactly one project entry.
        return next(iter(enabled_by_project_hash.values()))

    def _list_stdio_status_payloads(self) -> list[dict[str, object]]:
        status_ttl_seconds = self._get_stdio_status_ttl_seconds()
        now_utc = datetime.now(timezone.utc)
        status_dir_env = os.getenv("UNITY_MCP_STATUS_DIR")
        status_dir = Path(status_dir_env).expanduser() if status_dir_env else Path.home().joinpath(".unity-mcp")

        try:
            status_files = sorted(
                status_dir.glob("unity-mcp-status-*.json"),
                key=lambda path: path.stat().st_mtime,
                reverse=True,
            )
        except OSError as exc:
            logger.debug(
                "Failed to enumerate stdio status files from %s: %s",
                status_dir,
                exc,
                exc_info=True,
            )
            return []

        payloads: list[dict[str, object]] = []
        for status_file in status_files:
            file_hash = self._extract_project_hash_from_filename(status_file)
            try:
                with status_file.open("r", encoding="utf-8") as handle:
                    raw_payload = json.load(handle)
            except (OSError, ValueError) as exc:
                logger.debug(
                    "Failed to parse stdio status file %s: %s",
                    status_file,
                    exc,
                    exc_info=True,
                )
                continue

            if not isinstance(raw_payload, dict):
                logger.debug("Skipping stdio status file %s with non-object payload.", status_file)
                continue

            enabled_tools_raw = raw_payload.get("enabled_tools")
            if not isinstance(enabled_tools_raw, list):
                # Missing enabled_tools means the status format is too old for safe filtering.
                logger.debug("Skipping stdio status file %s without enabled_tools field.", status_file)
                continue

            enabled_tools = {
                tool_name
                for tool_name in enabled_tools_raw
                if isinstance(tool_name, str) and tool_name
            }

            freshness = self._parse_heartbeat_datetime(raw_payload.get("last_heartbeat"))
            if freshness is None:
                try:
                    freshness = datetime.fromtimestamp(status_file.stat().st_mtime, tz=timezone.utc)
                except OSError:
                    logger.debug(
                        "Failed to read mtime for stdio status file %s; skipping for safety.",
                        status_file,
                        exc_info=True,
                    )
                    continue

            if (now_utc - freshness).total_seconds() > status_ttl_seconds:
                logger.debug(
                    "Skipping stale stdio status file %s (age exceeds %ss).",
                    status_file,
                    status_ttl_seconds,
                )
                continue

            project_hash = raw_payload.get("project_hash")
            if not isinstance(project_hash, str) or not project_hash:
                project_hash = file_hash

            if not project_hash:
                logger.debug("Skipping stdio status file %s without project hash.", status_file)
                continue

            payloads.append(
                {
                    "project_hash": project_hash,
                    "enabled_tools": enabled_tools,
                }
            )

        return payloads

    @staticmethod
    def _extract_project_hash_from_filename(status_file: Path) -> str | None:
        prefix = "unity-mcp-status-"
        stem = status_file.stem
        if not stem.startswith(prefix):
            return None

        suffix = stem[len(prefix):]
        return suffix or None

    @staticmethod
    def _get_stdio_status_ttl_seconds() -> float:
        raw_ttl = os.getenv("UNITY_MCP_STDIO_STATUS_TTL_SECONDS", "15")
        try:
            ttl = float(raw_ttl)
            if ttl > 0:
                return ttl
        except (TypeError, ValueError):
            pass
        return 15.0

    @staticmethod
    def _parse_heartbeat_datetime(raw_heartbeat: object) -> datetime | None:
        if not isinstance(raw_heartbeat, str) or not raw_heartbeat:
            return None

        try:
            parsed = datetime.fromisoformat(raw_heartbeat.replace("Z", "+00:00"))
        except ValueError:
            return None

        if parsed.tzinfo is None:
            return parsed.replace(tzinfo=timezone.utc)

        return parsed.astimezone(timezone.utc)

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
