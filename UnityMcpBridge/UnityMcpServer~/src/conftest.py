def pytest_ignore_collect(path, config):
    # Avoid duplicate import mismatches between Bridge and MCPForUnity copies
    p = str(path)
    return p.endswith("test_telemetry.py")
