import json
import importlib
import logging
import os
from types import SimpleNamespace
from unittest.mock import AsyncMock, Mock

import pytest
from core.config import config


class RecordingMcp:
    def __init__(self, **kwargs):
        self.routes = {}
        self._additional_http_routes = []

    def custom_route(self, path, methods=None):
        def register(fn):
            self.routes[path] = fn
            return fn
        return register

    def add_middleware(self, middleware):
        pass

    def _get_additional_http_routes(self):
        return self._additional_http_routes


@pytest.fixture
def server(monkeypatch):
    # Importing the entry point configures process-wide logging and telemetry.
    # Keep collection and other tests independent of these startup side effects.
    names = ("", "mcp-for-unity-server", "unity-mcp-telemetry", "httpx", "urllib3", "mcp.server.lowlevel.server")
    loggers = [logging.getLogger(name) for name in names]
    original_handlers = {handler for logger in loggers for handler in logger.handlers}
    with monkeypatch.context() as startup:
        startup.setattr(logging, "basicConfig", Mock())
        startup.setenv("UNITY_MCP_TELEMETRY_TIMEOUT", os.environ.get("UNITY_MCP_TELEMETRY_TIMEOUT", "1.5"))
        for logger in loggers:
            startup.setattr(logger, "handlers", list(logger.handlers))
            startup.setattr(logger, "level", logger.level)
            startup.setattr(logger, "propagate", logger.propagate)
        try:
            main = importlib.import_module("main")
        finally:
            for handler in {handler for logger in loggers for handler in logger.handlers} - original_handlers:
                handler.close()
    monkeypatch.setattr(main, "FastMCP", RecordingMcp)
    monkeypatch.setattr(main, "CustomToolService", Mock())
    monkeypatch.setattr(main, "custom_tool_service", None)
    monkeypatch.setattr(main, "register_all_tools", Mock())
    monkeypatch.setattr(main, "register_all_resources", Mock())
    monkeypatch.setattr(main, "get_unity_instance_middleware", Mock())
    monkeypatch.setattr(config, "api_key_validation_url", "")
    return main


def snapshot(status):
    return {"status": "success", "result": {"success": True, "data": {
        "job_id": "startup-job", "unity_instance": "hash-a", "status": status}}}


@pytest.mark.asyncio
async def test_rest_startup_keeps_project_hash_when_websocket_session_changes(server, monkeypatch):
    main = server
    monkeypatch.setattr(config, "http_remote_hosted", False)
    sessions = {"old-session": SimpleNamespace(hash="hash-a", project="ProjectA"),
        "other-session": SimpleNamespace(hash="hash-b", project="ProjectB")}
    monkeypatch.setattr(main.PluginHub, "get_sessions", AsyncMock(return_value=SimpleNamespace(sessions=sessions)))
    send = AsyncMock(side_effect=[snapshot("waiting"), {"success": False, "hint": "retry"}, snapshot("completed")])
    raw_send = AsyncMock()
    monkeypatch.setattr(main.PluginHub, "send_command_for_instance", send)
    monkeypatch.setattr(main.PluginHub, "send_command", raw_send)
    mcp = main.create_mcp_server(False)
    request = SimpleNamespace(json=AsyncMock(return_value={"type": "manage_editor",
        "unity_instance": "ProjectA@hash-a", "params": {"action": "play", "onReadyCode": "return 42;"}}))

    response = await mcp.routes["/api/command"](request)

    assert response.status_code == 200
    assert json.loads(response.body)["data"]["status"] == "completed"
    assert send.await_count == 3
    assert all(call.args[0] == "hash-a" and call.kwargs == {"retry_on_reload": False} for call in send.await_args_list)
    assert [call.args[2]["action"] for call in send.await_args_list] == ["play", "get_play_mode_startup", "get_play_mode_startup"]
    raw_send.assert_not_awaited()


@pytest.mark.asyncio
async def test_rest_default_play_preserves_old_route(server, monkeypatch):
    main = server
    monkeypatch.setattr(config, "http_remote_hosted", False)
    monkeypatch.setattr(main.PluginHub, "get_sessions", AsyncMock(return_value=SimpleNamespace(sessions={
        "session-a": SimpleNamespace(hash="hash-a", project="ProjectA")})))
    raw = snapshot("completed")
    send = AsyncMock(return_value=raw)
    monkeypatch.setattr(main.PluginHub, "send_command", send)
    mcp = main.create_mcp_server(False)
    request = SimpleNamespace(json=AsyncMock(return_value={"type": "manage_editor", "params": {"action": "play"}}))
    response = await mcp.routes["/api/command"](request)
    assert json.loads(response.body) == raw
    send.assert_awaited_once_with("session-a", "manage_editor", {"action": "play"})


def test_hosted_server_does_not_expose_startup_or_other_cli_routes(server, monkeypatch):
    monkeypatch.setattr(config, "http_remote_hosted", True)
    assert "/api/command" not in server.create_mcp_server(False).routes
