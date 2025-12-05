"""Authentication and network access controls for the MCP server."""

from __future__ import annotations

import ipaddress
import logging
import os
import secrets
import sys
from dataclasses import dataclass, field
from pathlib import Path
from typing import Iterable, Sequence

from fastmcp.server.middleware import Middleware, MiddlewareContext
from fastmcp.server.dependencies import get_http_request
from starlette.requests import Request
from starlette.responses import JSONResponse
from starlette.websockets import WebSocket

logger = logging.getLogger("mcp-for-unity-server")


def _log_auth_context(kind: str, client_ip: str | None, has_auth_header: bool, has_api_key_header: bool) -> None:
    """Lightweight logging without leaking secrets."""
    logger.info(
        "%s auth context: ip=%s auth_header=%s api_key_header=%s",
        kind,
        client_ip or "unknown",
        "present" if has_auth_header else "absent",
        "present" if has_api_key_header else "absent",
    )


def _default_allowed_ips() -> list[str]:
    return ["*"]


@dataclass
class AuthSettings:
    allowed_ips: list[str] = field(default_factory=_default_allowed_ips)
    token: str | None = None

    def __post_init__(self) -> None:
        # Always ensure a non-empty token is present so auth cannot be disabled
        if not self.token:
            self.token = load_or_create_api_key()

    @property
    def normalized_allowed_ips(self) -> list[str]:
        return [ip.strip() for ip in self.allowed_ips if ip and ip.strip()] or ["*"]

    @classmethod
    def build(
        cls,
        *,
        token: str | None = None,
        allowed_ips: Sequence[str] | None = None,
    ) -> "AuthSettings":
        resolved_token = load_or_create_api_key(token)
        resolved_allowed = list(allowed_ips) if allowed_ips else _default_allowed_ips()
        return cls(allowed_ips=resolved_allowed, token=resolved_token)


def get_api_key_path() -> Path:
    if home := os.environ.get("UNITY_MCP_HOME"):
        return Path(home) / "api_key"

    if sys.platform == "win32":
        base = Path(os.environ.get("LOCALAPPDATA", Path.home() / "AppData/Local"))
        return base / "UnityMCP" / "api_key"

    if sys.platform == "darwin":
        return Path.home() / "Library" / "Application Support" / "UnityMCP" / "api_key"

    return Path.home() / ".local" / "share" / "UnityMCP" / "api_key"


def load_or_create_api_key(preferred: str | None = None) -> str:
    if preferred:
        logger.info("Using API key provided via CLI flag --api-key")
        return preferred

    path = get_api_key_path()
    try:
        if path.exists():
            existing = path.read_text(encoding="utf-8").strip()
            if existing:
                logger.info("Loaded API key from %s", path)
                return existing
    except Exception:
        logger.debug("Failed to read API key file at %s", path, exc_info=True)

    token = secrets.token_urlsafe(32)
    try:
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(token, encoding="utf-8")
        logger.info("Generated and persisted new API key to %s", path)
    except Exception:
        logger.warning("Failed to persist API key to %s", path, exc_info=True)

    return token


def _ip_in_allowlist(ip: str | None, allowed: Iterable[str]) -> bool:
    if ip is None:
        return False
    try:
        ip = ipaddress.ip_address(ip)
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
            try:
                if ip == ipaddress.ip_address(pat):
                    return True
            except ValueError:
                continue
    return False


def _extract_api_key(headers: dict[str, str]) -> str | None:
    auth_header = headers.get("authorization") or headers.get("Authorization")
    if auth_header and auth_header.lower().startswith("bearer "):
        token = auth_header[7:].strip()
        if token:
            return token

    api_key = headers.get("x-api-key") or headers.get("X-API-Key")
    if api_key:
        return api_key.strip()
    return None


def _unauthorized_response(message: str, status_code: int = 401) -> JSONResponse:
    return JSONResponse({"success": False, "error": "unauthorized", "message": message}, status_code=status_code)


def verify_http_request(request: Request, settings: AuthSettings) -> JSONResponse | None:
    client_ip = request.client.host if request.client else None
    _log_auth_context(
        "HTTP",
        client_ip,
        has_auth_header=bool(request.headers.get("authorization") or request.headers.get("Authorization")),
        has_api_key_header=bool(request.headers.get("x-api-key") or request.headers.get("X-API-Key")),
    )
    if not _ip_in_allowlist(client_ip, settings.normalized_allowed_ips):
        logger.warning("HTTP auth denied: IP not allowed (%s)", client_ip)
        return _unauthorized_response("IP not allowed", status_code=403)

    api_key = _extract_api_key(request.headers)
    if api_key != settings.token:
        provided = (api_key or "").strip()
        logger.warning(
            "HTTP auth denied: missing or invalid API key from %s (provided=%s expected=%s)",
            client_ip,
            provided[:4] + "***" if provided else "none",
            (settings.token or "")[:4] + "***",
        )
        return _unauthorized_response("Missing or invalid API key", status_code=401)

    logger.debug("HTTP auth accepted for %s", client_ip)
    return None


async def verify_websocket(websocket: WebSocket, settings: AuthSettings) -> JSONResponse | None:
    client_ip = websocket.client.host if websocket.client else None
    headers = dict(websocket.headers)
    _log_auth_context(
        "WS",
        client_ip,
        has_auth_header=bool(headers.get("authorization") or headers.get("Authorization")),
        has_api_key_header=bool(headers.get("x-api-key") or headers.get("X-API-Key")),
    )
    if not _ip_in_allowlist(client_ip, settings.normalized_allowed_ips):
        logger.warning("WS auth denied: IP not allowed (%s)", client_ip)
        return _unauthorized_response("IP not allowed", status_code=403)

    api_key = _extract_api_key(headers)
    if api_key != settings.token:
        provided = (api_key or "").strip()
        logger.warning(
            "WS auth denied: missing or invalid API key from %s (provided=%s expected=%s)",
            client_ip,
            provided[:4] + "***" if provided else "none",
            (settings.token or "")[:4] + "***",
        )
        return _unauthorized_response("Missing or invalid API key", status_code=401)

    logger.debug("WS auth accepted for %s", client_ip)
    return None


class AuthMiddleware(Middleware):
    """Enforces IP allowlist and API key on MCP requests."""

    def __init__(self, settings: AuthSettings):
        super().__init__()
        self.settings = settings

    def _check_request_if_present(self) -> JSONResponse | None:
        try:
            request = get_http_request()
        except Exception:
            request = None
        if request is None:
            # If we're serving HTTP transport, absence of a request means we cannot authenticate
            if os.environ.get("UNITY_MCP_TRANSPORT", "stdio").lower() == "http":
                logger.warning("HTTP auth denied: no request context available")
                return _unauthorized_response("Missing request context")
            return None
        result = verify_http_request(request, self.settings)
        if result is None:
            logger.info("AuthMiddleware: request authorized")
        else:
            logger.info("AuthMiddleware: request rejected with status %s", result.status_code)
        return result

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
