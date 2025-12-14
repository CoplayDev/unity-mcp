import types

import pytest
import tests.integration.conftest  # noqa: F401  # ensure stubs are registered before imports

from core.auth import auth_settings, verify_http_request, verify_websocket


class _DummyRequest:
    def __init__(self, host: str | None, headers: dict[str, str] | None = None):
        self.client = types.SimpleNamespace(host=host)
        self.headers = headers or {}


class _DummyWebSocket:
    def __init__(self, host: str | None, headers: list[tuple[str, str]] | None = None):
        self.client = types.SimpleNamespace(host=host)
        self.headers = headers or []


def test_http_allowlist_allows_any_ip_when_wildcard():
    settings = auth_settings(token="secret", allowed_ips=["*"])
    request = _DummyRequest("192.168.1.10", headers={"X-API-Key": "secret"})

    response = verify_http_request(request, settings)

    assert response is None


def test_http_allowlist_blocks_outside_cidr():
    settings = auth_settings(token="secret", allowed_ips=["10.0.0.0/8"])
    request = _DummyRequest("192.168.1.10", headers={"X-API-Key": "secret"})

    response = verify_http_request(request, settings)

    assert response is not None
    assert response.status_code == 403


def test_http_requires_matching_token_when_set():
    settings = auth_settings(token="secret", allowed_ips=["*"])
    request = _DummyRequest("127.0.0.1", headers={"X-API-Key": "secret"})

    response = verify_http_request(request, settings)

    assert response is None


def test_http_blocks_missing_token_when_required():
    settings = auth_settings(token="secret", allowed_ips=["127.0.0.1"])
    request = _DummyRequest("127.0.0.1")

    response = verify_http_request(request, settings)

    assert response is not None
    assert response.status_code == 401


@pytest.mark.asyncio
async def test_websocket_checks_token_and_allowlist():
    settings = auth_settings(token="secret", allowed_ips=["10.0.0.0/8"])
    websocket = _DummyWebSocket("10.1.2.3", headers=[("x-api-key", "secret")])

    response = await verify_websocket(websocket, settings)

    assert response is None


@pytest.mark.asyncio
async def test_websocket_blocks_invalid_token():
    settings = auth_settings(token="secret", allowed_ips=["10.0.0.0/8"])
    websocket = _DummyWebSocket("10.1.2.3", headers=[("x-api-key", "wrong")])

    response = await verify_websocket(websocket, settings)

    assert response is not None
    assert response.status_code == 401


def test_http_no_auth_when_disabled():
    settings = auth_settings(enabled=False, allowed_ips=["127.0.0.1"])
    request = _DummyRequest("192.168.1.10")

    response = verify_http_request(request, settings)

    assert response is None


def test_http_no_token_required_when_empty():
    settings = auth_settings(token="", allowed_ips=["*"], enabled=True)
    request = _DummyRequest("127.0.0.1")

    response = verify_http_request(request, settings)

    assert response is None
