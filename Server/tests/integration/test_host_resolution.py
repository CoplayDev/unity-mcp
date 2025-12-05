import tests.integration.conftest  # noqa: F401  # ensure stubs are registered before imports

from core.auth import AuthSettings
from utils.network import resolve_http_host


def test_resolve_host_coerces_wildcard_when_auth_disabled():
    settings = AuthSettings(enabled=False)

    host, coerced = resolve_http_host(settings, arg_host=None, env_host=None, parsed_host="0.0.0.0")

    assert host == "localhost"
    assert coerced is True


def test_resolve_host_respects_explicit_arg_even_when_disabled():
    settings = AuthSettings(enabled=False)

    host, coerced = resolve_http_host(settings, arg_host="0.0.0.0", env_host=None, parsed_host=None)

    assert host == "0.0.0.0"
    assert coerced is False


def test_resolve_host_wildcard_kept_when_auth_enabled():
    settings = AuthSettings(enabled=True)

    host, coerced = resolve_http_host(settings, arg_host=None, env_host=None, parsed_host="0.0.0.0")

    assert host == "0.0.0.0"
    assert coerced is False


def test_resolve_host_defaults_to_localhost_when_missing():
    settings = AuthSettings(enabled=False)

    host, coerced = resolve_http_host(settings, arg_host=None, env_host=None, parsed_host=None)

    assert host == "localhost"
    assert coerced is False
