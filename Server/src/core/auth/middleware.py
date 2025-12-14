"""FastMCP auth middleware and HTTP guard factory."""

from __future__ import annotations

import logging
import os
from typing import Callable, Type

from fastmcp.server.dependencies import get_http_request
from fastmcp.server.middleware import Middleware, MiddlewareContext
from starlette.middleware.base import BaseHTTPMiddleware
from starlette.responses import JSONResponse

from .guard import AuthGuard, unauthorized_response
from .settings import AuthSettings

logger = logging.getLogger("mcp-for-unity-server")


class AuthMiddleware(Middleware):
    def __init__(self, settings: AuthSettings):
        super().__init__()
        self.settings = settings
        self._guard = AuthGuard(settings)

    def _check_request_if_present(self) -> JSONResponse | None:
        if not self.settings.enabled:
            return None
        try:
            request = get_http_request()
        except Exception:
            request = None
        if request is None:
            if os.environ.get("UNITY_MCP_TRANSPORT", "stdio").lower() == "http":
                logger.warning("HTTP auth denied: no request context available")
                return unauthorized_response("Missing request context")
            return None
        return self._guard.check_http(request)

    async def on_request(self, context: MiddlewareContext, call_next):
        failure = self._check_request_if_present()
        if failure is not None:
            return failure
        return await call_next(context)

    async def on_call_tool(self, context: MiddlewareContext, call_next):
        failure = self._check_request_if_present()
        if failure is not None:
            return failure
        return await call_next(context)

    async def on_read_resource(self, context: MiddlewareContext, call_next):
        failure = self._check_request_if_present()
        if failure is not None:
            return failure
        return await call_next(context)


def build_http_auth_guard(settings: AuthSettings, path_prefix: str = "/mcp") -> Type[BaseHTTPMiddleware]:
    """Create a Starlette HTTP middleware class that enforces auth for a prefix."""

    guard = AuthGuard(settings)

    class HttpAuthGuard(BaseHTTPMiddleware):
        async def dispatch(self, request, call_next):
            if request.url.path.startswith(path_prefix):
                failure = guard.check_http(request)
                if failure is not None:
                    return failure
            return await call_next(request)

    return HttpAuthGuard
