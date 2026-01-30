"""Integration tests for multi-user session isolation in remote-hosted mode.

These tests compose PluginRegistry + PluginHub to verify that users
cannot see or interact with each other's Unity instances.
"""

import asyncio
from unittest.mock import AsyncMock

import pytest

from core.config import config
from transport.plugin_hub import NoUnitySessionError, PluginHub
from transport.plugin_registry import PluginRegistry


@pytest.fixture(autouse=True)
def _reset_plugin_hub():
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


async def _setup_two_user_registry():
    """Set up a registry with two users, each having Unity instances.

    Returns the configured registry. Also configures PluginHub to use it.
    """
    registry = PluginRegistry()
    loop = asyncio.get_running_loop()
    PluginHub.configure(registry, loop)

    await registry.register("sess-A1", "ProjectAlpha", "hashA1", "2022.3", user_id="userA")
    await registry.register("sess-B1", "ProjectBeta", "hashB1", "2022.3", user_id="userB")
    await registry.register("sess-A2", "ProjectGamma", "hashA2", "2022.3", user_id="userA")

    return registry


class TestMultiUserSessionFiltering:
    @pytest.mark.asyncio
    async def test_get_sessions_filters_by_user(self):
        """PluginHub.get_sessions(user_id=X) returns only X's sessions."""
        await _setup_two_user_registry()

        sessions_a = await PluginHub.get_sessions(user_id="userA")
        assert len(sessions_a.sessions) == 2
        project_names = {s.project for s in sessions_a.sessions.values()}
        assert project_names == {"ProjectAlpha", "ProjectGamma"}

        sessions_b = await PluginHub.get_sessions(user_id="userB")
        assert len(sessions_b.sessions) == 1
        assert next(iter(sessions_b.sessions.values())
                    ).project == "ProjectBeta"

    @pytest.mark.asyncio
    async def test_get_sessions_no_filter_returns_all_in_local_mode(self):
        """In local mode, PluginHub.get_sessions() without user_id returns everything."""
        await _setup_two_user_registry()

        all_sessions = await PluginHub.get_sessions()
        assert len(all_sessions.sessions) == 3

    @pytest.mark.asyncio
    async def test_get_sessions_no_filter_raises_in_remote_hosted(self, monkeypatch):
        """In remote-hosted mode, PluginHub.get_sessions() without user_id raises."""
        monkeypatch.setattr(config, "http_remote_hosted", True)
        await _setup_two_user_registry()

        with pytest.raises(ValueError, match="requires user_id"):
            await PluginHub.get_sessions()


class TestResolveSessionIdIsolation:
    @pytest.mark.asyncio
    async def test_resolve_session_for_own_hash(self, monkeypatch):
        """User A can resolve their own project hash."""
        monkeypatch.setattr(config, "http_remote_hosted", True)
        await _setup_two_user_registry()

        session_id = await PluginHub._resolve_session_id("hashA1", user_id="userA")
        assert session_id == "sess-A1"

    @pytest.mark.asyncio
    async def test_cannot_resolve_other_users_hash(self, monkeypatch):
        """User A cannot resolve User B's project hash."""
        monkeypatch.setattr(config, "http_remote_hosted", True)
        monkeypatch.setenv("UNITY_MCP_SESSION_RESOLVE_MAX_WAIT_S", "0.1")
        await _setup_two_user_registry()

        # userA tries to resolve userB's hash -> should not find it
        with pytest.raises(NoUnitySessionError):
            await PluginHub._resolve_session_id("hashB1", user_id="userA")


class TestInstanceListResourceIsolation:
    @pytest.mark.asyncio
    async def test_unity_instances_resource_filters_by_user(self, monkeypatch):
        """The unity_instances resource should pass user_id and return filtered results."""
        monkeypatch.setattr(config, "http_remote_hosted", True)
        monkeypatch.setattr(config, "transport_mode", "http")
        await _setup_two_user_registry()

        from services.resources.unity_instances import unity_instances
        from tests.integration.test_helpers import DummyContext

        ctx = DummyContext()
        ctx.set_state("user_id", "userA")

        result = await unity_instances(ctx)

        assert result["success"] is True
        assert result["instance_count"] == 2
        instance_names = {i["name"] for i in result["instances"]}
        assert instance_names == {"ProjectAlpha", "ProjectGamma"}
        assert "ProjectBeta" not in instance_names


class TestSetActiveInstanceIsolation:
    @pytest.mark.asyncio
    async def test_set_active_instance_only_sees_own_sessions(self, monkeypatch):
        """set_active_instance should only offer sessions belonging to the current user."""
        monkeypatch.setattr(config, "http_remote_hosted", True)
        monkeypatch.setattr(config, "transport_mode", "http")
        await _setup_two_user_registry()

        from services.tools.set_active_instance import set_active_instance
        from transport.unity_instance_middleware import UnityInstanceMiddleware
        from tests.integration.test_helpers import DummyContext

        middleware = UnityInstanceMiddleware()
        monkeypatch.setattr(
            "services.tools.set_active_instance.get_unity_instance_middleware",
            lambda: middleware,
        )

        ctx = DummyContext()
        ctx.set_state("user_id", "userA")

        result = await set_active_instance(ctx, "ProjectAlpha@hashA1")
        assert result["success"] is True
        assert middleware.get_active_instance(ctx) == "ProjectAlpha@hashA1"

    @pytest.mark.asyncio
    async def test_set_active_instance_rejects_other_users_instance(self, monkeypatch):
        """set_active_instance should not find another user's instance."""
        monkeypatch.setattr(config, "http_remote_hosted", True)
        monkeypatch.setattr(config, "transport_mode", "http")
        await _setup_two_user_registry()

        from services.tools.set_active_instance import set_active_instance
        from transport.unity_instance_middleware import UnityInstanceMiddleware
        from tests.integration.test_helpers import DummyContext

        middleware = UnityInstanceMiddleware()
        monkeypatch.setattr(
            "services.tools.set_active_instance.get_unity_instance_middleware",
            lambda: middleware,
        )

        ctx = DummyContext()
        ctx.set_state("user_id", "userA")

        # UserA tries to select UserB's instance -> should fail
        result = await set_active_instance(ctx, "ProjectBeta@hashB1")
        assert result["success"] is False
