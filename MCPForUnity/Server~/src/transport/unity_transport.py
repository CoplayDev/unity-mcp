"""Transport helpers for routing commands to Unity."""
from __future__ import annotations

import asyncio
import inspect
import os
from typing import Awaitable, Callable, TypeVar

from fastmcp import Context

from transport.plugin_hub import PluginHub
from models.unity_response import normalize_unity_response
from services.tools import get_unity_instance_from_context

T = TypeVar("T")


def _is_http_transport() -> bool:
    return os.environ.get("UNITY_MCP_TRANSPORT", "stdio").lower() == "http"


def _current_transport() -> str:
    """Expose the active transport mode as a simple string identifier."""
    return "http" if _is_http_transport() else "stdio"


def with_unity_instance(
    log: str | Callable[[Context, tuple, dict, str | None], str] | None = None,
    *,
    kwarg_name: str = "unity_instance",
):
    def _decorate(fn: Callable[..., T]):
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
                        await ctx.info(msg)
                    except Exception:
                        pass
                kwargs.setdefault(kwarg_name, inst)
                return await fn(ctx, *args, **kwargs)
        else:
            async def _wrapper(ctx: Context, *args, **kwargs):
                inst = get_unity_instance_from_context(ctx)
                msg = _compose_message(ctx, args, kwargs, inst)
                if msg:
                    try:
                        await ctx.info(msg)
                    except Exception:
                        pass
                kwargs.setdefault(kwarg_name, inst)
                return fn(ctx, *args, **kwargs)

        from functools import wraps

        return wraps(fn)(_wrapper)  # type: ignore[arg-type]

    return _decorate


async def send_with_unity_instance(
    send_fn: Callable[..., Awaitable[T]],
    unity_instance: str | None,
    *args,
    **kwargs,
) -> T:
    if _is_http_transport():
        if not args:
            raise ValueError("HTTP transport requires command arguments")
        command_type = args[0]
        params = args[1] if len(args) > 1 else kwargs.get("params")
        if params is None:
            params = {}
        if not isinstance(params, dict):
            raise TypeError(
                "Command parameters must be a dict for HTTP transport")
        raw = await PluginHub.send_command_for_instance(
            unity_instance,
            command_type,
            params,
        )
        return normalize_unity_response(raw)

    if unity_instance:
        kwargs.setdefault("instance_id", unity_instance)
    return await send_fn(*args, **kwargs)
