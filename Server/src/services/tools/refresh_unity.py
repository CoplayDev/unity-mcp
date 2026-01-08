from __future__ import annotations

import asyncio
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
    response = await unity_transport.send_with_unity_instance(
        async_send_command_with_retry,
        unity_instance,
        "refresh_unity",
        params,
    )

    # Option A: treat disconnects / retry hints as recoverable when wait_for_ready is true.
    # Unity can legitimately disconnect during refresh/compile/domain reload, so callers should not
    # interpret that as a hard failure (#503-style loops).
    if isinstance(response, dict) and not response.get("success", True):
        hint = response.get("hint")
        err = (response.get("error") or response.get("message") or "")
        reason = _extract_response_reason(response)
        is_retryable = (hint == "retry") or (
            "disconnected" in str(err).lower())
        if (not wait_for_ready) or (not is_retryable):
            return MCPResponse(**response)
        if reason not in {"reloading", "no_unity_session"}:
            recovered_from_disconnect = True

    # Optional server-side wait loop (defensive): if Unity tool doesn't wait or returns quickly,
    # poll the canonical editor_state resource until ready or timeout.
    if wait_for_ready:
        timeout_s = 60.0
        start = time.monotonic()

        while time.monotonic() - start < timeout_s:
            state_resp = await editor_state.get_editor_state(ctx)
            state = state_resp.model_dump() if hasattr(
                state_resp, "model_dump") else state_resp
            data = (state or {}).get("data") if isinstance(
                state, dict) else None
            advice = (data or {}).get(
                "advice") if isinstance(data, dict) else None
            if isinstance(advice, dict) and advice.get("ready_for_tools") is True:
                break
            await asyncio.sleep(0.25)

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

    return MCPResponse(**response) if isinstance(response, dict) else response
