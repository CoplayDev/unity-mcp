"""Auth guards shared by HTTP and WebSocket surfaces."""

from __future__ import annotations

import ipaddress
import logging
from typing import Iterable, Mapping

from starlette.requests import Request
from starlette.responses import JSONResponse
from starlette.websockets import WebSocket

from .settings import AuthSettings

logger = logging.getLogger("mcp-for-unity-server")


def _ip_in_allowlist(ip: str | None, allowed: Iterable[str]) -> bool:
    if ip is None:
        return False
    try:
        candidate = ipaddress.ip_address(ip)
    except ValueError:
        return False

    for pattern in allowed:
        pat = pattern.strip()
        if pat == "*":
            return True
        try:
            net = ipaddress.ip_network(pat, strict=False)
            if candidate in net:
                return True
        except ValueError:
            try:
                if candidate == ipaddress.ip_address(pat):
                    return True
            except ValueError:
                continue
    return False


def _extract_token(headers: Mapping[str, str]) -> str | None:
    api_key = headers.get("x-api-key") or headers.get("X-API-Key")
    if api_key:
        return api_key.strip()
    return None


def unauthorized_response(message: str, status_code: int = 401, error: str = "invalid_token") -> JSONResponse:
    return JSONResponse(
        {"success": False, "error": "unauthorized", "message": message},
        status_code=status_code,
    )


class AuthGuard:
    def __init__(self, settings: AuthSettings) -> None:
        self.settings = settings

    def check_http(self, request: Request) -> JSONResponse | None:
        if not self.settings.enabled:
            return None
        client_ip = request.client.host if request.client else None
        return self._evaluate(client_ip, request.headers)

    async def check_ws(self, websocket: WebSocket) -> JSONResponse | None:
        if not self.settings.enabled:
            return None
        client_ip = websocket.client.host if websocket.client else None
        headers = dict(websocket.headers)
        return self._evaluate(client_ip, headers)

    def _evaluate(self, client_ip: str | None, headers: Mapping[str, str]) -> JSONResponse | None:
        logger.debug(
            "Auth context: ip=%s api_key_header=%s",
            client_ip or "unknown",
            "present" if headers.get("x-api-key") or headers.get("X-API-Key") else "absent",
        )

        if not _ip_in_allowlist(client_ip, self.settings.normalized_allowed_ips):
            logger.warning("Auth denied: IP not allowed (%s)", client_ip)
            return unauthorized_response("IP not allowed", status_code=403)

        if not self.settings.token_required:
            return None

        provided = _extract_token(headers) or ""
        if provided != (self.settings.token or ""):
            masked_expected = (self.settings.token or "")[:4] + "***"
            masked_provided = provided[:4] + "***" if provided else "none"
            logger.warning(
                "Auth denied: missing or invalid token from %s (provided=%s expected=%s)",
                client_ip,
                masked_provided,
                masked_expected,
            )
            status = 401 if not provided else 403
            return unauthorized_response(
                "Missing or invalid API key",
                status_code=status,
                error="invalid_token" if status == 401 else "insufficient_scope",
            )

        return None


def verify_http_request(request: Request, settings: AuthSettings) -> JSONResponse | None:
    return AuthGuard(settings).check_http(request)


async def verify_websocket(websocket: WebSocket, settings: AuthSettings) -> JSONResponse | None:
    return await AuthGuard(settings).check_ws(websocket)
