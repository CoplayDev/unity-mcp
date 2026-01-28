"""API Key validation service for remote-hosted mode."""

from __future__ import annotations

import asyncio
import logging
import time
from dataclasses import dataclass
from typing import Any

import httpx

logger = logging.getLogger("mcp-for-unity-server")


@dataclass
class ValidationResult:
    """Result of an API key validation."""
    valid: bool
    user_id: str | None = None
    metadata: dict[str, Any] | None = None
    error: str | None = None
    cacheable: bool = True


class ApiKeyService:
    """Service for validating API keys against an external auth endpoint.

    Follows the class-level singleton pattern for global access by MCP tools.
    """

    _instance: "ApiKeyService | None" = None

    # Request defaults (sensible hardening)
    REQUEST_TIMEOUT: float = 5.0
    MAX_RETRIES: int = 1

    def __init__(
        self,
        validation_url: str,
        cache_ttl: float = 300.0,
        service_token_header: str | None = None,
        service_token: str | None = None,
    ):
        """Initialize the API key service.

        Args:
            validation_url: External URL to validate API keys (POST with {"api_key": "..."})
            cache_ttl: Cache TTL for validated keys in seconds (default: 300)
            service_token_header: Optional header name for service authentication (e.g. "X-Service-Token")
            service_token: Optional token value for service authentication
        """
        self._validation_url = validation_url
        self._cache_ttl = cache_ttl
        self._service_token_header = service_token_header
        self._service_token = service_token
        # Cache: api_key -> (valid, user_id, metadata, expires_at)
        self._cache: dict[str, tuple[bool, str |
                                     None, dict[str, Any] | None, float]] = {}
        self._cache_lock = asyncio.Lock()
        ApiKeyService._instance = self

    @classmethod
    def get_instance(cls) -> "ApiKeyService":
        """Get the singleton instance.

        Raises:
            RuntimeError: If the service has not been initialized.
        """
        if cls._instance is None:
            raise RuntimeError("ApiKeyService not initialized")
        return cls._instance

    @classmethod
    def is_initialized(cls) -> bool:
        """Check if the service has been initialized."""
        return cls._instance is not None

    async def validate(self, api_key: str) -> ValidationResult:
        """Validate an API key.

        Returns:
            ValidationResult with valid=True and user_id if valid,
            or valid=False with error message if invalid.
        """
        if not api_key:
            return ValidationResult(valid=False, error="API key required")

        # Check cache first
        async with self._cache_lock:
            cached = self._cache.get(api_key)
            if cached is not None:
                valid, user_id, metadata, expires_at = cached
                if time.time() < expires_at:
                    if valid:
                        return ValidationResult(valid=True, user_id=user_id, metadata=metadata)
                    else:
                        return ValidationResult(valid=False, error="Invalid API key")
                else:
                    # Expired, remove from cache
                    del self._cache[api_key]

        # Call external validation URL
        result = await self._validate_external(api_key)

        # Only cache definitive results (valid keys and confirmed-invalid keys).
        # Transient failures (auth service unavailable, timeouts, etc.) should
        # not be cached to avoid locking out users during service outages.
        if result.cacheable:
            async with self._cache_lock:
                expires_at = time.time() + self._cache_ttl
                self._cache[api_key] = (
                    result.valid,
                    result.user_id,
                    result.metadata,
                    expires_at,
                )

        return result

    async def _validate_external(self, api_key: str) -> ValidationResult:
        """Call external validation endpoint.

        Failure mode: fail closed (treat as invalid on errors).
        """
        # Redact API key from logs
        redacted_key = f"{api_key[:4]}...{api_key[-4:]}" if len(
            api_key) > 8 else "***"

        for attempt in range(self.MAX_RETRIES + 1):
            try:
                async with httpx.AsyncClient(timeout=self.REQUEST_TIMEOUT) as client:
                    # Build request headers
                    headers = {"Content-Type": "application/json"}
                    if self._service_token_header and self._service_token:
                        headers[self._service_token_header] = self._service_token

                    response = await client.post(
                        self._validation_url,
                        json={"api_key": api_key},
                        headers=headers,
                    )

                    if response.status_code == 200:
                        data = response.json()
                        if data.get("valid"):
                            return ValidationResult(
                                valid=True,
                                user_id=data.get("user_id"),
                                metadata=data.get("metadata"),
                            )
                        else:
                            return ValidationResult(
                                valid=False,
                                error=data.get("error", "Invalid API key"),
                            )
                    elif response.status_code == 401:
                        return ValidationResult(valid=False, error="Invalid API key")
                    else:
                        logger.warning(
                            "API key validation returned status %d for key %s",
                            response.status_code,
                            redacted_key,
                        )
                        # Fail closed but don't cache (transient service error)
                        return ValidationResult(
                            valid=False,
                            error=f"Auth service error (status {response.status_code})",
                            cacheable=False,
                        )

            except httpx.TimeoutException:
                if attempt < self.MAX_RETRIES:
                    logger.debug(
                        "API key validation timeout for key %s, retrying...",
                        redacted_key,
                    )
                    await asyncio.sleep(0.1 * (attempt + 1))
                    continue
                logger.warning(
                    "API key validation timeout for key %s after %d attempts",
                    redacted_key,
                    attempt + 1,
                )
                return ValidationResult(
                    valid=False,
                    error="Auth service timeout",
                    cacheable=False,
                )
            except httpx.RequestError as exc:
                if attempt < self.MAX_RETRIES:
                    logger.debug(
                        "API key validation request error for key %s: %s, retrying...",
                        redacted_key,
                        exc,
                    )
                    await asyncio.sleep(0.1 * (attempt + 1))
                    continue
                logger.warning(
                    "API key validation request error for key %s: %s",
                    redacted_key,
                    exc,
                )
                return ValidationResult(
                    valid=False,
                    error="Auth service unavailable",
                    cacheable=False,
                )
            except Exception as exc:
                logger.error(
                    "Unexpected error validating API key %s: %s",
                    redacted_key,
                    exc,
                )
                return ValidationResult(
                    valid=False,
                    error="Auth service error",
                    cacheable=False,
                )

        # Should not reach here, but fail closed
        return ValidationResult(valid=False, error="Auth service error", cacheable=False)

    async def invalidate_cache(self, api_key: str) -> None:
        """Remove an API key from the cache."""
        async with self._cache_lock:
            self._cache.pop(api_key, None)

    async def clear_cache(self) -> None:
        """Clear all cached validations."""
        async with self._cache_lock:
            self._cache.clear()


__all__ = ["ApiKeyService", "ValidationResult"]
