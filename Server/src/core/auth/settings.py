"""Auth settings and API key storage helpers."""

from __future__ import annotations

import logging
import os
import secrets
import sys
from dataclasses import dataclass, field
from pathlib import Path
from typing import Sequence

logger = logging.getLogger("mcp-for-unity-server")

DEFAULT_ALLOWED_IPS: list[str] = ["127.0.0.1/32", "::1/128"]


@dataclass
class AuthSettings:
    enabled: bool = False
    allowed_ips: list[str] = field(default_factory=lambda: list(DEFAULT_ALLOWED_IPS))
    token: str | None = None

    @property
    def normalized_allowed_ips(self) -> list[str]:
        return [ip.strip() for ip in self.allowed_ips if ip and ip.strip()] or list(DEFAULT_ALLOWED_IPS)

    @property
    def token_required(self) -> bool:
        return self.enabled and bool(self.token)


def build_auth_settings(
    *,
    enabled: bool = False,
    token: str | None = None,
    allowed_ips: Sequence[str] | None = None,
) -> AuthSettings:
    resolved_allowed = list(allowed_ips) if allowed_ips else list(DEFAULT_ALLOWED_IPS)
    # When auth is enabled, always require a token. Treat empty string as unset.
    normalized_token = None
    if token is not None and str(token).strip() != "":
        normalized_token = token
    resolved_token = normalized_token if normalized_token is not None else (load_or_create_api_key() if enabled else None)
    return AuthSettings(enabled=enabled, allowed_ips=resolved_allowed, token=resolved_token)


def get_api_key_path() -> Path:
    if home := os.environ.get("UNITY_MCP_HOME"):
        return Path(home) / "api_key"

    if sys.platform == "win32":
        base = Path(os.environ.get("LOCALAPPDATA", Path.home() / "AppData/Local"))
        return base / "UnityMCP" / "api_key"

    if sys.platform == "darwin":
        return Path.home() / "Library" / "Application Support" / "UnityMCP" / "api_key"

    return Path.home() / ".local" / "share" / "UnityMCP" / "api_key"


def load_or_create_api_key() -> str:
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
        # Best-effort hardening on POSIX systems.
        try:
            if os.name == "posix":
                os.chmod(path, 0o600)
        except Exception:
            logger.debug("Failed to set API key file permissions", exc_info=True)
        logger.info("Generated and persisted new API key to %s", path)
    except Exception:
        logger.warning("Failed to persist API key to %s", path, exc_info=True)

    return token
