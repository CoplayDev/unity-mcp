# Gateway Async-Aware Blocking + State Persistence Design

**Goal:** Make the command gateway aware of async Unity operations (test runs, compilation) so domain-reload-causing commands are held until safe, and queue state survives domain reloads.

**Context:** The gateway's `CommandQueue` uses in-memory static state that gets wiped on domain reload. Operations like `refresh_unity` trigger reload, destroying all ticket/queue data. Meanwhile, `run_tests` returns instantly (just submits to Unity's TestRunner) so the gateway treats it as complete even though tests are still executing. A subsequent `refresh_unity` would corrupt the test run.

---

## Problem 1: Async-Aware Blocking

### Classification

Extend `CommandClassifier.Classify` to return a `causesDomainReload` flag alongside the existing `ExecutionTier`.

**Domain-reload operations (only two):**
- `refresh_unity` when `compile != "none"`
- `manage_editor` when `action == "play"`

**Heavy but NOT domain-reload:** `run_tests`, `manage_script` (create/delete is file I/O only), `manage_shader`, `manage_scene` (load/save), `manage_editor` (stop).

Script and shader file operations are disk writes. Domain reload happens only when `refresh_unity` is explicitly called. This allows batching multiple script creates before a single refresh.

### Guard Logic

In `CommandQueue.ProcessTick`, before dequeuing a heavy job:

```
if job.CausesDomainReload
   AND (TestJobManager.HasRunningJob OR EditorApplication.isCompiling)
→ skip, keep queued
```

Non-reload heavy jobs proceed normally. The guard is a predicate added to the existing dequeue check, not a restructure.

### Caller Feedback

`poll_job` response includes `blocked_by` reason when a job is held:

```json
{
  "status": "queued",
  "blocked_by": "tests_running",
  "position": 0
}
```

---

## Problem 2: State Persistence

### Mechanism

Follow the `TestJobManager` pattern: serialize queue state to `SessionState` (survives domain reloads within a single editor session).

**Persisted:**
- Active tickets + status (queued/running/done/failed)
- Agent, label, tier, `causesDomainReload` flag per job
- Commands in each job (tool name + serialized params)
- `_nextId` counter

**Not persisted:**
- Results (consumed once, then expire)
- Smooth/Instant in-flight tracking (ephemeral)

### Lifecycle

- `CommandGatewayState` hooks `AssemblyReloadEvents.beforeAssemblyReload` → serialize to `SessionState`
- `[InitializeOnLoad]` constructor → restore from `SessionState`
- Heavy jobs that were `Running` when reload hit → mark `Failed` ("interrupted by domain reload")

**Key:** `SessionState.SetString("MCPForUnity.GatewayQueueV1", json)`

---

## Files Modified

| File | Change |
|------|--------|
| `CommandClassifier.cs` | Return `(ExecutionTier, bool causesDomainReload)` tuple |
| `BatchCommand.cs` (in BatchJob.cs) | Add `bool CausesDomainReload` property |
| `BatchJob.cs` | Add `bool CausesDomainReload` (true if any command causes reload) |
| `CommandQueue.cs` | Add guard in `ProcessTick`, add serialization methods |
| `CommandGatewayState.cs` | Hook `beforeAssemblyReload`, restore on init |
| `TicketStore.cs` | Add JSON serialization/deserialization |
| `BatchExecute.cs` | Propagate `causesDomainReload` from classifier |
| `PollJob.cs` | Include `blocked_by` in queued response |

## Files Added

| File | Purpose |
|------|---------|
| Tests: `CommandClassifierTests.cs` | Domain-reload classification tests |
| Tests: `CommandQueuePersistenceTests.cs` | SessionState round-trip tests |
| Tests: `CommandQueueGuardTests.cs` | Guard logic: blocked when tests running, proceeds when clear |

---

## Test Plan

**Unit tests:**
- Classifier returns `causesDomainReload=true` for `refresh_unity` (compile=request), `manage_editor` (play)
- Classifier returns `causesDomainReload=false` for `run_tests`, `manage_script` (create), `manage_scene` (load)
- Queue skips domain-reload job when `TestJobManager.HasRunningJob` is true
- Queue proceeds with domain-reload job when tests are not running
- TicketStore JSON round-trip preserves tickets, status, counter
- Interrupted Running jobs marked Failed on restore

**Integration test (manual via MCP):**
1. Submit async batch: `run_tests` → gets ticket, starts
2. Submit async batch: `refresh_unity` (compile=request) → gets ticket, held
3. Poll second ticket → "queued", `blocked_by: "tests_running"`
4. After tests complete → second job proceeds
