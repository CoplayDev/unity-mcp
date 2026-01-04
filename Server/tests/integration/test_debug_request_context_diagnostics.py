import pytest


def test_debug_request_context_includes_server_diagnostics(monkeypatch):
    # Import inside test so stubs in conftest are applied.
    import services.tools.debug_request_context as mod

    class DummyCtx:
        # minimal surface for debug_request_context
        request_context = None
        session_id = None
        client_id = None

        def get_state(self, _k):
            return None

    # Ensure get_package_version is stable for assertion
    monkeypatch.setattr(mod, "get_package_version", lambda: "9.9.9-test")

    res = mod.debug_request_context(DummyCtx())
    assert res.get("success") is True
    data = res.get("data") or {}
    server = data.get("server") or {}
    assert server.get("version") == "9.9.9-test"
    assert "cwd" in server
    assert "argv" in server



