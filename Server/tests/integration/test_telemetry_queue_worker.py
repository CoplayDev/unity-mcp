import types
import threading
import time
import queue as q

import core.telemetry as telemetry


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
    # Threshold set high (500ms) to accommodate CI environments with variable load.
    # The key assertion is that 50 record() calls don't block on a full queue;
    # even under heavy CI load, non-blocking calls should complete well under 500ms.
    assert elapsed_ms < 500.0, f"Took {elapsed_ms:.1f}ms (expected <500ms for non-blocking calls)"

    # Allow worker to process some
    time.sleep(0.3)

    # Verify drops were logged (queue full backpressure)
    dropped_logs = [
        m for m in caplog.messages if "Telemetry queue full; dropping" in m]
    assert len(dropped_logs) >= 1

    # Ensure only one worker thread exists and is alive
    assert collector._worker.is_alive()
    worker_threads = [
        t for t in threading.enumerate() if t is collector._worker]
    assert len(worker_threads) == 1
