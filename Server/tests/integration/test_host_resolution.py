import tests.integration.conftest  # noqa: F401  # ensure stubs are registered before imports

from utils.network import resolve_http_host


def test_resolve_host_prefers_explicit_arg():
    host = resolve_http_host(arg_host="0.0.0.0", env_host="1.2.3.4", parsed_host="5.6.7.8")

    assert host == "0.0.0.0"


def test_resolve_host_prefers_env_when_arg_missing():
    host = resolve_http_host(arg_host=None, env_host="1.2.3.4", parsed_host="5.6.7.8")

    assert host == "1.2.3.4"


def test_resolve_host_falls_back_to_parsed():
    host = resolve_http_host(arg_host=None, env_host=None, parsed_host="5.6.7.8")

    assert host == "5.6.7.8"


def test_resolve_host_defaults_to_localhost():
    host = resolve_http_host(arg_host=None, env_host=None, parsed_host=None)

    assert host == "localhost"
