"""Authentication and network access controls for the MCP server."""

from __future__ import annotations

import ipaddress
import os
from dataclasses import dataclass
from typing import Iterable, Sequence

from fastmcp.server.middleware import Middleware, MiddlewareContext
from fastmcp.server.dependencies import get_http_request
from starlette.requests import Request
from starlette.responses import JSONResponse
from starlette.websockets import WebSocket


@dataclass
class AuthSettings:
    enabled: bool = False
    allowed_ips: list[str] | None = None
    token: str | None = None

    @property
    def normalized_allowed_ips(self) -> list[str]:
        if self.allowed_ips:
            return [ip.strip() for ip in self.allowed_ips if ip and ip.strip()]
        return ["*"]

    @classmethod
    def from_env_and_args(
        cls,
        *,
        args_enabled: bool | None = None,
        args_allowed_ips: Sequence[str] | None = None,
        args_token: str | None = None,
    ) -> "AuthSettings":
        env_enabled = os.environ.get("UNITY_MCP_AUTH_ENABLED", "").lower() in (
            "1",
            "true",
            "yes",
            "on",
        )
        enabled = bool(args_enabled) or env_enabled

        env_allowed = os.environ.get("UNITY_MCP_ALLOWED_IPS")
        allowed_ips: list[str] | None = None
        if args_allowed_ips:
            allowed_ips = list(args_allowed_ips)
        elif env_allowed:
            allowed_ips = [p.strip() for p in env_allowed.split(",") if p.strip()]

        token = args_token or os.environ.get("UNITY_MCP_AUTH_TOKEN") or None
        return cls(enabled=enabled, allowed_ips=allowed_ips, token=token or None)


def _ip_in_allowlist(client_ip: str | None, allowed: Iterable[str]) -> bool:
    if client_ip is None:
        return False
    try:
        ip = ipaddress.ip_address(client_ip)
    except ValueError:
        return False

    for pattern in allowed:
        pat = pattern.strip()
        if pat == "*":
            return True
        try:
            net = ipaddress.ip_network(pat, strict=False)
            if ip in net:
                return True
        except ValueError:
            # Exact IP fall back
            try:
                if ip == ipaddress.ip_address(pat):
                    return True
            except ValueError:
                continue
    return False


def _extract_bearer_token(headers: dict[str, str]) -> str | None:
    auth_header = headers.get("authorization") or headers.get("Authorization")
    if not auth_header:
        return None
    if not auth_header.lower().startswith("bearer "):
        return None
    return auth_header[7:].strip() or None


def _unauthorized_response(message: str, status_code: int = 401) -> JSONResponse:
    return JSONResponse({"success": False, "error": "unauthorized", "message": message}, status_code=status_code)


def verify_http_request(request: Request, settings: AuthSettings) -> JSONResponse | None:
    if not settings.enabled:
        return None

    client_ip = request.client.host if request.client else None
    if not _ip_in_allowlist(client_ip, settings.normalized_allowed_ips):
        return _unauthorized_response("IP not allowed", status_code=403)

    if settings.token:
        bearer = _extract_bearer_token(request.headers)
        if bearer != settings.token:
            return _unauthorized_response("Missing or invalid bearer token", status_code=401)

    return None


async def verify_websocket(websocket: WebSocket, settings: AuthSettings) -> JSONResponse | None:
    if not settings.enabled:
        return None

    client_ip = websocket.client.host if websocket.client else None
    if not _ip_in_allowlist(client_ip, settings.normalized_allowed_ips):
        return _unauthorized_response("IP not allowed", status_code=403)

    if settings.token:
        bearer = _extract_bearer_token(dict(websocket.headers))
        if bearer != settings.token:
            return _unauthorized_response("Missing or invalid bearer token", status_code=401)

    return None


class AuthMiddleware(Middleware):
    """Enforces optional IP allowlist and bearer token on MCP requests."""

    def __init__(self, settings: AuthSettings):
        super().__init__()
        self.settings = settings

    def _check_request_if_present(self) -> JSONResponse | None:
        try:
            request = get_http_request()
        except Exception:
            return None
        if request is None:
            return None
        return verify_http_request(request, self.settings)

    async def on_request(self, context: MiddlewareContext, call_next):
        failure = self._check_request_if_present()
        if failure is not None:
            return failure
        return await call_next(context)

    async def on_call_tool(self, context: MiddlewareContext, call_next):
        # Defense-in-depth: enforce token again at tool dispatch
        if self.settings.enabled and self.settings.token:
            failure = self._check_request_if_present()
            if failure is not None:
                return failure
        return await call_next(context)

    async def on_read_resource(self, context: MiddlewareContext, call_next):
        if self.settings.enabled and self.settings.token:
            failure = self._check_request_if_present()
            if failure is not None:
                return failure
        return await call_next(context)
