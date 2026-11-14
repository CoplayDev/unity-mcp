"""The command and fast-fail timeouts PluginHub sends to Unity, and their env overrides."""
import asyncio
import time
from unittest.mock import AsyncMock, MagicMock, patch

import pytest

from transport.plugin_hub import PluginHub


def _send(command, params, env, monkeypatch):
    """Sends one command through PluginHub.send_command; returns (timeout sent to Unity, server wait)."""
    for name in ("UNITY_MCP_COMMAND_TIMEOUT_S", "UNITY_MCP_FAST_FAIL_TIMEOUT_S"):
        monkeypatch.delenv(name, raising=False)
    for name, value in env.items():
        monkeypatch.setenv(name, value)

    async def exercise():
        websocket = MagicMock()
        messages = []
        waits = []
        real_wait_for = asyncio.wait_for

        async def send_json(message):
            messages.append(message)
            PluginHub._pending[message["id"]]["future"].set_result({"success": True})

        async def wait_for(future, timeout):
            waits.append(timeout)
            return await real_wait_for(future, timeout)

        websocket.send_json = AsyncMock(side_effect=send_json)
        with patch.object(PluginHub, "_get_connection", AsyncMock(return_value=websocket)), \
             patch.object(PluginHub, "_lock", asyncio.Lock()), \
             patch.object(PluginHub, "_pending", {}), \
             patch("transport.plugin_hub.asyncio.wait_for", side_effect=wait_for):
            result = await PluginHub.send_command("test-session", command, params)
        assert result["success"]
        return messages[0]["timeout"], waits[0]

    return asyncio.run(exercise())


class TestCommandTimeout:
    def test_default_is_the_class_constant(self, monkeypatch):
        assert _send("refresh_unity", {}, {}, monkeypatch) == (30.0, 30.0)

    def test_env_raises_it(self, monkeypatch):
        env = {"UNITY_MCP_COMMAND_TIMEOUT_S": "90"}
        assert _send("refresh_unity", {}, env, monkeypatch) == (90.0, 90.0)

    def test_a_longer_requested_timeout_still_wins(self, monkeypatch):
        env = {"UNITY_MCP_COMMAND_TIMEOUT_S": "90"}
        assert _send("run_tests", {"timeout_seconds": 300}, env, monkeypatch) == (300.0, 305.0)

    def test_env_above_the_ceiling_is_clamped(self, monkeypatch):
        env = {"UNITY_MCP_COMMAND_TIMEOUT_S": "99999"}
        assert _send("refresh_unity", {}, env, monkeypatch) == (3600.0, 3600.0)

    @pytest.mark.parametrize("value", ["0", "-5", "nonsense"])
    def test_a_bad_env_never_gives_a_zero_timeout(self, value, monkeypatch):
        unity_timeout, server_wait = _send(
            "refresh_unity", {}, {"UNITY_MCP_COMMAND_TIMEOUT_S": value}, monkeypatch)
        assert unity_timeout >= 1.0 and server_wait >= 1.0


class TestFastFailTimeout:
    def test_default_is_the_class_constant(self, monkeypatch):
        assert _send("read_console", {}, {}, monkeypatch) == (2.0, 2.0)

    def test_env_raises_it(self, monkeypatch):
        env = {"UNITY_MCP_FAST_FAIL_TIMEOUT_S": "8"}
        assert _send("read_console", {}, env, monkeypatch) == (8.0, 8.0)

    def test_command_env_does_not_touch_fast_fail(self, monkeypatch):
        env = {"UNITY_MCP_COMMAND_TIMEOUT_S": "90"}
        assert _send("get_editor_state", {}, env, monkeypatch) == (2.0, 2.0)

    def test_env_above_the_ceiling_is_clamped(self, monkeypatch):
        env = {"UNITY_MCP_FAST_FAIL_TIMEOUT_S": "500"}
        assert _send("read_console", {}, env, monkeypatch) == (120.0, 120.0)

    def test_nan_keeps_the_default(self, monkeypatch):
        env = {"UNITY_MCP_FAST_FAIL_TIMEOUT_S": "nan"}
        assert _send("read_console", {}, env, monkeypatch) == (2.0, 2.0)


class TestReadinessProbe:
    @pytest.mark.asyncio
    async def test_a_ping_never_outlasts_the_readiness_budget(self, monkeypatch):
        # A raised fast-fail timeout lets one ping wait long; the readiness wait must still end on time.
        monkeypatch.setenv("UNITY_MCP_SESSION_READY_WAIT_SECONDS", "0.3")
        monkeypatch.setenv("UNITY_MCP_FAST_FAIL_TIMEOUT_S", "120")

        async def silent_unity(_session_id, _command, _params):
            await asyncio.sleep(60)

        monkeypatch.setattr(PluginHub, "_resolve_session_id", AsyncMock(return_value="sess-busy"))
        monkeypatch.setattr(PluginHub, "_ensure_live_connection", AsyncMock(return_value=True))
        monkeypatch.setattr(PluginHub, "send_command", silent_unity)

        started = time.monotonic()
        result = await PluginHub.send_command_for_instance(
            unity_instance="Project@hash-busy", command_type="read_console", params={})

        assert time.monotonic() - started < 2.0
        assert result["success"] is False
        assert "ping not answered" in result["error"]
