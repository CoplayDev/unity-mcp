"""Tests for UnityInstanceMiddleware auth enforcement in remote-hosted mode."""

import asyncio
import sys
from unittest.mock import AsyncMock, Mock, patch

import pytest

from core.config import config
from tests.integration.test_helpers import DummyContext


class TestMiddlewareAuthEnforcement:
    @pytest.mark.asyncio
    async def test_remote_hosted_requires_user_id(self, monkeypatch):
        """_inject_unity_instance should raise RuntimeError when remote-hosted and no user_id."""
        monkeypatch.setattr(config, "http_remote_hosted", True)

        from transport.unity_instance_middleware import UnityInstanceMiddleware

        middleware = UnityInstanceMiddleware()

        # Mock _resolve_user_id to return None (no API key / failed validation)
        monkeypatch.setattr(middleware, "_resolve_user_id",
                            AsyncMock(return_value=None))

        ctx = DummyContext()
        middleware_ctx = Mock()
        middleware_ctx.fastmcp_context = ctx

        with pytest.raises(RuntimeError, match="API key authentication required"):
            await middleware._inject_unity_instance(middleware_ctx)

    @pytest.mark.asyncio
    async def test_sets_user_id_in_context_state(self, monkeypatch):
        """_inject_unity_instance should set user_id in ctx state when resolved."""
        monkeypatch.setattr(config, "http_remote_hosted", True)

        from transport.unity_instance_middleware import UnityInstanceMiddleware

        middleware = UnityInstanceMiddleware()
        monkeypatch.setattr(middleware, "_resolve_user_id",
                            AsyncMock(return_value="user-55"))

        # We need PluginHub to be configured for the session resolution path
        # But we don't need it to actually find a session for this test
        from transport.plugin_hub import PluginHub
        from transport.plugin_registry import PluginRegistry

        registry = PluginRegistry()
        loop = asyncio.get_running_loop()
        PluginHub.configure(registry, loop)

        ctx = DummyContext()
        ctx.client_id = "client-1"
        middleware_ctx = Mock()
        middleware_ctx.fastmcp_context = ctx

        # Set an active instance so the middleware doesn't try to auto-select
        middleware.set_active_instance(ctx, "Proj@hash1")
        # Register a matching session so resolution doesn't fail
        await registry.register("s1", "Proj", "hash1", "2022", user_id="user-55")

        await middleware._inject_unity_instance(middleware_ctx)

        assert ctx.get_state("user_id") == "user-55"


class TestMiddlewareSessionKey:
    def test_get_session_key_uses_user_id_fallback(self):
        """When no client_id, middleware should use user:$user_id as session key."""
        from transport.unity_instance_middleware import UnityInstanceMiddleware

        middleware = UnityInstanceMiddleware()

        ctx = DummyContext()
        # Simulate no client_id attribute
        if hasattr(ctx, "client_id"):
            delattr(ctx, "client_id")
        ctx.set_state("user_id", "user-77")

        key = middleware.get_session_key(ctx)
        assert key == "user:user-77"

    def test_get_session_key_prefers_client_id(self):
        """client_id should take precedence over user_id."""
        from transport.unity_instance_middleware import UnityInstanceMiddleware

        middleware = UnityInstanceMiddleware()

        ctx = DummyContext()
        ctx.client_id = "client-abc"
        ctx.set_state("user_id", "user-77")

        key = middleware.get_session_key(ctx)
        assert key == "client-abc"


class TestAutoSelectDisabledRemoteHosted:
    @pytest.mark.asyncio
    async def test_auto_select_returns_none_in_remote_hosted(self, monkeypatch):
        """_maybe_autoselect_instance should return None in remote-hosted mode even with one session."""
        monkeypatch.setattr(config, "http_remote_hosted", True)
        monkeypatch.setattr(config, "transport_mode", "http")

        # Re-import middleware to pick up the stubbed transport module
        monkeypatch.delitem(
            sys.modules, "transport.unity_instance_middleware", raising=False)
        from transport.unity_instance_middleware import UnityInstanceMiddleware, PluginHub as HubRef

        # Configure PluginHub with one session so auto-select has something to find
        from transport.plugin_registry import PluginRegistry
        registry = PluginRegistry()
        await registry.register("s1", "Proj", "h1", "2022", user_id="userA")

        loop = asyncio.get_running_loop()
        HubRef.configure(registry, loop)

        middleware = UnityInstanceMiddleware()
        ctx = DummyContext()
        ctx.client_id = "client-1"

        result = await middleware._maybe_autoselect_instance(ctx)
        # Remote-hosted mode should NOT auto-select (early return at the transport check)
        assert result is None


class TestHttpAuthBehavior:
    @pytest.mark.asyncio
    async def test_http_local_does_not_require_user_id(self, monkeypatch):
        """HTTP local mode should allow requests without user_id."""
        monkeypatch.setattr(config, "http_remote_hosted", False)
        monkeypatch.setattr(config, "transport_mode", "http")

        from transport import unity_transport

        async def fake_send_command_for_instance(*_args, **_kwargs):
            return {"success": True, "data": {"ok": True}}

        monkeypatch.setattr(
            unity_transport.PluginHub,
            "send_command_for_instance",
            fake_send_command_for_instance,
        )

        async def _unused_send_fn(*_args, **_kwargs):
            raise AssertionError("send_fn should not be used in HTTP mode")

        result = await unity_transport.send_with_unity_instance(
            _unused_send_fn, None, "ping", {}
        )

        assert result["success"] is True
        assert result["data"] == {"ok": True}

    @pytest.mark.asyncio
    async def test_http_remote_requires_user_id(self, monkeypatch):
        """HTTP remote-hosted mode should reject requests without user_id."""
        monkeypatch.setattr(config, "http_remote_hosted", True)
        monkeypatch.setattr(config, "transport_mode", "http")

        from transport import unity_transport

        async def _unused_send_fn(*_args, **_kwargs):
            raise AssertionError("send_fn should not be used in HTTP mode")

        result = await unity_transport.send_with_unity_instance(
            _unused_send_fn, None, "ping", {}
        )

        assert result["success"] is False
        assert result["error"] == "auth_required"
