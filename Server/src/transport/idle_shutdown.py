"""Stop a Unity-launched HTTP server once no Unity Editor has been connected for a while.

A server started by an Editor is stopped by that Editor when it quits as the last one
connected. If the launching Editor quits first, the server stays up for the others, and
when they quit too nothing is left that can stop it: only the launcher holds its pidfile
and instance token. This watcher lets such a server exit on its own instead.
"""

from __future__ import annotations

import asyncio
import logging
import os
import signal
import time
from typing import Callable

from core.config import config
from transport.plugin_registry import PluginRegistry

logger = logging.getLogger(__name__)

# Long enough to ride out domain reloads, an Editor restart and the reconnect gaps of an
# Editor whose main thread is busy; an idle server lingering a little longer costs nothing.
IDLE_SHUTDOWN_SECONDS = 600.0
CHECK_INTERVAL_SECONDS = 15.0

# Set once the watcher has asked the server to stop, so main() can tell this shutdown
# from a Ctrl+C.
requested = False


def is_enabled() -> bool:
    """Only a local HTTP server launched by a Unity Editor (which passes its instance
    token) stops itself; one started by hand or hosted remotely keeps running."""
    return (
        (config.transport_mode or "").lower() == "http"
        and not config.http_remote_hosted
        and bool(os.environ.get("UNITY_MCP_INSTANCE_TOKEN"))
    )


def _request_shutdown() -> None:
    global requested
    requested = True
    # The same graceful path as Ctrl+C: uvicorn stops serving and the lifespan unwinds.
    signal.raise_signal(signal.SIGINT)


async def shutdown_when_idle(
    registry: PluginRegistry,
    idle_seconds: float = IDLE_SHUTDOWN_SECONDS,
    check_interval: float = CHECK_INTERVAL_SECONDS,
    request_shutdown: Callable[[], None] = _request_shutdown,
    clock: Callable[[], float] = time.monotonic,
) -> None:
    """Request shutdown once no Editor has been connected for ``idle_seconds``,
    counting from start-up so a server no Editor ever reaches also exits.

    ``clock`` must be the clock ``registry.last_session_ended`` is stamped with."""
    last_connected = clock()
    while True:
        await asyncio.sleep(check_interval)
        if await registry.list_sessions():
            last_connected = clock()
            continue
        # A session that connected and left between two polls still resets the window.
        ended = registry.last_session_ended
        if ended is not None and ended > last_connected:
            last_connected = ended
        if clock() - last_connected >= idle_seconds:
            logger.info(
                "No Unity Editor connected for %.0f s; shutting down the server", idle_seconds)
            request_shutdown()
            return
