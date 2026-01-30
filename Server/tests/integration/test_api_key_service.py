"""Tests for ApiKeyService: validation, caching, retries, and singleton lifecycle."""

import asyncio
import time
from unittest.mock import AsyncMock, MagicMock, patch

import httpx
import pytest

from services.api_key_service import ApiKeyService, ValidationResult


@pytest.fixture(autouse=True)
def _reset_singleton():
    """Reset the ApiKeyService singleton between tests."""
    ApiKeyService._instance = None
    yield
    ApiKeyService._instance = None


def _make_service(
    validation_url="https://auth.example.com/validate",
    cache_ttl=300.0,
    service_token_header=None,
    service_token=None,
):
    return ApiKeyService(
        validation_url=validation_url,
        cache_ttl=cache_ttl,
        service_token_header=service_token_header,
        service_token=service_token,
    )


def _mock_response(status_code=200, json_data=None):
    resp = MagicMock(spec=httpx.Response)
    resp.status_code = status_code
    resp.json.return_value = json_data or {}
    return resp


# ---------------------------------------------------------------------------
# Singleton lifecycle
# ---------------------------------------------------------------------------


class TestSingletonLifecycle:
    def test_get_instance_before_init_raises(self):
        with pytest.raises(RuntimeError, match="not initialized"):
            ApiKeyService.get_instance()

    def test_is_initialized_false_before_init(self):
        assert ApiKeyService.is_initialized() is False

    def test_is_initialized_true_after_init(self):
        _make_service()
        assert ApiKeyService.is_initialized() is True

    def test_get_instance_returns_service(self):
        svc = _make_service()
        assert ApiKeyService.get_instance() is svc


# ---------------------------------------------------------------------------
# Basic validation
# ---------------------------------------------------------------------------


class TestBasicValidation:
    @pytest.mark.asyncio
    async def test_valid_key(self):
        svc = _make_service()
        mock_resp = _mock_response(
            200, {"valid": True, "user_id": "user-1", "metadata": {"plan": "pro"}})

        with patch("httpx.AsyncClient") as MockClient:
            instance = AsyncMock()
            instance.__aenter__ = AsyncMock(return_value=instance)
            instance.__aexit__ = AsyncMock(return_value=False)
            instance.post = AsyncMock(return_value=mock_resp)
            MockClient.return_value = instance

            result = await svc.validate("test-valid-key-12345678")

        assert result.valid is True
        assert result.user_id == "user-1"
        assert result.metadata == {"plan": "pro"}

    @pytest.mark.asyncio
    async def test_invalid_key_200_body(self):
        svc = _make_service()
        mock_resp = _mock_response(
            200, {"valid": False, "error": "Key revoked"})

        with patch("httpx.AsyncClient") as MockClient:
            instance = AsyncMock()
            instance.__aenter__ = AsyncMock(return_value=instance)
            instance.__aexit__ = AsyncMock(return_value=False)
            instance.post = AsyncMock(return_value=mock_resp)
            MockClient.return_value = instance

            result = await svc.validate("test-invalid-key-1234")

        assert result.valid is False
        assert result.error == "Key revoked"

    @pytest.mark.asyncio
    async def test_invalid_key_401_status(self):
        svc = _make_service()
        mock_resp = _mock_response(401)

        with patch("httpx.AsyncClient") as MockClient:
            instance = AsyncMock()
            instance.__aenter__ = AsyncMock(return_value=instance)
            instance.__aexit__ = AsyncMock(return_value=False)
            instance.post = AsyncMock(return_value=mock_resp)
            MockClient.return_value = instance

            result = await svc.validate("test-bad-key-12345678")

        assert result.valid is False
        assert "Invalid API key" in result.error

    @pytest.mark.asyncio
    async def test_empty_key_fast_path(self):
        svc = _make_service()

        with patch("httpx.AsyncClient") as MockClient:
            result = await svc.validate("")

        assert result.valid is False
        assert "required" in result.error.lower()
        # No HTTP call should have been made
        MockClient.assert_not_called()


# ---------------------------------------------------------------------------
# Caching
# ---------------------------------------------------------------------------


