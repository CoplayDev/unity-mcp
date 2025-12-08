"""Authentication utilities for MCP for Unity.

This package centralizes auth settings, guards, and middleware to keep
the auth surface consistent across HTTP, WebSocket, and FastMCP tool
invocations.
"""

from .settings import (
    AuthSettings,
    DEFAULT_ALLOWED_IPS,
    build_auth_settings,
    get_api_key_path,
    load_or_create_api_key,
)
from .guard import AuthGuard, verify_http_request, verify_websocket, unauthorized_response
from .middleware import AuthMiddleware, build_http_auth_guard


# Snake-case convenience wrappers to keep public API naming consistent with the rest of the codebase.
def auth_settings(*, enabled: bool = False, token: str | None = None, allowed_ips=None) -> AuthSettings:
    return build_auth_settings(enabled=enabled, token=token, allowed_ips=allowed_ips)


def auth_guard(settings: AuthSettings) -> AuthGuard:
    return AuthGuard(settings)


def auth_middleware(settings: AuthSettings) -> AuthMiddleware:
    return AuthMiddleware(settings)


def http_auth_guard(settings: AuthSettings, path_prefix: str = "/mcp"):
    return build_http_auth_guard(settings, path_prefix=path_prefix)


__all__ = [
    # Snake-case helpers used throughout the server
    "auth_settings",
    "auth_guard",
    "auth_middleware",
    "http_auth_guard",
    "verify_http_request",
    "verify_websocket",
    "unauthorized_response",
    "get_api_key_path",
    "load_or_create_api_key",
    "DEFAULT_ALLOWED_IPS",
    # Class exports for typing/advanced use
    "AuthSettings",
    "AuthGuard",
    "AuthMiddleware",
    "build_auth_settings",
    "build_http_auth_guard",
]
