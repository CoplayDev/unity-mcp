import pytest


@pytest.mark.asyncio
async def test_debug_request_context_includes_server_diagnostics(monkeypatch):
    # Import inside test so stubs in conftest are applied.
    import services.tools.debug_request_context as mod

    class DummyCtx:
        # minimal surface for debug_request_context
        request_context = None
        session_id = None
        client_id = None

        async def get_state(self, _k):
            return None

    # Ensure get_package_version is stable for assertion
    monkeypatch.setattr(mod, "get_package_version", lambda: "9.9.9-test")

    res = await mod.debug_request_context(DummyCtx())
    assert res.get("success") is True
    data = res.get("data") or {}
    server = data.get("server") or {}
    assert server.get("version") == "9.9.9-test"
    assert "cwd" in server
    assert "argv" in server


@pytest.mark.asyncio
async def test_debug_request_context_redacts_secret_argv(monkeypatch):
    """Any tenant can call this tool on a remote-hosted server, so the service token that
    the server was started with must not come back in the diagnostics."""
    import json
    import services.tools.debug_request_context as mod

    class DummyCtx:
        request_context = None
        session_id = None
        client_id = None

        async def get_state(self, _k):
            return None

    monkeypatch.setattr(mod, "get_package_version", lambda: "9.9.9-test")
    monkeypatch.setattr(mod.sys, "argv", [
        "mcp-for-unity",
        "--http-remote-hosted",
        "--api-key-service-token", "s3cret-service-token",
        "--api-key-validation-url=https://auth.example.com/validate?token=url-embedded",
        "--api-key-service-token-header", "X-Service-Token",
        "--http-port", "8080",
    ])

    res = await mod.debug_request_context(DummyCtx())
    argv = res["data"]["server"]["argv"]
    dumped = json.dumps(argv)

    assert "s3cret-service-token" not in dumped
    assert "url-embedded" not in dumped
    # Flag names and non-secret values survive so the deployment shape stays debuggable.
    assert "--api-key-service-token" in argv
    assert "--http-remote-hosted" in argv
    assert argv[-2:] == ["--http-port", "8080"]


def test_redact_argv_hides_the_value_even_when_it_starts_with_a_dash():
    import services.tools.debug_request_context as mod

    # Secret flags always take a value; a value that begins with "-" is still the secret.
    assert mod._redact_argv(["--token", "-abc123", "--verbose"]) == ["--token", "***", "--verbose"]
    assert mod._redact_argv(["--token", "abc", "--verbose"]) == ["--token", "***", "--verbose"]
    assert mod._redact_argv(["--password=hunter2"]) == ["--password=***"]
    assert mod._redact_argv(["positional", "--port", "1"]) == ["positional", "--port", "1"]



