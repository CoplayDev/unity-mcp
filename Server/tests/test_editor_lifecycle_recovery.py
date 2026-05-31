from __future__ import annotations

import pytest


def test_send_command_returns_structured_offline_when_no_editor(monkeypatch):
    from transport.legacy import unity_connection as mod

    def missing_connection(_instance_id=None):
        raise ConnectionError(
            "No Unity Editor instances found. Please ensure Unity is running with MCP for Unity bridge."
        )

    monkeypatch.setenv("UNITY_MCP_EDITOR_RECONNECT_MAX_WAIT_S", "0")
    monkeypatch.setattr(mod, "get_unity_connection", missing_connection)

    result = mod.send_command_with_retry("read_console", {}, instance_id="POB@abc")

    assert result.success is False
    assert result.error == "editor_offline"
    assert result.hint == "retry"
    assert result.data["reason"] == "editor_offline"
    assert result.data["retry_after_ms"] == 1000


def test_send_command_retries_until_editor_returns(monkeypatch):
    from transport.legacy import unity_connection as mod

    calls = {"count": 0, "sleep": 0}

    class FakeConnection:
        def send_command(self, command_type, params, max_attempts=None):
            return {
                "success": True,
                "data": {
                    "command_type": command_type,
                    "params": params,
                    "max_attempts": max_attempts,
                },
            }

    def reconnecting_connection(_instance_id=None):
        calls["count"] += 1
        if calls["count"] == 1:
            raise ConnectionError("Failed to connect to Unity instance 'POB@abc' on port 6400.")
        return FakeConnection()

    def fake_sleep(seconds):
        calls["sleep"] += 1

    now = {"value": 0.0}

    def fake_monotonic():
        now["value"] += 0.1
        return now["value"]

    monkeypatch.setenv("UNITY_MCP_EDITOR_RECONNECT_MAX_WAIT_S", "5")
    monkeypatch.setattr(mod, "get_unity_connection", reconnecting_connection)
    monkeypatch.setattr(mod.time, "sleep", fake_sleep)
    monkeypatch.setattr(mod.time, "monotonic", fake_monotonic)

    result = mod.send_command_with_retry("read_console", {"action": "get"}, instance_id="POB@abc")

    assert result["success"] is True
    assert calls["count"] == 2
    assert calls["sleep"] == 1


@pytest.mark.parametrize(
    "message",
    [
        "No Unity Editor instances found.",
        "Failed to connect to Unity instance 'POB@abc' on port 6400.",
        "Could not connect to Unity",
        "[WinError 10061] No connection could be made because the target machine actively refused it",
    ],
)
def test_editor_offline_error_detection(message):
    from transport.legacy.unity_connection import _is_editor_offline_error

    assert _is_editor_offline_error(ConnectionError(message)) is True
