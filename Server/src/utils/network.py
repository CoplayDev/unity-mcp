def resolve_http_host(
    arg_host: str | None,
    env_host: str | None,
    parsed_host: str | None,
) -> str:
    """Resolve HTTP host from arguments/env/url with sensible defaults."""

    if arg_host:
        return arg_host
    if env_host:
        return env_host

    return parsed_host or "localhost"
