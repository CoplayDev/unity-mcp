import os
import time
from typing import Any

from fastmcp import Context

from models import MCPResponse
from services.registry import mcp_for_unity_resource
from services.tools import get_unity_instance_from_context
from services.state.external_changes_scanner import external_changes_scanner
import transport.unity_transport as unity_transport
from transport.legacy.unity_connection import async_send_command_with_retry


def _now_unix_ms() -> int:
    return int(time.time() * 1000)


def _in_pytest() -> bool:
    # Avoid instance-discovery side effects during the Python integration test suite.
    return bool(os.environ.get("PYTEST_CURRENT_TEST"))


async def _infer_single_instance_id(ctx: Context) -> str | None:
    """
    Best-effort: if exactly one Unity instance is connected, return its Name@hash id.
    This makes editor_state outputs self-describing even when no explicit active instance is set.
    """
    if _in_pytest():
        return None

    try:
        transport = unity_transport._current_transport()
    except Exception:
        transport = None

    if transport == "http":
        # HTTP/WebSocket transport: derive from PluginHub sessions.
        try:
            from transport.plugin_hub import PluginHub

            sessions_data = await PluginHub.get_sessions()
            sessions = sessions_data.sessions if hasattr(
                sessions_data, "sessions") else {}
            if isinstance(sessions, dict) and len(sessions) == 1:
                session = next(iter(sessions.values()))
                project = getattr(session, "project", None)
                project_hash = getattr(session, "hash", None)
                if project and project_hash:
                    return f"{project}@{project_hash}"
        except Exception:
            return None
        return None

    # Stdio/TCP transport: derive from connection pool discovery.
    try:
        from transport.legacy.unity_connection import get_unity_connection_pool

        pool = get_unity_connection_pool()
        instances = pool.discover_all_instances(force_refresh=False)
        if isinstance(instances, list) and len(instances) == 1:
            inst = instances[0]
            inst_id = getattr(inst, "id", None)
            return str(inst_id) if inst_id else None
    except Exception:
        return None
    return None


def _enrich_advice_and_staleness(state_v2: dict[str, Any]) -> dict[str, Any]:
    now_ms = _now_unix_ms()
    observed = state_v2.get("observed_at_unix_ms")
    try:
        observed_ms = int(observed)
    except Exception:
        observed_ms = now_ms

    age_ms = max(0, now_ms - observed_ms)
    # Conservative default: treat >2s as stale (covers common unfocused-editor throttling).
    is_stale = age_ms > 2000

    compilation = state_v2.get("compilation") or {}
    tests = state_v2.get("tests") or {}
    assets = state_v2.get("assets") or {}
    refresh = (assets.get("refresh") or {}) if isinstance(assets, dict) else {}

    blocking: list[str] = []
    if compilation.get("is_compiling") is True:
        blocking.append("compiling")
    if compilation.get("is_domain_reload_pending") is True:
        blocking.append("domain_reload")
    if tests.get("is_running") is True:
        blocking.append("running_tests")
    if refresh.get("is_refresh_in_progress") is True:
        blocking.append("asset_refresh")
    if is_stale:
        blocking.append("stale_status")

    ready_for_tools = len(blocking) == 0

    state_v2["advice"] = {
        "ready_for_tools": ready_for_tools,
        "blocking_reasons": blocking,
        "recommended_retry_after_ms": 0 if ready_for_tools else 500,
        "recommended_next_action": "none" if ready_for_tools else "retry_later",
    }
    state_v2["staleness"] = {"age_ms": age_ms, "is_stale": is_stale}
    return state_v2


@mcp_for_unity_resource(
    uri="unity://editor/state",
    name="editor_state",
    description="Canonical editor readiness snapshot. Includes advice and server-computed staleness.",
)
async def get_editor_state(ctx: Context) -> MCPResponse:
    unity_instance = get_unity_instance_from_context(ctx)

    response = await unity_transport.send_with_unity_instance(
        async_send_command_with_retry,
        unity_instance,
        "get_editor_state",
        {},
    )

    # If Unity returns a structured retry hint or error, surface it directly.
    if isinstance(response, dict) and not response.get("success", True):
        return MCPResponse(**response)

    state_v2 = response.get("data") if isinstance(
        response, dict) and isinstance(response.get("data"), dict) else {}
    state_v2.setdefault("schema_version", "unity-mcp/editor_state@2")
    state_v2.setdefault("observed_at_unix_ms", _now_unix_ms())
    state_v2.setdefault("sequence", 0)

    # Ensure the returned snapshot is clearly associated with the targeted instance.
    unity_section = state_v2.get("unity")
    if not isinstance(unity_section, dict):
        unity_section = {}
        state_v2["unity"] = unity_section
    current_instance_id = unity_section.get("instance_id")
    if current_instance_id in (None, ""):
        if unity_instance:
            unity_section["instance_id"] = unity_instance
        else:
            inferred = await _infer_single_instance_id(ctx)
            if inferred:
                unity_section["instance_id"] = inferred

    # External change detection (server-side): compute per instance based on project root path.
    try:
        instance_id = unity_section.get("instance_id")
        if isinstance(instance_id, str) and instance_id.strip():
            from services.resources.project_info import get_project_info

            proj_resp = await get_project_info(ctx)
            proj = proj_resp.model_dump() if hasattr(
                proj_resp, "model_dump") else proj_resp
            proj_data = proj.get("data") if isinstance(proj, dict) else None
            project_root = proj_data.get("projectRoot") if isinstance(
                proj_data, dict) else None
            if isinstance(project_root, str) and project_root.strip():
                external_changes_scanner.set_project_root(
                    instance_id, project_root)

            ext = external_changes_scanner.update_and_get(instance_id)

            assets = state_v2.get("assets")
            if not isinstance(assets, dict):
                assets = {}
                state_v2["assets"] = assets
            assets["external_changes_dirty"] = bool(
                ext.get("external_changes_dirty", False))
            assets["external_changes_last_seen_unix_ms"] = ext.get(
                "external_changes_last_seen_unix_ms")
            assets["external_changes_dirty_since_unix_ms"] = ext.get(
                "dirty_since_unix_ms")
            assets["external_changes_last_cleared_unix_ms"] = ext.get(
                "last_cleared_unix_ms")
    except Exception:
        pass

    state_v2 = _enrich_advice_and_staleness(state_v2)
    return MCPResponse(success=True, message="Retrieved editor state.", data=state_v2)
