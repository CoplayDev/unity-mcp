import os
import importlib


def test_endpoint_rejects_non_http(tmp_path, monkeypatch):
    # Point data dir to temp to avoid touching real files
    monkeypatch.setenv("XDG_DATA_HOME", str(tmp_path))
    monkeypatch.setenv("UNITY_MCP_TELEMETRY_ENDPOINT", "file:///etc/passwd")

    # Import the telemetry module from the correct path
    import sys
    import pathlib
    ROOT = pathlib.Path(__file__).resolve().parents[1]
    SRC = ROOT / "MCPForUnity" / "UnityMcpServer~" / "src"
    sys.path.insert(0, str(SRC))
    
    monkeypatch.chdir(str(SRC))
    telemetry = importlib.import_module("telemetry")
    importlib.reload(telemetry)

    tc = telemetry.TelemetryCollector()
    # Should have fallen back to default endpoint
    assert tc.config.endpoint == tc.config.default_endpoint


def test_config_preferred_then_env_override(tmp_path, monkeypatch):
    # Simulate config telemetry endpoint
    monkeypatch.setenv("XDG_DATA_HOME", str(tmp_path))
    monkeypatch.delenv("UNITY_MCP_TELEMETRY_ENDPOINT", raising=False)

    # Patch config.telemetry_endpoint via import mocking
    import importlib
    import sys
    import pathlib
    ROOT = pathlib.Path(__file__).resolve().parents[1]
    SRC = ROOT / "MCPForUnity" / "UnityMcpServer~" / "src"
    sys.path.insert(0, str(SRC))
    
    cfg_mod = importlib.import_module("config")
    old_endpoint = cfg_mod.config.telemetry_endpoint
    cfg_mod.config.telemetry_endpoint = "https://example.com/telemetry"
    try:
        monkeypatch.chdir(str(SRC))
        telemetry = importlib.import_module("telemetry")
        importlib.reload(telemetry)
        tc = telemetry.TelemetryCollector()
        # When no env override is set, config endpoint is preferred
        assert tc.config.endpoint == "https://example.com/telemetry"

        # Env should override config
        monkeypatch.setenv("UNITY_MCP_TELEMETRY_ENDPOINT",
                           "https://override.example/ep")
        monkeypatch.chdir(str(SRC))
        importlib.reload(telemetry)
        tc2 = telemetry.TelemetryCollector()
        assert tc2.config.endpoint == "https://override.example/ep"
    finally:
        cfg_mod.config.telemetry_endpoint = old_endpoint


def test_uuid_preserved_on_malformed_milestones(tmp_path, monkeypatch):
    monkeypatch.setenv("XDG_DATA_HOME", str(tmp_path))

    # Import the telemetry module from the correct path
    import sys
    import pathlib
    ROOT = pathlib.Path(__file__).resolve().parents[1]
    SRC = ROOT / "MCPForUnity" / "UnityMcpServer~" / "src"
    sys.path.insert(0, str(SRC))
    
    monkeypatch.chdir(str(SRC))
    telemetry = importlib.import_module("telemetry")
    importlib.reload(telemetry)

    tc1 = telemetry.TelemetryCollector()
    first_uuid = tc1._customer_uuid

    # Write malformed milestones
    tc1.config.milestones_file.write_text("{not-json}", encoding="utf-8")

    # Reload collector; UUID should remain same despite bad milestones
    importlib.reload(telemetry)
    tc2 = telemetry.TelemetryCollector()
    assert tc2._customer_uuid == first_uuid
