"""MCP tools package - auto-discovery and Unity routing helpers."""

import logging
import os
from pathlib import Path
from typing import TypeVar

from fastmcp import Context, FastMCP
from core.telemetry_decorator import telemetry_tool
from core.logging_decorator import log_execution
from utils.module_discovery import discover_modules
from services.registry import get_registered_tools, TOOL_GROUPS, DEFAULT_ENABLED_GROUPS

logger = logging.getLogger("mcp-for-unity-server")

# Export decorator and helpers for easy imports within tools
__all__ = [
    "register_all_tools",
    "sync_tool_visibility_from_unity",
    "get_unity_instance_from_context",
]


def register_all_tools(mcp: FastMCP, *, project_scoped_tools: bool = True):
    """
    Auto-discover and register all tools in the tools/ directory.

    Any .py file in this directory or subdirectories with @mcp_for_unity_tool decorated
    functions will be automatically registered.

    After registration, non-default tool groups are disabled at the server level
    so that new sessions only see the *core* tools (plus always-visible meta-tools).
    Clients can activate additional groups at any time via ``manage_tools``.
    """
    logger.info("Auto-discovering MCP for Unity Server tools...")
    # Dynamic import of all modules in this directory
    tools_dir = Path(__file__).parent

    # Discover and import all modules
    list(discover_modules(tools_dir, __package__))

    tools = get_registered_tools()

    if not tools:
        logger.warning("No MCP tools registered!")
        return

    for tool_info in tools:
        func = tool_info['func']
        tool_name = tool_info['name']
        description = tool_info['description']
        kwargs = tool_info['kwargs']

        if not project_scoped_tools and tool_name == "execute_custom_tool":
            logger.info(
                "Skipping execute_custom_tool registration (project-scoped tools disabled)")
            continue

        # Apply decorators: logging -> telemetry -> mcp.tool
        # Note: Parameter normalization (camelCase -> snake_case) is handled by
        # ParamNormalizerMiddleware before FastMCP validation
        wrapped = log_execution(tool_name, "Tool")(func)
        wrapped = telemetry_tool(tool_name)(wrapped)
        wrapped = mcp.tool(
            name=tool_name, description=description, **kwargs)(wrapped)
        tool_info['func'] = wrapped
        logger.debug(f"Registered tool: {tool_name} - {description}")

    logger.info(f"Registered {len(tools)} MCP tools")

    # In HTTP mode, disable non-default groups at the server level so new
    # sessions start lean.  Unity will re-enable groups via register_tools
    # (PluginHub._sync_server_tool_visibility) once it connects.
    # In stdio mode we skip this: the legacy TCP bridge has no register_tools
    # message, so disabled groups would stay invisible for the entire session.
    # Tools with group=None (no tag) are unaffected and always visible.
    from core.config import config as server_config

    if (server_config.transport_mode or "stdio").lower() == "http":
        groups_to_disable = set(TOOL_GROUPS.keys()) - DEFAULT_ENABLED_GROUPS
        for group_name in sorted(groups_to_disable):
            tag = f"group:{group_name}"
            mcp.disable(tags={tag}, components={"tool"})
            logger.debug(f"Disabled tool group at startup: {group_name}")
        logger.info(
            f"Default tool groups: {', '.join(sorted(DEFAULT_ENABLED_GROUPS))}. "
            f"Disabled: {', '.join(sorted(groups_to_disable))}. "
            "Use manage_tools to activate more."
        )
    else:
        logger.info(
            "Stdio transport: all tool groups enabled at startup. "
            "Will sync with Unity's tool states after connecting."
        )


async def sync_tool_visibility_from_unity(
    instance_id: str | None = None,
    notify: bool = True,
) -> dict:
    """Query Unity for tool enabled/disabled states and sync server-level visibility.

    This bridges the gap in stdio mode where Unity can't push ``register_tools``
    messages.  The Python server queries Unity's ``get_tool_states`` resource via
    the legacy TCP connection and feeds the result into
    ``PluginHub._sync_server_tool_visibility``.

    Args:
        instance_id: Optional Unity instance identifier.
        notify: If True, send ``tools/list_changed`` to connected MCP sessions.

    Returns:
        dict with sync results (enabled/disabled groups, tool count).
    """
    from transport.legacy.unity_connection import async_send_command_with_retry
    from transport.plugin_hub import PluginHub

    try:
        response = await async_send_command_with_retry(
            "get_tool_states", {}, instance_id=instance_id,
        )

        # Detect unsupported command (Unity package too old)
        if isinstance(response, dict):
            error_msg = response.get("error") or response.get("message") or ""
            if isinstance(error_msg, str) and (
                "unknown" in error_msg.lower()
                or "unsupported command" in error_msg.lower()
            ):
                logger.debug(
                    "Unity does not support get_tool_states yet — "
                    "update the MCPForUnity package to enable tool toggle sync"
                )
                return {
                    "error": "Unity package does not support get_tool_states. "
                    "Update MCPForUnity to the latest version to enable "
                    "tool toggle syncing from the Unity Editor GUI.",
                    "unsupported": True,
                }

        # Extract tool list from response
        tools = None
        if isinstance(response, dict):
            # SuccessResponse wraps data in "data" key
            data = response.get("data")
            if isinstance(data, dict):
                tools = data.get("tools")
            elif isinstance(data, list):
                tools = data
            # Fallback: maybe tools directly in response
            if tools is None:
                tools = response.get("tools")

        if not tools or not isinstance(tools, list):
            logger.debug(
                "sync_tool_visibility_from_unity: no tool data in Unity response: %s",
                response,
            )
            return {"error": "No tool data returned from Unity"}

        # Filter to enabled tools only — _sync_server_tool_visibility treats
        # the list as "registered" (i.e. enabled) tools.
        enabled_tools = [t for t in tools if t.get("enabled", True)]

        logger.info(
            "Syncing tool visibility from Unity: %d/%d tools enabled",
            len(enabled_tools), len(tools),
        )

        PluginHub._sync_server_tool_visibility(enabled_tools)

        if notify:
            await PluginHub._notify_mcp_tool_list_changed()

        # Build summary
        from services.registry import get_group_tool_names
        group_tools = get_group_tool_names()
        enabled_names = {t.get("name") for t in enabled_tools if t.get("name")}
        enabled_groups = []
        disabled_groups = []
        for group_name in sorted(TOOL_GROUPS.keys()):
            tool_names = group_tools.get(group_name, [])
            if any(n in enabled_names for n in tool_names):
                enabled_groups.append(group_name)
            else:
                disabled_groups.append(group_name)

        return {
            "synced": True,
            "enabled_groups": enabled_groups,
            "disabled_groups": disabled_groups,
            "enabled_tool_count": len(enabled_tools),
            "total_tool_count": len(tools),
        }

    except Exception as exc:
        logger.warning(
            "Failed to sync tool visibility from Unity: %s", exc,
        )
        return {"error": str(exc)}


async def get_unity_instance_from_context(
    ctx: Context,
    key: str = "unity_instance",
) -> str | None:
    """Extract the unity_instance value from middleware state.

    The instance is set via the set_active_instance tool and injected into
    request state by UnityInstanceMiddleware.
    """
    get_state_fn = getattr(ctx, "get_state", None)
    if callable(get_state_fn):
        try:
            return await get_state_fn(key)
        except Exception:  # pragma: no cover - defensive
            pass

    return None