class TestCaching:
    @pytest.mark.asyncio
    async def test_cache_hit_valid_key(self):
        svc = _make_service(cache_ttl=300.0)
        mock_resp = _mock_response(200, {"valid": True, "user_id": "u1"})
        call_count = 0

        async def counting_post(*args, **kwargs):
            nonlocal call_count
            call_count += 1
            return mock_resp

        with patch("httpx.AsyncClient") as MockClient:
            instance = AsyncMock()
            instance.__aenter__ = AsyncMock(return_value=instance)
            instance.__aexit__ = AsyncMock(return_value=False)
            instance.post = counting_post
            MockClient.return_value = instance

            r1 = await svc.validate("test-cached-valid-key1")
            r2 = await svc.validate("test-cached-valid-key1")

        assert r1.valid is True
        assert r2.valid is True
        assert r2.user_id == "u1"
        assert call_count == 1  # Only one HTTP call

    @pytest.mark.asyncio
    async def test_cache_hit_invalid_key(self):
        svc = _make_service(cache_ttl=300.0)
        mock_resp = _mock_response(200, {"valid": False, "error": "bad"})
        call_count = 0

        async def counting_post(*args, **kwargs):
            nonlocal call_count
            call_count += 1
            return mock_resp

        with patch("httpx.AsyncClient") as MockClient:
            instance = AsyncMock()
            instance.__aenter__ = AsyncMock(return_value=instance)
            instance.__aexit__ = AsyncMock(return_value=False)
            instance.post = counting_post
            MockClient.return_value = instance

            r1 = await svc.validate("test-cached-bad-key12")
            r2 = await svc.validate("test-cached-bad-key12")

        assert r1.valid is False
        assert r2.valid is False
        assert call_count == 1

    @pytest.mark.asyncio
    async def test_cache_expiry(self):
        svc = _make_service(cache_ttl=1.0)  # 1 second TTL
        mock_resp = _mock_response(200, {"valid": True, "user_id": "u1"})
        call_count = 0

        async def counting_post(*args, **kwargs):
            nonlocal call_count
            call_count += 1
            return mock_resp

        with patch("httpx.AsyncClient") as MockClient:
            instance = AsyncMock()
            instance.__aenter__ = AsyncMock(return_value=instance)
            instance.__aexit__ = AsyncMock(return_value=False)
            instance.post = counting_post
            MockClient.return_value = instance

            await svc.validate("test-expiry-key-12345")
            assert call_count == 1

            # Manually expire the cache entry by manipulating the stored tuple
            async with svc._cache_lock:
                key = "test-expiry-key-12345"
                valid, user_id, metadata, _expires = svc._cache[key]
                svc._cache[key] = (valid, user_id, metadata, time.time() - 1)

            await svc.validate("test-expiry-key-12345")
            assert call_count == 2  # Had to re-validate

    @pytest.mark.asyncio
    async def test_invalidate_cache(self):
        svc = _make_service(cache_ttl=300.0)
        mock_resp = _mock_response(200, {"valid": True, "user_id": "u1"})
        call_count = 0

        async def counting_post(*args, **kwargs):
            nonlocal call_count
            call_count += 1
            return mock_resp

        with patch("httpx.AsyncClient") as MockClient:
            instance = AsyncMock()
            instance.__aenter__ = AsyncMock(return_value=instance)
            instance.__aexit__ = AsyncMock(return_value=False)
            instance.post = counting_post
            MockClient.return_value = instance

            await svc.validate("test-invalidate-key12")
            assert call_count == 1

            await svc.invalidate_cache("test-invalidate-key12")

            await svc.validate("test-invalidate-key12")
            assert call_count == 2

    @pytest.mark.asyncio
    async def test_clear_cache(self):
        svc = _make_service(cache_ttl=300.0)
        mock_resp = _mock_response(200, {"valid": True, "user_id": "u1"})
        call_count = 0

        async def counting_post(*args, **kwargs):
            nonlocal call_count
            call_count += 1
            return mock_resp

        with patch("httpx.AsyncClient") as MockClient:
            instance = AsyncMock()
            instance.__aenter__ = AsyncMock(return_value=instance)
            instance.__aexit__ = AsyncMock(return_value=False)
            instance.post = counting_post
            MockClient.return_value = instance

            await svc.validate("test-clear-key1-12345")
            await svc.validate("test-clear-key2-12345")
            assert call_count == 2

            await svc.clear_cache()

            await svc.validate("test-clear-key1-12345")
            await svc.validate("test-clear-key2-12345")
            assert call_count == 4  # Both had to re-validate


# ---------------------------------------------------------------------------
# Transient failures & retries
# ---------------------------------------------------------------------------


