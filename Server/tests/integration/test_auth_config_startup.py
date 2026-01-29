"""Tests for auth configuration validation and startup routes."""

import json
import sys
from unittest.mock import MagicMock

import pytest

from core.config import config
from starlette.requests import Request
from starlette.responses import JSONResponse


@pytest.fixture(autouse=True)
def _restore_config(monkeypatch):
    """Prevent main() side effects on the global config from leaking to other tests."""
    monkeypatch.setattr(config, "http_remote_hosted", config.http_remote_hosted)
    monkeypatch.setattr(config, "api_key_validation_url", config.api_key_validation_url)
    monkeypatch.setattr(config, "api_key_login_url", config.api_key_login_url)
    monkeypatch.setattr(config, "api_key_cache_ttl", config.api_key_cache_ttl)
    monkeypatch.setattr(config, "api_key_service_token_header", config.api_key_service_token_header)
    monkeypatch.setattr(config, "api_key_service_token", config.api_key_service_token)


class TestStartupConfigValidation:
    def test_remote_hosted_flag_without_validation_url_exits(self, monkeypatch):
        """--http-remote-hosted without --api-key-validation-url should SystemExit(1)."""
        monkeypatch.setattr(
            sys,
            "argv",
            [
                "main",
                "--transport", "http",
                "--http-remote-hosted",
                # Deliberately omit --api-key-validation-url
            ],
        )
        monkeypatch.delenv("UNITY_MCP_API_KEY_VALIDATION_URL", raising=False)
        monkeypatch.delenv("UNITY_MCP_HTTP_REMOTE_HOSTED", raising=False)

        from main import main

        with pytest.raises(SystemExit) as exc_info:
            main()

        assert exc_info.value.code == 1

    def test_remote_hosted_env_var_without_validation_url_exits(self, monkeypatch):
        """UNITY_MCP_HTTP_REMOTE_HOSTED=true without validation URL should SystemExit(1)."""
        monkeypatch.setattr(
            sys,
            "argv",
            [
                "main",
                "--transport", "http",
                # No --http-remote-hosted flag
            ],
        )
        monkeypatch.setenv("UNITY_MCP_HTTP_REMOTE_HOSTED", "true")
        monkeypatch.delenv("UNITY_MCP_API_KEY_VALIDATION_URL", raising=False)

        from main import main

        with pytest.raises(SystemExit) as exc_info:
            main()

        assert exc_info.value.code == 1


class TestLoginUrlEndpoint:
    """Test the /api/auth/login-url route handler logic.

    These tests replicate the handler inline to avoid full MCP server construction.
    The logic mirrors main.py's auth_login_url route exactly.
    """

    @staticmethod
    async def _auth_login_url(_request):
        """Replicate the route handler from main.py."""
        if not config.api_key_login_url:
            return JSONResponse(
                {
                    "success": False,
                    "error": "API key management not configured. Contact your server administrator.",
                },
                status_code=404,
            )
        return JSONResponse({
            "success": True,
            "login_url": config.api_key_login_url,
        })

    @pytest.mark.asyncio
    async def test_login_url_returns_url_when_configured(self, monkeypatch):
        monkeypatch.setattr(config, "api_key_login_url",
                            "https://app.example.com/keys")

        response = await self._auth_login_url(MagicMock(spec=Request))

        assert response.status_code == 200
        body = json.loads(response.body.decode())
        assert body["success"] is True
        assert body["login_url"] == "https://app.example.com/keys"

    @pytest.mark.asyncio
    async def test_login_url_returns_404_when_not_configured(self, monkeypatch):
        monkeypatch.setattr(config, "api_key_login_url", None)

        response = await self._auth_login_url(MagicMock(spec=Request))

        assert response.status_code == 404
        body = json.loads(response.body.decode())
        assert body["success"] is False
        assert "not configured" in body["error"]
