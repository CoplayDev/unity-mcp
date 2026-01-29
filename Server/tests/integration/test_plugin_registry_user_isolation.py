"""Tests for PluginRegistry user-scoped session isolation (remote-hosted mode)."""

import pytest

from core.config import config
from transport.plugin_registry import PluginRegistry


class TestRegistryUserIsolation:
    @pytest.mark.asyncio
    async def test_register_with_user_id_stores_composite_key(self):
        registry = PluginRegistry()
        session = await registry.register(
            "sess-1", "MyProject", "hash1", "2022.3", user_id="user-A"
        )
        assert session.user_id == "user-A"
        assert ("user-A", "hash1") in registry._user_hash_to_session
        assert registry._user_hash_to_session[("user-A", "hash1")] == "sess-1"

    @pytest.mark.asyncio
    async def test_get_session_id_by_hash(self):
        registry = PluginRegistry()
        await registry.register("sess-1", "Proj", "h1", "2022", user_id="uA")

        found = await registry.get_session_id_by_hash("h1", "uA")
        assert found == "sess-1"

        # Different user, same hash -> not found
        not_found = await registry.get_session_id_by_hash("h1", "uB")
        assert not_found is None

    @pytest.mark.asyncio
    async def test_cross_user_isolation_same_hash(self):
        """Two users registering with the same project_hash get independent sessions."""
        registry = PluginRegistry()
        sess_a = await registry.register("sA", "Proj", "hash1", "2022", user_id="userA")
        sess_b = await registry.register("sB", "Proj", "hash1", "2022", user_id="userB")

        assert sess_a.session_id == "sA"
        assert sess_b.session_id == "sB"

        # Each user resolves to their own session
        assert await registry.get_session_id_by_hash("hash1", "userA") == "sA"
        assert await registry.get_session_id_by_hash("hash1", "userB") == "sB"

        # Both sessions exist
        all_sessions = await registry.list_sessions()
        assert len(all_sessions) == 2

    @pytest.mark.asyncio
    async def test_list_sessions_filtered_by_user(self):
        registry = PluginRegistry()
        await registry.register("s1", "ProjA", "hA", "2022", user_id="userA")
        await registry.register("s2", "ProjB", "hB", "2022", user_id="userB")
        await registry.register("s3", "ProjC", "hC", "2022", user_id="userA")

        user_a_sessions = await registry.list_sessions(user_id="userA")
        assert len(user_a_sessions) == 2
        assert "s1" in user_a_sessions
        assert "s3" in user_a_sessions

        user_b_sessions = await registry.list_sessions(user_id="userB")
        assert len(user_b_sessions) == 1
        assert "s2" in user_b_sessions

    @pytest.mark.asyncio
    async def test_list_sessions_no_filter_returns_all_in_local_mode(self):
        """In local mode (not remote-hosted), list_sessions(user_id=None) returns all."""
        registry = PluginRegistry()
        await registry.register("s1", "P1", "h1", "2022", user_id="uA")
        await registry.register("s2", "P2", "h2", "2022", user_id="uB")
        await registry.register("s3", "P3", "h3", "2022")  # local mode, no user_id

        all_sessions = await registry.list_sessions(user_id=None)
        assert len(all_sessions) == 3

    @pytest.mark.asyncio
    async def test_list_sessions_no_filter_raises_in_remote_hosted(self, monkeypatch):
        """In remote-hosted mode, list_sessions(user_id=None) raises ValueError."""
        monkeypatch.setattr(config, "http_remote_hosted", True)

        registry = PluginRegistry()
        await registry.register("s1", "P1", "h1", "2022", user_id="uA")

        with pytest.raises(ValueError, match="requires user_id"):
            await registry.list_sessions(user_id=None)

    @pytest.mark.asyncio
    async def test_unregister_cleans_user_scoped_mapping(self):
        registry = PluginRegistry()
        await registry.register("s1", "Proj", "h1", "2022", user_id="uA")
        assert ("uA", "h1") in registry._user_hash_to_session

        await registry.unregister("s1")

        assert ("uA", "h1") not in registry._user_hash_to_session
        assert "s1" not in (await registry.list_sessions())

    @pytest.mark.asyncio
    async def test_reconnect_replaces_previous_session(self):
        """Same (user_id, hash) re-registered evicts old session, stores new one."""
        registry = PluginRegistry()
        await registry.register("old-sess", "Proj", "h1", "2022", user_id="uA")
        assert await registry.get_session_id_by_hash("h1", "uA") == "old-sess"

        await registry.register("new-sess", "Proj", "h1", "2022", user_id="uA")
        assert await registry.get_session_id_by_hash("h1", "uA") == "new-sess"

        # Old session should be evicted
        all_sessions = await registry.list_sessions()
        assert "old-sess" not in all_sessions
        assert "new-sess" in all_sessions
