from typing import Any
import os
import sys

from core.telemetry import get_package_version

from fastmcp import Context
from mcp.types import ToolAnnotations

from services.registry import mcp_for_unity_tool
from transport.unity_instance_middleware import get_unity_instance_middleware
from transport.plugin_hub import PluginHub

_SECRET_FLAG_MARKERS = ("token", "secret", "password", "api-key", "api_key", "apikey")


def _redact_argv(argv: list[str]) -> list[str]:
    """Keep flag names for diagnosis, hide the values of secret-bearing ones.

    A remote-hosted server is started with --api-key-service-token on its command line and
    this tool is callable by every authenticated tenant, so the raw argv handed out the
    service credential. The flag shape is what helps debug a deployment; the value never is.
    """
    # Every secret-bearing flag the server accepts takes a value, so the token after a bare
    # flag is always that value, even when it happens to start with "-".
    out: list[str] = []
    hide_next = False
    for arg in argv:
        if hide_next:
            out.append("***")
            hide_next = False
            continue
        name, sep, _value = arg.partition("=")
        secret = name.startswith("-") and any(m in name.lower() for m in _SECRET_FLAG_MARKERS)
        out.append(f"{name}=***" if secret and sep else arg)
        hide_next = secret and not sep
    return out


@mcp_for_unity_tool(
    unity_target=None,
    group=None,
    description="Return the current FastMCP request context details (client_id, session_id, and meta dump).",
    annotations=ToolAnnotations(
        title="Debug Request Context",
        readOnlyHint=True,
        destructiveHint=False,
        idempotentHint=True,
        openWorldHint=False,
    ),
)
async def debug_request_context(ctx: Context) -> dict[str, Any]:
    # Check request_context properties
    rc = getattr(ctx, "request_context", None)
    rc_client_id = getattr(rc, "client_id", None)
    rc_session_id = getattr(rc, "session_id", None)
    meta = getattr(rc, "meta", None)

    # Check direct ctx properties (per latest FastMCP docs)
    ctx_session_id = getattr(ctx, "session_id", None)
    ctx_client_id = getattr(ctx, "client_id", None)

    meta_dump = None
    if meta is not None:
        try:
            dump_fn = getattr(meta, "model_dump", None)
            if callable(dump_fn):
                meta_dump = dump_fn(exclude_none=False)
            elif isinstance(meta, dict):
                meta_dump = dict(meta)
        except Exception as e:
            meta_dump = {"_error": str(e)}

    # List all ctx attributes for debugging
    ctx_attrs = [attr for attr in dir(ctx) if not attr.startswith("_")]

    # Get session state info via middleware. Active-instance storage now lives
    # in FastMCP's session-scoped state store, keyed by ctx.session_id, so
    # there is no global dict to enumerate — that snapshot was a footgun
    # anyway (it exposed every connected client's selection).
    middleware = get_unity_instance_middleware()
    active_instance = await middleware.get_active_instance(ctx)

    # Debugging PluginHub state
    plugin_hub_configured = PluginHub.is_configured()

    return {
        "success": True,
        "data": {
            "server": {
                "version": get_package_version(),
                "cwd": os.getcwd(),
                "argv": _redact_argv(sys.argv),
            },
            "request_context": {
                "client_id": rc_client_id,
                "session_id": rc_session_id,
                "meta": meta_dump,
            },
            "direct_properties": {
                "session_id": ctx_session_id,
                "client_id": ctx_client_id,
            },
            "session_state": {
                "active_instance": active_instance,
                "plugin_hub_configured": plugin_hub_configured,
                "middleware_id": id(middleware),
            },
            "available_attributes": ctx_attrs,
        },
    }
