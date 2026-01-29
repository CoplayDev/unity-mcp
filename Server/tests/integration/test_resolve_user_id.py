"""Tests for _resolve_user_id_from_request in unity_transport.py."""

import sys
import types
from unittest.mock import AsyncMock

import pytest

from core.config import config
from services.api_key_service import ApiKeyService, ValidationResult


@pytest.fixture(autouse=True)
def _reset_api_key_singleton():
    ApiKeyService._instance = None
    yield
    ApiKeyService._instance = None


class TestResolveUserIdFromRequest:
    @pytest.mark.asyncio
    async def test_returns_none_when_not_remote_hosted(self, monkeypatch):
        monkeypatch.setattr(config, "http_remote_hosted", False)

        from transport.unity_transport import _resolve_user_id_from_request

        result = await _resolve_user_id_from_request()
        assert result is None

    @pytest.mark.asyncio
    async def test_returns_none_when_service_not_initialized(self, monkeypatch):
        monkeypatch.setattr(config, "http_remote_hosted", True)
        # ApiKeyService._instance is None (from fixture)

        from transport.unity_transport import _resolve_user_id_from_request

        result = await _resolve_user_id_from_request()
        assert result is None

    @pytest.mark.asyncio
    async def test_returns_user_id_for_valid_key(self, monkeypatch):
        monkeypatch.setattr(config, "http_remote_hosted", True)

        svc = ApiKeyService(validation_url="https://auth.example.com/validate")
        svc.validate = AsyncMock(
            return_value=ValidationResult(valid=True, user_id="user-123")
        )

        # Stub the fastmcp dependency that provides HTTP headers
        deps_mod = types.ModuleType("fastmcp.server.dependencies")
        deps_mod.get_http_headers = lambda include_all=False: {
            "x-api-key": "sk-valid"}
        monkeypatch.setitem(
            sys.modules, "fastmcp.server.dependencies", deps_mod)

        from transport.unity_transport import _resolve_user_id_from_request

        result = await _resolve_user_id_from_request()
        assert result == "user-123"
        svc.validate.assert_called_once_with("sk-valid")

    @pytest.mark.asyncio
    async def test_returns_none_for_invalid_key(self, monkeypatch):
        monkeypatch.setattr(config, "http_remote_hosted", True)

        svc = ApiKeyService(validation_url="https://auth.example.com/validate")
        svc.validate = AsyncMock(
            return_value=ValidationResult(valid=False, error="bad key")
        )

        deps_mod = types.ModuleType("fastmcp.server.dependencies")
        deps_mod.get_http_headers = lambda include_all=False: {
            "x-api-key": "sk-bad"}
        monkeypatch.setitem(
            sys.modules, "fastmcp.server.dependencies", deps_mod)

        from transport.unity_transport import _resolve_user_id_from_request

        result = await _resolve_user_id_from_request()
        assert result is None

    @pytest.mark.asyncio
    async def test_returns_none_on_exception(self, monkeypatch):
        monkeypatch.setattr(config, "http_remote_hosted", True)

        svc = ApiKeyService(validation_url="https://auth.example.com/validate")
        svc.validate = AsyncMock(side_effect=RuntimeError("boom"))

        deps_mod = types.ModuleType("fastmcp.server.dependencies")
        deps_mod.get_http_headers = lambda include_all=False: {
            "x-api-key": "sk-err"}
        monkeypatch.setitem(
            sys.modules, "fastmcp.server.dependencies", deps_mod)

        from transport.unity_transport import _resolve_user_id_from_request

        result = await _resolve_user_id_from_request()
        assert result is None

    @pytest.mark.asyncio
    async def test_returns_none_when_no_api_key_header(self, monkeypatch):
        monkeypatch.setattr(config, "http_remote_hosted", True)

        ApiKeyService(validation_url="https://auth.example.com/validate")

        deps_mod = types.ModuleType("fastmcp.server.dependencies")
        deps_mod.get_http_headers = lambda include_all=False: {}  # No x-api-key
        monkeypatch.setitem(
            sys.modules, "fastmcp.server.dependencies", deps_mod)

        from transport.unity_transport import _resolve_user_id_from_request

        result = await _resolve_user_id_from_request()
        assert result is None