class TestTransientFailures:
    @pytest.mark.asyncio
    async def test_5xx_not_cached(self):
        svc = _make_service(cache_ttl=300.0)
        mock_500 = _mock_response(500)
        mock_ok = _mock_response(200, {"valid": True, "user_id": "u1"})
        responses = [mock_500, mock_500, mock_ok]  # Extra for retry
        call_idx = 0

        async def sequential_post(*args, **kwargs):
            nonlocal call_idx
            resp = responses[min(call_idx, len(responses) - 1)]
            call_idx += 1
            return resp

        with patch("httpx.AsyncClient") as MockClient:
            instance = AsyncMock()
            instance.__aenter__ = AsyncMock(return_value=instance)
            instance.__aexit__ = AsyncMock(return_value=False)
            instance.post = sequential_post
            MockClient.return_value = instance

            # First call: 500 -> not cached
            r1 = await svc.validate("test-5xx-test-key1234")
            assert r1.valid is False
            assert r1.cacheable is False

            # Second call should hit HTTP again (not cached)
            r2 = await svc.validate("test-5xx-test-key1234")
            # Second call also gets 500 from our mock sequence
            assert r2.valid is False

    @pytest.mark.asyncio
    async def test_timeout_then_retry_succeeds(self):
        svc = _make_service()
        mock_ok = _mock_response(200, {"valid": True, "user_id": "u1"})
        attempt = 0

        async def timeout_then_ok(*args, **kwargs):
            nonlocal attempt
            attempt += 1
            if attempt == 1:
                raise httpx.TimeoutException("timed out")
            return mock_ok

        with patch("httpx.AsyncClient") as MockClient:
            instance = AsyncMock()
            instance.__aenter__ = AsyncMock(return_value=instance)
            instance.__aexit__ = AsyncMock(return_value=False)
            instance.post = timeout_then_ok
            MockClient.return_value = instance

            result = await svc.validate("test-timeout-retry-ok")

        assert result.valid is True
        assert result.user_id == "u1"
        assert attempt == 2

    @pytest.mark.asyncio
    async def test_timeout_exhausts_retries(self):
        svc = _make_service()

        async def always_timeout(*args, **kwargs):
            raise httpx.TimeoutException("timed out")

        with patch("httpx.AsyncClient") as MockClient:
            instance = AsyncMock()
            instance.__aenter__ = AsyncMock(return_value=instance)
            instance.__aexit__ = AsyncMock(return_value=False)
            instance.post = always_timeout
            MockClient.return_value = instance

            result = await svc.validate("test-timeout-exhaust1")

        assert result.valid is False
        assert "timeout" in result.error.lower()
        assert result.cacheable is False

    @pytest.mark.asyncio
    async def test_request_error_then_retry_succeeds(self):
        svc = _make_service()
        mock_ok = _mock_response(200, {"valid": True, "user_id": "u1"})
        attempt = 0

        async def error_then_ok(*args, **kwargs):
            nonlocal attempt
            attempt += 1
            if attempt == 1:
                raise httpx.ConnectError("connection refused")
            return mock_ok

        with patch("httpx.AsyncClient") as MockClient:
            instance = AsyncMock()
            instance.__aenter__ = AsyncMock(return_value=instance)
            instance.__aexit__ = AsyncMock(return_value=False)
            instance.post = error_then_ok
            MockClient.return_value = instance

            result = await svc.validate("test-reqerr-retry-ok1")

        assert result.valid is True
        assert attempt == 2

    @pytest.mark.asyncio
    async def test_request_error_exhausts_retries(self):
        svc = _make_service()

        async def always_error(*args, **kwargs):
            raise httpx.ConnectError("connection refused")

        with patch("httpx.AsyncClient") as MockClient:
            instance = AsyncMock()
            instance.__aenter__ = AsyncMock(return_value=instance)
            instance.__aexit__ = AsyncMock(return_value=False)
            instance.post = always_error
            MockClient.return_value = instance

            result = await svc.validate("test-reqerr-exhaust1")

        assert result.valid is False
        assert "unavailable" in result.error.lower()
        assert result.cacheable is False

    @pytest.mark.asyncio
    async def test_unexpected_exception(self):
        svc = _make_service()

        async def unexpected(*args, **kwargs):
            raise ValueError("something unexpected")

        with patch("httpx.AsyncClient") as MockClient:
            instance = AsyncMock()
            instance.__aenter__ = AsyncMock(return_value=instance)
            instance.__aexit__ = AsyncMock(return_value=False)
            instance.post = unexpected
            MockClient.return_value = instance

            result = await svc.validate("test-unexpected-err12")

        assert result.valid is False
        assert result.cacheable is False


# ---------------------------------------------------------------------------
# Service token
# ---------------------------------------------------------------------------


class TestServiceToken:
    @pytest.mark.asyncio
    async def test_service_token_sent_in_headers(self):
        svc = _make_service(
            service_token_header="X-Service-Token",
            service_token="test-svc-token-123",
        )
        mock_resp = _mock_response(200, {"valid": True, "user_id": "u1"})
        captured_headers = {}

        async def capture_post(url, *, json=None, headers=None):
            captured_headers.update(headers or {})
            return mock_resp

        with patch("httpx.AsyncClient") as MockClient:
            instance = AsyncMock()
            instance.__aenter__ = AsyncMock(return_value=instance)
            instance.__aexit__ = AsyncMock(return_value=False)
            instance.post = capture_post
            MockClient.return_value = instance

            await svc.validate("test-svctoken-key1234")

        assert captured_headers.get("X-Service-Token") == "test-svc-token-123"
        assert captured_headers.get("Content-Type") == "application/json"
