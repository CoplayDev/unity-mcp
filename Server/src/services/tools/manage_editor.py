import time
from typing import Annotated, Any, Literal

from fastmcp import Context
from mcp.types import ToolAnnotations

from services.registry import mcp_for_unity_tool
from core.telemetry import is_telemetry_enabled, record_tool_usage
from services.tools import get_unity_instance_from_context
from transport.unity_transport import send_with_unity_instance
from transport.legacy.unity_connection import async_send_command_with_retry
from services.tools.utils import coerce_bool

_WAIT_FOR_COMPILATION_ACTIONS = frozenset({"wait_for_compilation"})


@mcp_for_unity_tool(
    description="Controls and queries the Unity editor's state and settings. Tip: pass booleans as true/false; if your client only sends strings, 'true'/'false' are accepted. Read-only actions: telemetry_status, telemetry_ping, wait_for_compilation. Modifying actions: play, pause, stop, set_active_tool, add_tag, remove_tag, add_layer, remove_layer.",
    annotations=ToolAnnotations(
        title="Manage Editor",
    ),
)
async def manage_editor(
    ctx: Context,
    action: Annotated[Literal["telemetry_status", "telemetry_ping", "wait_for_compilation", "play", "pause", "stop", "set_active_tool", "add_tag", "remove_tag", "add_layer", "remove_layer"], "Get and update the Unity Editor state."],
    wait_for_completion: Annotated[bool | str,
                                   "Optional. If True, waits for certain actions (accepts true/false or 'true'/'false')"] | None = None,
    timeout: Annotated[int | float | None,
                       "Timeout in seconds for wait_for_compilation (default: 30)."] = None,
    tool_name: Annotated[str,
                         "Tool name when setting active tool"] | None = None,
    tag_name: Annotated[str,
                        "Tag name when adding and removing tags"] | None = None,
    layer_name: Annotated[str,
                          "Layer name when adding and removing layers"] | None = None,
) -> dict[str, Any]:
    unity_instance = get_unity_instance_from_context(ctx)

    wait_for_completion = coerce_bool(wait_for_completion)

    try:
        if action == "telemetry_status":
            return {"success": True, "telemetry_enabled": is_telemetry_enabled()}

        if action == "telemetry_ping":
            record_tool_usage("diagnostic_ping", True, 1.0, None)
            return {"success": True, "message": "telemetry ping queued"}

        if action == "wait_for_compilation":
            return await _wait_for_compilation(ctx, timeout)

        params = {
            "action": action,
            "waitForCompletion": wait_for_completion,
            "toolName": tool_name,
            "tagName": tag_name,
            "layerName": layer_name,
        }
        params = {k: v for k, v in params.items() if v is not None}

        response = await send_with_unity_instance(async_send_command_with_retry, unity_instance, "manage_editor", params)

        if isinstance(response, dict) and response.get("success"):
            return {"success": True, "message": response.get("message", "Editor operation successful."), "data": response.get("data")}
        return response if isinstance(response, dict) else {"success": False, "message": str(response)}

    except Exception as e:
        return {"success": False, "message": f"Python error managing editor: {str(e)}"}


async def _wait_for_compilation(ctx: Context, timeout: int | float | None) -> dict[str, Any]:
    """Poll editor_state until compilation and domain reload finish."""
    from services.tools.refresh_unity import wait_for_editor_ready

    timeout_s = float(timeout) if timeout is not None else 30.0
    timeout_s = max(1.0, min(timeout_s, 120.0))

    start = time.monotonic()
    ready, elapsed = await wait_for_editor_ready(ctx, timeout_s=timeout_s)

    if ready:
        return {
            "success": True,
            "message": "Compilation complete. Editor is ready.",
            "data": {
                "waited_seconds": round(elapsed, 2),
                "ready": True,
            },
        }

    return {
        "success": False,
        "message": f"Timed out after {timeout_s:.0f}s waiting for compilation to finish.",
        "data": {
            "waited_seconds": round(elapsed, 2),
            "ready": False,
            "timeout_seconds": timeout_s,
        },
    }
