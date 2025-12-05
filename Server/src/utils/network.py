from core.auth import AuthSettings


def resolve_http_host(
    auth_settings: AuthSettings,
    arg_host: str | None,
    env_host: str | None,
    parsed_host: str | None,
) -> tuple[str, bool]:
    """Resolve HTTP host, defaulting to localhost when auth is disabled.

    Returns (host, coerced) where coerced indicates we replaced a wildcard host
    because auth is off and no explicit host was provided.
    """
    if arg_host:
        return arg_host, False
    if env_host:
        return env_host, False

    host = parsed_host or "localhost"
    if not auth_settings.enabled and host in {"0.0.0.0", "::", "[::]", ""}:
        return "localhost", True
    return host, False
