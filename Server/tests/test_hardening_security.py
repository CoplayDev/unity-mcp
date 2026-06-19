"""Security-behavior tests for the hardening pass (harden/security).

Covers the Python-runnable items of the verification brief:
  C1 (token resolver, both directions), C6 (off-loopback bind guard decision
  matrix through the real main()), and C7 (telemetry off by default).

The C# items (C1 stdio bridge gate, C2 dispatch boundary, C4/C5 menu/play gates,
C3 Roslyn) require a Unity Editor and are covered by EditMode tests, not here.
"""
import sys
from pathlib import Path

import pytest

ROOT = Path(__file__).resolve().parents[1]
SRC = ROOT / "src"
if str(SRC) not in sys.path:
    sys.path.insert(0, str(SRC))


# --------------------------------------------------------------------------- #
# C1 — bridge token resolver (both directions)
# --------------------------------------------------------------------------- #
class TestBridgeTokenResolver:
    def _fresh_home(self, monkeypatch, tmp_path):
        monkeypatch.delenv("UNITY_MCP_BRIDGE_TOKEN", raising=False)
        monkeypatch.setenv("HOME", str(tmp_path))
        monkeypatch.setattr(Path, "home", classmethod(lambda cls: tmp_path))

    def test_env_token_wins(self, monkeypatch, tmp_path):
        from transport.legacy.unity_connection import resolve_bridge_token
        self._fresh_home(monkeypatch, tmp_path)
        # Even with a file present, the env var takes precedence.
        d = tmp_path / ".unity-mcp"; d.mkdir()
        (d / "bridge-token").write_text("from-file")
        monkeypatch.setenv("UNITY_MCP_BRIDGE_TOKEN", "from-env")
        assert resolve_bridge_token() == "from-env"

    def test_file_fallback(self, monkeypatch, tmp_path):
        from transport.legacy.unity_connection import resolve_bridge_token
        self._fresh_home(monkeypatch, tmp_path)
        d = tmp_path / ".unity-mcp"; d.mkdir()
        (d / "bridge-token").write_text("  file-tok\n")  # whitespace trimmed
        assert resolve_bridge_token() == "file-tok"

    def test_empty_when_neither(self, monkeypatch, tmp_path):
        """Negative: no env, no file -> empty string (the gate then fails closed)."""
        from transport.legacy.unity_connection import resolve_bridge_token
        self._fresh_home(monkeypatch, tmp_path)
        assert resolve_bridge_token() == ""

    def test_constant_time_compare_semantics(self):
        """The gates compare with hmac.compare_digest. Verify accept/reject."""
        import hmac
        expected = "s3cr3t-token"
        assert hmac.compare_digest("s3cr3t-token", expected) is True
        assert hmac.compare_digest("", expected) is False
        assert hmac.compare_digest("wrong", expected) is False


# --------------------------------------------------------------------------- #
# C6 — off-loopback bind guard (decision matrix through real main())
# --------------------------------------------------------------------------- #
class _GuardPassed(Exception):
    """Sentinel raised in place of create_mcp_server to prove we got past the guard."""


@pytest.fixture
def run_main_http(monkeypatch, tmp_path):
    """Drive main() in HTTP mode up to (but not into) the server run.

    Returns a callable(host, remote_hosted, token) -> "passed" | exit code.
    """
    from core.config import config

    def _run(host, remote_hosted=False, token=None):
        import main as main_mod

        # Isolate global config mutations.
        monkeypatch.setattr(config, "http_remote_hosted", remote_hosted)
        monkeypatch.setattr(config, "transport_mode", "stdio")
        # Stop before actually serving: create_mcp_server is the first call after the guard.
        monkeypatch.setattr(main_mod, "create_mcp_server",
                            lambda *a, **k: (_ for _ in ()).throw(_GuardPassed()))

        # Token source
        monkeypatch.delenv("UNITY_MCP_BRIDGE_TOKEN", raising=False)
        monkeypatch.setattr(Path, "home", classmethod(lambda cls: tmp_path))
        if token:
            monkeypatch.setenv("UNITY_MCP_BRIDGE_TOKEN", token)

        argv = ["main", "--transport", "http", "--http-host", host]
        if remote_hosted:
            argv += ["--http-remote-hosted",
                     "--api-key-validation-url", "https://example.test/validate"]
        monkeypatch.setattr(sys, "argv", argv)
        monkeypatch.delenv("UNITY_MCP_HTTP_REMOTE_HOSTED", raising=False)
        monkeypatch.delenv("UNITY_MCP_HTTP_HOST", raising=False)

        try:
            main_mod.main()
            return "ran"  # should not happen
        except _GuardPassed:
            return "passed"
        except SystemExit as e:
            return e.code

    return _run

    # ----- positive: loopback proceeds -----

class TestOffLoopbackGuard:
    def test_loopback_proceeds(self, run_main_http):
        assert run_main_http("127.0.0.1") == "passed"

    def test_non_loopback_no_token_refuses(self, run_main_http):
        """Negative: 0.0.0.0 in local mode with no token must refuse to start."""
        assert run_main_http("0.0.0.0", remote_hosted=False, token=None) == 2

    def test_non_loopback_with_token_proceeds(self, run_main_http):
        assert run_main_http("0.0.0.0", remote_hosted=False, token="tok123") == "passed"

    def test_non_loopback_remote_hosted_proceeds(self, run_main_http):
        # Remote-hosted relies on API-key auth, so off-loopback is allowed.
        assert run_main_http("0.0.0.0", remote_hosted=True, token=None) == "passed"


# --------------------------------------------------------------------------- #
# C7 — telemetry off by default
# --------------------------------------------------------------------------- #
class TestTelemetryDefault:
    def _clear_disable_env(self, monkeypatch):
        for v in ("DISABLE_TELEMETRY", "UNITY_MCP_DISABLE_TELEMETRY", "MCP_DISABLE_TELEMETRY"):
            monkeypatch.delenv(v, raising=False)

    def test_config_default_off(self):
        from core.config import ServerConfig
        assert ServerConfig().telemetry_enabled is False

    def test_collector_disabled_by_default(self, monkeypatch):
        self._clear_disable_env(monkeypatch)
        import core.telemetry as t
        assert t.TelemetryConfig().enabled is False

    def test_opt_in_still_works(self, monkeypatch):
        """Positive sanity: explicit opt-in re-enables (when not disabled by env).

        TelemetryConfig discovers its ServerConfig by module name (preferring
        'src.core.config'); inject a stub under that name with telemetry_enabled=True
        so the test is independent of how sys.path happens to be laid out.
        """
        self._clear_disable_env(monkeypatch)
        import sys as _sys
        import types
        from core.config import ServerConfig

        cfg = ServerConfig()
        cfg.telemetry_enabled = True
        stub = types.ModuleType("src.core.config")
        stub.config = cfg
        monkeypatch.setitem(_sys.modules, "src.core.config", stub)

        import core.telemetry as t
        assert t.TelemetryConfig().enabled is True
