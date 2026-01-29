"""Tests for PluginHub WebSocket API key authentication gate."""

import asyncio
from types import SimpleNamespace
from unittest.mock import AsyncMock, MagicMock, patch

import pytest

from core.config import config
from core.constants import API_KEY_HEADER
from services.api_key_service import ApiKeyService, ValidationResult
from transport.plugin_hub import PluginHub
from transport.plugin_registry import PluginRegistry


@pytest.fixture(autouse=True)
def _reset_api_key_singleton():
    ApiKeyService._instance = None
    yield
    ApiKeyService._instance = None


@pytest.fixture(autouse=True)
def _reset_plugin_hub():
    """Ensure PluginHub class-level state doesn't leak between tests."""
    old_registry = PluginHub._registry
    old_connections = PluginHub._connections.copy()
    old_pending = PluginHub._pending.copy()
    old_lock = PluginHub._lock
    old_loop = PluginHub._loop

    yield

    PluginHub._registry = old_registry
    PluginHub._connections = old_connections
    PluginHub._pending = old_pending
    PluginHub._lock = old_lock
    PluginHub._loop = old_loop


def _make_mock_websocket(headers=None, state_attrs=None):
    """Create a mock WebSocket with configurable headers and state."""
    ws = AsyncMock()
    ws.headers = headers or {}
    ws.state = SimpleNamespace(**(state_attrs or {}))
    ws.accept = AsyncMock()
    ws.close = AsyncMock()
    ws.send_json = AsyncMock()
    return ws


def _make_hub():
    """Create a PluginHub instance with a minimal ASGI scope."""
    scope = {"type": "websocket"}
    return PluginHub(scope, receive=AsyncMock(), send=AsyncMock())


def _init_api_key_service(validate_result=None):
    """Initialize ApiKeyService with a mocked validate method."""
    svc = ApiKeyService(validation_url="https://auth.example.com/validate")
    if validate_result is not None:
        svc.validate = AsyncMock(return_value=validate_result)
    return svc


class TestWebSocketAuthGate:
    @pytest.mark.asyncio
    async def test_no_api_key_remote_hosted_rejected(self, monkeypatch):
        """WebSocket without API key in remote-hosted mode -> close 4401."""
        monkeypatch.setattr(config, "http_remote_hosted", True)
        _init_api_key_service(ValidationResult(valid=True, user_id="u1"))

        ws = _make_mock_websocket(headers={})  # No X-API-Key header
        hub = _make_hub()

        await hub.on_connect(ws)

        ws.close.assert_called_once_with(code=4401, reason="API key required")
        ws.accept.assert_not_called()

    @pytest.mark.asyncio
    async def test_invalid_api_key_rejected(self, monkeypatch):
        """WebSocket with invalid API key -> close 4403."""
        monkeypatch.setattr(config, "http_remote_hosted", True)
        _init_api_key_service(ValidationResult(
            valid=False, error="Invalid API key"))

        ws = _make_mock_websocket(headers={API_KEY_HEADER: "sk-bad-key"})
        hub = _make_hub()

        await hub.on_connect(ws)

        ws.close.assert_called_once_with(code=4403, reason="Invalid API key")
        ws.accept.assert_not_called()

    @pytest.mark.asyncio
    async def test_valid_api_key_accepted(self, monkeypatch):
        """WebSocket with valid API key -> accepted, user_id stored in state."""
        monkeypatch.setattr(config, "http_remote_hosted", True)
        _init_api_key_service(
            ValidationResult(valid=True, user_id="user-42",
                             metadata={"plan": "pro"})
        )

        ws = _make_mock_websocket(headers={API_KEY_HEADER: "sk-valid-key"})
        hub = _make_hub()

        await hub.on_connect(ws)

        ws.accept.assert_called_once()
        ws.close.assert_not_called()
        assert ws.state.user_id == "user-42"
        assert ws.state.api_key_metadata == {"plan": "pro"}
        # Should have sent welcome message
        ws.send_json.assert_called_once()

    @pytest.mark.asyncio
    async def test_auth_service_unavailable_close_1013(self, monkeypatch):
        """Auth service error with 'unavailable' -> close 1013 (try again later)."""
        monkeypatch.setattr(config, "http_remote_hosted", True)
        _init_api_key_service(
            ValidationResult(
                valid=False, error="Auth service unavailable", cacheable=False)
        )

        ws = _make_mock_websocket(headers={API_KEY_HEADER: "sk-some-key"})
        hub = _make_hub()

        await hub.on_connect(ws)

        ws.close.assert_called_once_with(code=1013, reason="Try again later")
        ws.accept.assert_not_called()

    @pytest.mark.asyncio
    async def test_not_remote_hosted_accepts_without_key(self, monkeypatch):
        """When not remote-hosted, WebSocket accepted without API key."""
        monkeypatch.setattr(config, "http_remote_hosted", False)

        ws = _make_mock_websocket(headers={})
        hub = _make_hub()

        await hub.on_connect(ws)

        ws.accept.assert_called_once()
        ws.close.assert_not_called()


class TestUserIdFlowsToRegistration:
    @pytest.mark.asyncio
    async def test_user_id_passed_to_registry_on_register(self, monkeypatch):
        """After valid auth, the register message should pass user_id to registry."""
        monkeypatch.setattr(config, "http_remote_hosted", True)
        _init_api_key_service(
            ValidationResult(valid=True, user_id="user-99")
        )

        registry = PluginRegistry()
        loop = asyncio.get_running_loop()
        PluginHub.configure(registry, loop)

        # Simulate full flow: connect, then register
        ws = _make_mock_websocket(headers={API_KEY_HEADER: "sk-valid-key"})
        hub = _make_hub()

        await hub.on_connect(ws)
        assert ws.state.user_id == "user-99"

        # Simulate register message
        register_data = {
            "type": "register",
            "project_name": "TestProject",
            "project_hash": "abc123",
            "unity_version": "2022.3",
        }
        await hub.on_receive(ws, register_data)

        # Verify registry has the user_id
        sessions = await registry.list_sessions(user_id="user-99")
        assert len(sessions) == 1
        session = next(iter(sessions.values()))
        assert session.user_id == "user-99"
        assert session.project_name == "TestProject"
        assert session.project_hash == "abc123"
