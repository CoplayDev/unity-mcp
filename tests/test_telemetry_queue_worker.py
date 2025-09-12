import sys
import pathlib
import importlib.util
import types
import threading
import time
import queue as q


ROOT = pathlib.Path(__file__).resolve().parents[1]
SRC = ROOT / "UnityMcpBridge" / "UnityMcpServer~" / "src"
sys.path.insert(0, str(SRC))

# Stub mcp.server.fastmcp to satisfy imports without the full dependency
mcp_pkg = types.ModuleType("mcp")
server_pkg = types.ModuleType("mcp.server")
fastmcp_pkg = types.ModuleType("mcp.server.fastmcp")

class _Dummy:
    pass

fastmcp_pkg.FastMCP = _Dummy
fastmcp_pkg.Context = _Dummy
server_pkg.fastmcp = fastmcp_pkg
mcp_pkg.server = server_pkg
sys.modules.setdefault("mcp", mcp_pkg)
sys.modules.setdefault("mcp.server", server_pkg)
sys.modules.setdefault("mcp.server.fastmcp", fastmcp_pkg)


def _load_module(path: pathlib.Path, name: str):
    spec = importlib.util.spec_from_file_location(name, path)
    mod = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(mod)
    return mod


telemetry = _load_module(SRC / "telemetry.py", "telemetry_mod")


def test_telemetry_queue_backpressure_and_single_worker(monkeypatch, caplog):
    caplog.set_level("DEBUG")

    collector = telemetry.TelemetryCollector()
    # Force-enable telemetry regardless of env settings from conftest
    collector.config.enabled = True

    # Wake existing worker once so it observes the new queue on the next loop
    collector.record(telemetry.RecordType.TOOL_EXECUTION, {"i": -1})
    # Replace queue with tiny one to trigger backpressure quickly
    small_q = q.Queue(maxsize=2)
    collector._queue = small_q
    # Give the worker a moment to switch queues
    time.sleep(0.02)

    # Make sends slow to build backlog and exercise worker
    def slow_send(self, rec):
        time.sleep(0.05)

    collector._send_telemetry = types.MethodType(slow_send, collector)

    # Fire many events quickly; record() should not block even when queue fills
    start = time.perf_counter()
    for i in range(50):
        collector.record(telemetry.RecordType.TOOL_EXECUTION, {"i": i})
    elapsed_ms = (time.perf_counter() - start) * 1000.0

    # Should be fast despite backpressure (non-blocking enqueue or drop)
    assert elapsed_ms < 80.0

    # Allow worker to process some
    time.sleep(0.3)

    # Verify drops were logged (queue full backpressure)
    dropped_logs = [m for m in caplog.messages if "Telemetry queue full; dropping" in m]
    assert len(dropped_logs) >= 1

    # Ensure only one worker thread exists and is alive
    assert collector._worker.is_alive()
    worker_threads = [t for t in threading.enumerate() if t is collector._worker]
    assert len(worker_threads) == 1


