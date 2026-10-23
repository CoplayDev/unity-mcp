"""Only authenticated local requests may trigger cooperative shutdown."""
import os
import signal
from unittest.mock import Mock

import pytest
from starlette.requests import Request
from starlette.routing import Route

from core.config import config


@pytest.fixture
def shutdown_endpoint_factory(monkeypatch):
    import main

    # Integration collection installs a FastMCP stub; collect the real handlers independently.
    server = Mock()
    server._additional_http_routes = []
    server._get_additional_http_routes.side_effect = lambda: list(server._additional_http_routes)

    def custom_route(path, methods):
        def register(handler):
            server._additional_http_routes.append(Route(path, handler, methods=methods))
            return handler
        return register

    server.custom_route.side_effect = custom_route
    monkeypatch.setattr(main, "FastMCP", Mock(return_value=server))
    monkeypatch.setattr(main, "CustomToolService", Mock())
    monkeypatch.setattr(main, "custom_tool_service", None)
    monkeypatch.setattr(main, "get_unity_instance_middleware", Mock())
    monkeypatch.setattr(main, "register_all_tools", Mock())
    monkeypatch.setattr(main, "register_all_resources", Mock())

    def create_endpoint():
        app = main.create_mcp_server(False)
        return next(r.endpoint for r in app._get_additional_http_routes() if r.path == "/_unity/shutdown")

    return create_endpoint


@pytest.fixture
def shutdown(monkeypatch, shutdown_endpoint_factory):
    monkeypatch.setenv("UNITY_MCP_INSTANCE_TOKEN", "launch-token")
    monkeypatch.setattr(config, "http_remote_hosted", False)
    raised = Mock()
    monkeypatch.setattr(signal, "getsignal", lambda _: Mock())
    monkeypatch.setattr(signal, "raise_signal", raised)
    return shutdown_endpoint_factory(), raised


def request(host="127.0.0.1", token="launch-token"):
    return Request({"type": "http", "method": "POST", "path": "/_unity/shutdown",
                    "client": (host, 1234), "headers": [(b"x-unity-instance-token", token.encode())]})


@pytest.mark.asyncio
@pytest.mark.parametrize("host", ["127.0.0.1", "::1"])
async def test_shutdown_defers_signal_until_after_response(shutdown, host):
    endpoint, raised = shutdown
    response = await endpoint(request(host))
    assert response.status_code == 202
    assert str(os.getpid()).encode() in response.body
    raised.assert_not_called()
    await response.background()
    raised.assert_called_once_with(signal.SIGINT)


@pytest.mark.asyncio
@pytest.mark.parametrize("host,token", [
    ("127.0.0.1", ""), ("127.0.0.1", "wrong"), ("127.0.0.1", "\u00e9"),
    ("192.0.2.1", "launch-token"), ("unknown", "launch-token"),
])
async def test_shutdown_rejects_untrusted_requests(shutdown, host, token):
    endpoint, raised = shutdown
    assert (await endpoint(request(host, token))).status_code == 403
    raised.assert_not_called()


@pytest.mark.asyncio
async def test_shutdown_rejects_remote_hosted_server(shutdown, monkeypatch):
    endpoint, raised = shutdown
    monkeypatch.setattr(config, "http_remote_hosted", True)
    assert (await endpoint(request())).status_code == 403
    raised.assert_not_called()


@pytest.mark.asyncio
@pytest.mark.parametrize("handler", [signal.SIG_DFL, signal.default_int_handler])
async def test_shutdown_requires_installed_handler(shutdown, monkeypatch, handler):
    endpoint, raised = shutdown
    monkeypatch.setattr(signal, "getsignal", lambda _: handler)
    assert (await endpoint(request())).status_code == 503
    raised.assert_not_called()


@pytest.mark.asyncio
async def test_repeated_shutdown_does_not_force_uvicorn_exit(shutdown):
    endpoint, raised = shutdown
    first = await endpoint(request())
    second = await endpoint(request())
    await first.background()
    await second.background()
    raised.assert_called_once_with(signal.SIGINT)


@pytest.mark.asyncio
async def test_shutdown_disabled_without_launch_token(monkeypatch, shutdown_endpoint_factory):
    monkeypatch.delenv("UNITY_MCP_INSTANCE_TOKEN", raising=False)
    endpoint = shutdown_endpoint_factory()
    assert (await endpoint(request())).status_code == 403
