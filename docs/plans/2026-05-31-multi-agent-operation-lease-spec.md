# Multi-agent operation lease

Date: 2026-05-31

## Problem

Multiple MCP clients can point at the same Unity project. Expensive editor
operations such as `refresh_unity` and `run_tests` are destructive enough that
parallel starts make the editor slower and less predictable:

- two agents can request refresh/compile at the same time;
- one agent can start tests while another starts a refresh;
- the existing Unity-side busy checks only help after the editor state has
  already changed.

## Goals

- Add a server-side, cross-process guard for a single Unity instance.
- Cover only the small high-impact surface first: `refresh_unity` and the
  `run_tests` start path.
- Fail fast with a structured retry response when another agent owns the
  editor operation window.
- Recover automatically from stale locks left by crashed MCP server processes.

## Non-goals

- No central command queue.
- No client-visible scheduler UI.
- No ordering guarantees across all MCP tools.
- No replacement for Unity's existing `tests_running` or compile-state
  preflight checks.

## Design

The Python server creates one atomic file lease per Unity instance in a shared
per-user directory. The lease file contains owner, operation, PID, token, start
time, and expiry time.

Acquire uses exclusive file creation so separate MCP server processes agree on
one owner. If the file exists and is not expired, the tool returns:

```json
{
  "success": false,
  "error": "operation_busy",
  "hint": "retry",
  "data": {
    "reason": "operation_busy",
    "operation": "refresh_unity",
    "owner": "session:...",
    "retry_after_ms": 2000
  }
}
```

`refresh_unity` holds the lease for the whole refresh/wait operation.

`run_tests` holds the lease only while it performs preflight and starts the
async Unity test job. The actual test execution continues to be represented by
Unity's test state and `get_test_job`; this keeps the change small while
removing the most common race window at job startup.

## Verification

- Unit-test lease acquire, busy response, release, and stale lease recovery.
- Tool-test that `run_tests` and `refresh_unity` return retryable busy responses
  before dispatching to Unity when another owner holds the lease.
- Tool-test that `run_tests` releases the startup lease after dispatch.
