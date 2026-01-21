from __future__ import annotations

import asyncio
import logging
import time
from typing import Annotated, Any, Literal

from fastmcp import Context
from mcp.types import ToolAnnotations

from models import MCPResponse
from services.registry import mcp_for_unity_tool
from services.tools import get_unity_instance_from_context
import transport.unity_transport as unity_transport
from transport.legacy.unity_connection import async_send_command_with_retry, _extract_response_reason
from services.state.external_changes_scanner import external_changes_scanner
import services.resources.editor_state as editor_state

logger = logging.getLogger(__name__)


@mcp_for_unity_tool(
    description="Request a Unity asset database refresh and optionally a script compilation. Can optionally wait for readiness.",
    annotations=ToolAnnotations(
        title="Refresh Unity",
        destructiveHint=True,
    ),
)
async def refresh_unity(
    ctx: Context,
    mode: Annotated[Literal["if_dirty", "force"], "Refresh mode"] = "if_dirty",
    scope: Annotated[Literal["assets", "scripts", "all"],
                     "Refresh scope"] = "all",
    compile: Annotated[Literal["none", "request"],
                       "Whether to request compilation"] = "none",
    wait_for_ready: Annotated[bool,
                              "If true, wait until editor_state.advice.ready_for_tools is true"] = True,
) -> MCPResponse | dict[str, Any]:
    unity_instance = get_unity_instance_from_context(ctx)

    params: dict[str, Any] = {
        "mode": mode,
        "scope": scope,
        "compile": compile,
        "wait_for_ready": bool(wait_for_ready),
    }

    recovered_from_disconnect = False
    # Don't retry on reload - refresh_unity triggers compilation/reload,
    # so retrying would cause multiple reloads (issue #577)
    response = await unity_transport.send_with_unity_instance(
        async_send_command_with_retry,
        unity_instance,
        "refresh_unity",
        params,
        retry_on_reload=False,
    )

    # Handle connection errors during refresh/compile gracefully.
    # Unity disconnects during domain reload, which is expected behavior - not a failure.
    # If we sent the command and connection closed, the refresh was likely triggered successfully.
    # Convert MCPResponse to dict if needed
    response_dict = response if isinstance(response, dict) else (response.model_dump() if hasattr(response, "model_dump") else response.__dict__)
    if not response_dict.get("success", True):
        hint = response_dict.get("hint")
        err = (response_dict.get("error") or response_dict.get("message") or "").lower()
        reason = _extract_response_reason(response_dict)

        # Connection closed/timeout during compile = refresh was triggered, Unity is reloading
        # This is SUCCESS, not failure - don't return error to prevent Claude Code from retrying
        is_connection_lost = (
            "connection closed" in err
            or "disconnected" in err
            or "aborted" in err  # WinError 10053: connection aborted
            or "timeout" in err
            or reason == "reloading"
        )

        if is_connection_lost and compile == "request":
            # EXPECTED BEHAVIOR: When compile="request", Unity triggers domain reload which
            # causes connection to close mid-command. This is NOT a failure - the refresh
            # was successfully triggered. Treating this as success prevents Claude Code from
            # retrying unnecessarily (which would cause multiple domain reloads - issue #577).
            # The subsequent wait_for_ready loop (below) will verify Unity becomes ready.
            logger.info("refresh_unity: Connection lost during compile (expected - domain reload triggered)")
            recovered_from_disconnect = True
        elif hint == "retry" or "could not connect" in err:
            # Retryable error - proceed to wait loop if wait_for_ready
            if not wait_for_ready:
                return MCPResponse(**response_dict)
            recovered_from_disconnect = True
        else:
            # Non-recoverable error - connection issue unrelated to domain reload
            logger.warning(f"refresh_unity: Non-recoverable error (compile={compile}): {err[:100]}")
            return MCPResponse(**response_dict)

    # Optional server-side wait loop (defensive): if Unity tool doesn't wait or returns quickly,
    # poll the canonical editor_state resource until ready or timeout.
    ready_confirmed = False
    if wait_for_ready:
        timeout_s = 60.0
        start = time.monotonic()

        # Blocking reasons that indicate Unity is actually busy (not just stale status)
        # Must match activityPhase values from EditorStateCache.cs
        real_blocking_reasons = {"compiling", "domain_reload", "running_tests", "asset_import"}

        while time.monotonic() - start < timeout_s:
            state_resp = await editor_state.get_editor_state(ctx)
            state = state_resp.model_dump() if hasattr(
                state_resp, "model_dump") else state_resp
            data = (state or {}).get("data") if isinstance(
                state, dict) else None
            advice = (data or {}).get(
                "advice") if isinstance(data, dict) else None
            if isinstance(advice, dict):
                # Exit if ready_for_tools is True
                if advice.get("ready_for_tools") is True:
                    ready_confirmed = True
                    break
                # Also exit if the only blocking reason is "stale_status" (Unity in background)
                # Staleness means we can't confirm status, not that Unity is actually busy
                blocking = set(advice.get("blocking_reasons") or [])
                if not (blocking & real_blocking_reasons):
                    ready_confirmed = True  # No real blocking reasons, consider ready
                    break
            await asyncio.sleep(0.25)

        # If we timed out without confirming readiness, log and return failure
        if not ready_confirmed:
            logger.warning(f"refresh_unity: Timed out after {timeout_s}s waiting for editor to become ready")
            return MCPResponse(
                success=False,
                message=f"Refresh triggered but timed out after {timeout_s}s waiting for editor readiness.",
                data={"timeout": True, "wait_seconds": timeout_s},
            )

    # After readiness is restored, clear any external-dirty flag for this instance so future tools can proceed cleanly.
    try:
        inst = unity_instance or await editor_state.infer_single_instance_id(ctx)
        if inst:
            external_changes_scanner.clear_dirty(inst)
    except Exception:
        pass

    if recovered_from_disconnect:
        return MCPResponse(
            success=True,
            message="Refresh recovered after Unity disconnect/retry; editor is ready.",
            data={"recovered_from_disconnect": True},
        )

    return MCPResponse(**response_dict) if isinstance(response, dict) else response
