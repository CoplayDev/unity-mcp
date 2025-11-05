"""
MCP Tools package - Auto-discovers and registers all tools in this directory.
"""
import logging
from pathlib import Path
from typing import Any, Awaitable, Callable, TypeVar

from fastmcp import Context, FastMCP
from telemetry_decorator import telemetry_tool

from registry import get_registered_tools
from module_discovery import discover_modules

logger = logging.getLogger("mcp-for-unity-server")

# Export decorator and helpers for easy imports within tools
__all__ = [
    "register_all_tools",
    "get_unity_instance_from_context",
    "send_with_unity_instance",
    "async_send_with_unity_instance",
    "with_unity_instance",
]

T = TypeVar("T")


def register_all_tools(mcp: FastMCP):
    """
    Auto-discover and register all tools in the tools/ directory.

    Any .py file in this directory or subdirectories with @mcp_for_unity_tool decorated
    functions will be automatically registered.
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

        # Apply the @mcp.tool decorator and telemetry
        wrapped = telemetry_tool(tool_name)(func)
        wrapped = mcp.tool(
            name=tool_name, description=description, **kwargs)(wrapped)
        tool_info['func'] = wrapped
        logger.debug(f"Registered tool: {tool_name} - {description}")

    logger.info(f"Registered {len(tools)} MCP tools")


def get_unity_instance_from_context(
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
            return get_state_fn(key)
        except Exception:  # pragma: no cover - defensive
            pass

    return None


def send_with_unity_instance(
    send_fn: Callable[..., T],
    unity_instance: str | None,
    *args,
    **kwargs,
) -> T:
    """Call a transport function, attaching instance_id only when provided."""

    if unity_instance:
        kwargs.setdefault("instance_id", unity_instance)
    return send_fn(*args, **kwargs)


async def async_send_with_unity_instance(
    send_fn: Callable[..., Awaitable[T]],
    unity_instance: str | None,
    *args,
    **kwargs,
) -> T:
    """Async variant of send_with_unity_instance."""

    if unity_instance:
        kwargs.setdefault("instance_id", unity_instance)
    return await send_fn(*args, **kwargs)


def with_unity_instance(
    log: str | Callable[[Context, tuple, dict, str | None], str] | None = None,
    *,
    kwarg_name: str = "unity_instance",
):
    """Decorator to extract unity_instance, perform standard logging, and pass the
    instance to the wrapped tool via kwarg.

    - log: a format string (using `{unity_instance}`) or a callable returning a message.
    - kwarg_name: name of the kwarg to inject (default: "unity_instance").
    """

    def _decorate(fn: Callable[..., T]):
        import asyncio
        import inspect
        is_coro = asyncio.iscoroutinefunction(fn)

        def _compose_message(ctx: Context, a: tuple, k: dict, inst: str | None) -> str | None:
            if log is None:
                return None
            if callable(log):
                try:
                    return log(ctx, a, k, inst)
                except Exception:
                    return None
            try:
                return str(log).format(unity_instance=inst or "default")
            except Exception:
                return str(log)

        if is_coro:
            async def _wrapper(ctx: Context, *args, **kwargs):
                inst = get_unity_instance_from_context(ctx)
                msg = _compose_message(ctx, args, kwargs, inst)
                if msg:
                    try:
                        result = ctx.info(msg)
                        if inspect.isawaitable(result):
                            await result
                    except Exception:
                        pass
                kwargs.setdefault(kwarg_name, inst)
                return await fn(ctx, *args, **kwargs)
        else:
            def _wrapper(ctx: Context, *args, **kwargs):
                inst = get_unity_instance_from_context(ctx)
                msg = _compose_message(ctx, args, kwargs, inst)
                if msg:
                    try:
                        result = ctx.info(msg)
                        if inspect.isawaitable(result):
                            try:
                                loop = asyncio.get_running_loop()
                                loop.create_task(result)
                            except RuntimeError:
                                pass
                    except Exception:
                        pass
                kwargs.setdefault(kwarg_name, inst)
                return fn(ctx, *args, **kwargs)

        from functools import wraps
        return wraps(fn)(_wrapper)  # type: ignore[arg-type]

    return _decorate
