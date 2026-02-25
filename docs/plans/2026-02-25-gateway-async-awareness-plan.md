# Gateway Async-Aware Blocking Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Make the command gateway hold domain-reload operations while tests run, and persist queue state across domain reloads.

**Architecture:** Extend `CommandClassifier` to tag commands with `CausesDomainReload`. Add a guard predicate to `CommandQueue.ProcessTick` that blocks reload-causing jobs when `TestJobManager.HasRunningJob` or `EditorApplication.isCompiling`. Persist queue state to `SessionState` following the existing `TestJobManager` pattern.

**Tech Stack:** Unity 6 Editor C# (NUnit, SessionState, Newtonsoft.Json, EditorApplication, AssemblyReloadEvents)

---

### Task 1: Add `CausesDomainReload` to CommandClassifier

**Files:**
- Modify: `MCPForUnity/Editor/Tools/CommandClassifier.cs`
- Modify: `TestProjects/UnityMCPTests/Assets/Tests/EditMode/Tools/CommandClassifierTests.cs`

**Step 1: Write the failing tests**

Add these tests to the bottom of the existing `CommandClassifierTests` class in `TestProjects/UnityMCPTests/Assets/Tests/EditMode/Tools/CommandClassifierTests.cs`:

```csharp
[Test]
public void CausesDomainReload_RefreshUnity_CompileRequest_ReturnsTrue()
{
    var p = new JObject { ["compile"] = "request" };
    Assert.That(CommandClassifier.CausesDomainReload("refresh_unity", p), Is.True);
}

[Test]
public void CausesDomainReload_RefreshUnity_CompileNone_ReturnsFalse()
{
    var p = new JObject { ["compile"] = "none" };
    Assert.That(CommandClassifier.CausesDomainReload("refresh_unity", p), Is.False);
}

[Test]
public void CausesDomainReload_RefreshUnity_NoCompileParam_ReturnsTrue()
{
    var p = new JObject { ["scope"] = "all" };
    Assert.That(CommandClassifier.CausesDomainReload("refresh_unity", p), Is.True);
}

[Test]
public void CausesDomainReload_ManageEditor_Play_ReturnsTrue()
{
    var p = new JObject { ["action"] = "play" };
    Assert.That(CommandClassifier.CausesDomainReload("manage_editor", p), Is.True);
}

[Test]
public void CausesDomainReload_ManageEditor_Stop_ReturnsFalse()
{
    var p = new JObject { ["action"] = "stop" };
    Assert.That(CommandClassifier.CausesDomainReload("manage_editor", p), Is.False);
}

[Test]
public void CausesDomainReload_RunTests_ReturnsFalse()
{
    var p = new JObject { ["mode"] = "EditMode" };
    Assert.That(CommandClassifier.CausesDomainReload("run_tests", p), Is.False);
}

[Test]
public void CausesDomainReload_ManageScript_Create_ReturnsFalse()
{
    var p = new JObject { ["action"] = "create" };
    Assert.That(CommandClassifier.CausesDomainReload("manage_script", p), Is.False);
}

[Test]
public void CausesDomainReload_ManageScene_Load_ReturnsFalse()
{
    var p = new JObject { ["action"] = "load" };
    Assert.That(CommandClassifier.CausesDomainReload("manage_scene", p), Is.False);
}

[Test]
public void CausesDomainReload_NullParams_ReturnsFalse()
{
    Assert.That(CommandClassifier.CausesDomainReload("refresh_unity", null), Is.False);
}
```

**Step 2: Run tests to verify they fail**

Run via Unity MCP:
```
run_tests(mode=EditMode, test_names=[
  "MCPForUnity.Tests.Editor.CommandClassifierTests.CausesDomainReload_RefreshUnity_CompileRequest_ReturnsTrue"
])
```
Expected: compile error — `CausesDomainReload` method doesn't exist yet.

**Step 3: Implement `CausesDomainReload` method**

Add this method to `CommandClassifier` in `MCPForUnity/Editor/Tools/CommandClassifier.cs` after the existing `Classify` method (after line 28):

```csharp
/// <summary>
/// Returns true if the given command would trigger a domain reload (compilation or play mode entry).
/// </summary>
public static bool CausesDomainReload(string toolName, JObject @params)
{
    if (@params == null) return false;

    return toolName switch
    {
        "refresh_unity" => @params.Value<string>("compile") != "none",
        "manage_editor" => @params.Value<string>("action") == "play",
        _ => false
    };
}
```

**Step 4: Run tests to verify they pass**

Run via Unity MCP:
```
run_tests(mode=EditMode, assembly_names=["MCPForUnity.Tests.Editor"])
```
Expected: all `CausesDomainReload_*` tests PASS, existing tests still PASS.

**Step 5: Commit**

```bash
cd /home/liory/Github/unity-mcp-fork
git add MCPForUnity/Editor/Tools/CommandClassifier.cs TestProjects/UnityMCPTests/Assets/Tests/EditMode/Tools/CommandClassifierTests.cs
git commit -m "feat(tools): add CausesDomainReload to CommandClassifier

Only refresh_unity (compile!=none) and manage_editor (play) trigger
domain reload. Script/shader file ops are just disk I/O."
```

---

### Task 2: Add `CausesDomainReload` to BatchCommand and BatchJob

**Files:**
- Modify: `MCPForUnity/Editor/Tools/BatchJob.cs`

**Step 1: Write the failing test**

Add to `TestProjects/UnityMCPTests/Assets/Tests/EditMode/Tools/CommandQueueTests.cs`:

```csharp
[Test]
public void BatchCommand_CausesDomainReload_DefaultsFalse()
{
    var cmd = new BatchCommand { Tool = "find_gameobjects", Params = new JObject(), Tier = ExecutionTier.Instant };
    Assert.That(cmd.CausesDomainReload, Is.False);
}

[Test]
public void BatchJob_CausesDomainReload_DefaultsFalse()
{
    var job = new BatchJob();
    Assert.That(job.CausesDomainReload, Is.False);
}
```

**Step 2: Run tests to verify they fail**

Expected: compile error — `CausesDomainReload` property doesn't exist.

**Step 3: Add the properties**

In `MCPForUnity/Editor/Tools/BatchJob.cs`, add to `BatchCommand` class (after line 36, `public ExecutionTier Tier`):

```csharp
public bool CausesDomainReload { get; set; }
```

Add to `BatchJob` class (after line 18, `public ExecutionTier Tier`):

```csharp
public bool CausesDomainReload { get; set; }
```

**Step 4: Run tests to verify they pass**

Expected: both new tests PASS.

**Step 5: Commit**

```bash
cd /home/liory/Github/unity-mcp-fork
git add MCPForUnity/Editor/Tools/BatchJob.cs TestProjects/UnityMCPTests/Assets/Tests/EditMode/Tools/CommandQueueTests.cs
git commit -m "feat(tools): add CausesDomainReload property to BatchCommand and BatchJob"
```

---

### Task 3: Propagate `CausesDomainReload` in BatchExecute and CommandQueue

**Files:**
- Modify: `MCPForUnity/Editor/Tools/BatchExecute.cs:254-268`
- Modify: `MCPForUnity/Editor/Tools/CommandQueue.cs:31-44`

**Step 1: Write the failing test**

Add to `CommandQueueTests.cs`:

```csharp
[Test]
public void Submit_WithReloadCommand_SetsJobCausesDomainReload()
{
    var cmds = new List<BatchCommand>
    {
        new() { Tool = "find_gameobjects", Params = new JObject(), Tier = ExecutionTier.Instant, CausesDomainReload = false },
        new() { Tool = "refresh_unity", Params = new JObject(), Tier = ExecutionTier.Heavy, CausesDomainReload = true }
    };
    var job = _queue.Submit("agent-1", "test", false, cmds);
    Assert.That(job.CausesDomainReload, Is.True);
}

[Test]
public void Submit_WithoutReloadCommand_JobCausesDomainReloadFalse()
{
    var cmds = new List<BatchCommand>
    {
        new() { Tool = "run_tests", Params = new JObject(), Tier = ExecutionTier.Heavy, CausesDomainReload = false }
    };
    var job = _queue.Submit("agent-1", "test", false, cmds);
    Assert.That(job.CausesDomainReload, Is.False);
}
```

**Step 2: Run tests to verify they fail**

Expected: `Submit_WithReloadCommand_SetsJobCausesDomainReload` fails — `job.CausesDomainReload` is `false`.

**Step 3: Implement propagation**

In `MCPForUnity/Editor/Tools/CommandQueue.cs`, in the `Submit` method, after line 37 (`job.Commands = commands;`), add:

```csharp
job.CausesDomainReload = commands.Any(c => c.CausesDomainReload);
```

In `MCPForUnity/Editor/Tools/BatchExecute.cs`, in `HandleAsyncSubmit`, replace line 267:

```csharp
commands.Add(new BatchCommand { Tool = toolName, Params = cmdParams, Tier = effectiveTier });
```

with:

```csharp
commands.Add(new BatchCommand
{
    Tool = toolName,
    Params = cmdParams,
    Tier = effectiveTier,
    CausesDomainReload = CommandClassifier.CausesDomainReload(toolName, cmdParams)
});
```

**Step 4: Run tests to verify they pass**

Expected: all new and existing tests PASS.

**Step 5: Commit**

```bash
cd /home/liory/Github/unity-mcp-fork
git add MCPForUnity/Editor/Tools/CommandQueue.cs MCPForUnity/Editor/Tools/BatchExecute.cs TestProjects/UnityMCPTests/Assets/Tests/EditMode/Tools/CommandQueueTests.cs
git commit -m "feat(tools): propagate CausesDomainReload through queue submission"
```

---

### Task 4: Add guard logic to ProcessTick

**Files:**
- Modify: `MCPForUnity/Editor/Tools/CommandQueue.cs`
- Modify: `MCPForUnity/Editor/Tools/CommandGatewayState.cs`
- Modify: `TestProjects/UnityMCPTests/Assets/Tests/EditMode/Tools/CommandQueueTests.cs`

**Step 1: Write the failing tests**

Add to `CommandQueueTests.cs`:

```csharp
[Test]
public void ProcessTick_ReloadJob_SkippedWhenEditorBusy()
{
    _queue.IsEditorBusy = () => true;
    var cmds = new List<BatchCommand>
    {
        new() { Tool = "refresh_unity", Params = new JObject(), Tier = ExecutionTier.Heavy, CausesDomainReload = true }
    };
    var job = _queue.Submit("agent-1", "refresh", false, cmds);

    // Tick should NOT start the job because editor is busy
    _queue.ProcessTick(DummyExecutor);
    Assert.That(job.Status, Is.EqualTo(JobStatus.Queued));
    Assert.That(_queue.HasActiveHeavy, Is.False);
}

[Test]
public void ProcessTick_ReloadJob_ProceedsWhenEditorNotBusy()
{
    _queue.IsEditorBusy = () => false;
    var cmds = new List<BatchCommand>
    {
        new() { Tool = "refresh_unity", Params = new JObject(), Tier = ExecutionTier.Heavy, CausesDomainReload = true }
    };
    var job = _queue.Submit("agent-1", "refresh", false, cmds);

    _queue.ProcessTick(DummyExecutor);
    Assert.That(job.Status, Is.Not.EqualTo(JobStatus.Queued));
}

[Test]
public void ProcessTick_NonReloadHeavyJob_ProceedsEvenWhenBusy()
{
    _queue.IsEditorBusy = () => true;
    var cmds = new List<BatchCommand>
    {
        new() { Tool = "run_tests", Params = new JObject(), Tier = ExecutionTier.Heavy, CausesDomainReload = false }
    };
    var job = _queue.Submit("agent-1", "tests", false, cmds);

    _queue.ProcessTick(DummyExecutor);
    Assert.That(job.Status, Is.Not.EqualTo(JobStatus.Queued));
}

[Test]
public void ProcessTick_ReloadJobBehindNonReload_NonReloadProceeds()
{
    _queue.IsEditorBusy = () => true;
    var testCmds = new List<BatchCommand>
    {
        new() { Tool = "run_tests", Params = new JObject(), Tier = ExecutionTier.Heavy, CausesDomainReload = false }
    };
    var refreshCmds = new List<BatchCommand>
    {
        new() { Tool = "refresh_unity", Params = new JObject(), Tier = ExecutionTier.Heavy, CausesDomainReload = true }
    };
    var testJob = _queue.Submit("agent-1", "tests", false, testCmds);
    var refreshJob = _queue.Submit("agent-2", "refresh", false, refreshCmds);

    _queue.ProcessTick(DummyExecutor);
    // test job should start (non-reload), refresh should stay queued
    Assert.That(testJob.Status, Is.Not.EqualTo(JobStatus.Queued));
    Assert.That(refreshJob.Status, Is.EqualTo(JobStatus.Queued));
}
```

**Step 2: Run tests to verify they fail**

Expected: compile error — `IsEditorBusy` property doesn't exist on `CommandQueue`.

**Step 3: Implement the guard**

In `MCPForUnity/Editor/Tools/CommandQueue.cs`:

Add after line 22 (`static readonly TimeSpan TicketExpiry`):

```csharp
/// <summary>
/// Predicate that returns true when domain-reload operations should be deferred.
/// Default always returns false. CommandGatewayState wires this to the real checks.
/// </summary>
public Func<bool> IsEditorBusy { get; set; } = () => false;
```

Replace the heavy queue dequeue block (lines 132-144) with:

```csharp
// 2. If heavy queue has items and no smooth in flight, start next heavy
if (_heavyQueue.Count > 0 && _smoothInFlight.Count == 0)
{
    bool editorBusy = IsEditorBusy();
    // Peek-and-skip: find the first eligible heavy job
    int count = _heavyQueue.Count;
    for (int i = 0; i < count; i++)
    {
        var ticket = _heavyQueue.Dequeue();
        var job = _store.GetJob(ticket);
        if (job == null || job.Status == JobStatus.Cancelled) continue;

        // Guard: domain-reload jobs must wait when editor is busy
        if (job.CausesDomainReload && editorBusy)
        {
            _heavyQueue.Enqueue(ticket); // put back at end
            continue;
        }

        _activeHeavyTicket = ticket;
        _ = ExecuteJob(job, executeCommand);
        // Re-enqueue any remaining peeked items were already handled by the loop
        return;
    }
}
```

In `MCPForUnity/Editor/Tools/CommandGatewayState.cs`, wire the real predicate. Replace the static constructor (lines 14-17):

```csharp
static CommandGatewayState()
{
    Queue.IsEditorBusy = () =>
        MCPForUnity.Editor.Services.TestJobManager.HasRunningJob
        || UnityEditor.EditorApplication.isCompiling;
    EditorApplication.update += OnUpdate;
}
```

Add `using MCPForUnity.Editor.Services;` at the top of `CommandGatewayState.cs`.

**Step 4: Run tests to verify they pass**

Expected: all 4 new guard tests PASS, all existing tests PASS.

**Step 5: Commit**

```bash
cd /home/liory/Github/unity-mcp-fork
git add MCPForUnity/Editor/Tools/CommandQueue.cs MCPForUnity/Editor/Tools/CommandGatewayState.cs TestProjects/UnityMCPTests/Assets/Tests/EditMode/Tools/CommandQueueTests.cs
git commit -m "feat(tools): add domain-reload guard to ProcessTick

Domain-reload-causing jobs (refresh_unity compile, manage_editor play)
are held in queue while TestJobManager.HasRunningJob or
EditorApplication.isCompiling is true. Non-reload heavy jobs proceed
normally."
```

---

### Task 5: Add `blocked_by` to PollJob response

**Files:**
- Modify: `MCPForUnity/Editor/Tools/PollJob.cs`
- Modify: `MCPForUnity/Editor/Tools/CommandQueue.cs`
- Modify: `TestProjects/UnityMCPTests/Assets/Tests/EditMode/Tools/BatchExecuteAsyncTests.cs`

**Step 1: Write the failing test**

Add to `BatchExecuteAsyncTests.cs`:

```csharp
[Test]
public void PollJob_QueuedReloadJob_IncludesBlockedBy_WhenBusy()
{
    var queue = new CommandQueue();
    queue.IsEditorBusy = () => true;
    var cmds = new List<BatchCommand>
    {
        new() { Tool = "refresh_unity", Params = new JObject(), Tier = ExecutionTier.Heavy, CausesDomainReload = true }
    };
    var job = queue.Submit("agent-1", "refresh", false, cmds);

    string reason = queue.GetBlockedReason(job.Ticket);
    Assert.That(reason, Is.Not.Null);
}

[Test]
public void PollJob_QueuedNonReloadJob_BlockedByIsNull()
{
    var queue = new CommandQueue();
    queue.IsEditorBusy = () => true;
    var cmds = new List<BatchCommand>
    {
        new() { Tool = "run_tests", Params = new JObject(), Tier = ExecutionTier.Heavy, CausesDomainReload = false }
    };
    var job = queue.Submit("agent-1", "tests", false, cmds);

    string reason = queue.GetBlockedReason(job.Ticket);
    Assert.That(reason, Is.Null);
}
```

Add required using at top of `BatchExecuteAsyncTests.cs`:
```csharp
using System.Collections.Generic;
```

**Step 2: Run tests to verify they fail**

Expected: compile error — `GetBlockedReason` doesn't exist.

**Step 3: Implement**

Add to `MCPForUnity/Editor/Tools/CommandQueue.cs` after the `GetStatus` method (after line 106):

```csharp
/// <summary>
/// Returns the reason a queued job is blocked, or null if not blocked.
/// </summary>
public string GetBlockedReason(string ticket)
{
    var job = _store.GetJob(ticket);
    if (job == null || job.Status != JobStatus.Queued) return null;
    if (!job.CausesDomainReload) return null;
    if (!IsEditorBusy()) return null;

    if (MCPForUnity.Editor.Services.TestJobManager.HasRunningJob)
        return "tests_running";
    if (UnityEditor.EditorApplication.isCompiling)
        return "compiling";
    return "editor_busy";
}
```

Add `using MCPForUnity.Editor.Services;` at the top of `CommandQueue.cs`.

In `MCPForUnity/Editor/Tools/PollJob.cs`, in the `JobStatus.Queued` case (line 27-47), add `blocked_by` to the data object. Replace the existing `Queued` case with:

```csharp
case JobStatus.Queued:
    var ahead = CommandGatewayState.Queue.GetAheadOf(ticket);
    string blockedBy = CommandGatewayState.Queue.GetBlockedReason(ticket);
    return new PendingResponse(
        blockedBy != null
            ? $"Queued at position {ahead.Count}. Blocked: {blockedBy}."
            : $"Queued at position {ahead.Count}.",
        pollIntervalSeconds: 2.0,
        data: new
        {
            ticket = job.Ticket,
            status = "queued",
            position = ahead.Count,
            blocked_by = blockedBy,
            agent = job.Agent,
            label = job.Label,
            ahead = ahead.ConvertAll(j => (object)new
            {
                ticket = j.Ticket,
                agent = j.Agent,
                label = j.Label,
                tier = j.Tier.ToString().ToLowerInvariant(),
                status = j.Status.ToString().ToLowerInvariant()
            })
        });
```

**Step 4: Run tests to verify they pass**

Expected: all tests PASS.

**Step 5: Commit**

```bash
cd /home/liory/Github/unity-mcp-fork
git add MCPForUnity/Editor/Tools/PollJob.cs MCPForUnity/Editor/Tools/CommandQueue.cs TestProjects/UnityMCPTests/Assets/Tests/EditMode/Tools/BatchExecuteAsyncTests.cs
git commit -m "feat(tools): add blocked_by reason to poll_job response

When a queued job is held because tests are running or compilation
is active, poll_job includes blocked_by: 'tests_running' or 'compiling'."
```

---

### Task 6: Add SessionState persistence to gateway queue

**Files:**
- Modify: `MCPForUnity/Editor/Tools/TicketStore.cs`
- Modify: `MCPForUnity/Editor/Tools/CommandGatewayState.cs`
- Modify: `TestProjects/UnityMCPTests/Assets/Tests/EditMode/Tools/TicketStoreTests.cs`

**Step 1: Write the failing tests**

Add to `TicketStoreTests.cs`:

```csharp
[Test]
public void ToJson_FromJson_RoundTrip_PreservesJobs()
{
    var job = _store.CreateJob("agent-1", "test-label", false, ExecutionTier.Heavy);
    job.CausesDomainReload = true;
    job.Commands = new System.Collections.Generic.List<BatchCommand>
    {
        new() { Tool = "refresh_unity", Params = new JObject { ["compile"] = "request" }, Tier = ExecutionTier.Heavy, CausesDomainReload = true }
    };

    string json = _store.ToJson();
    var restored = new TicketStore();
    restored.FromJson(json);

    var restoredJob = restored.GetJob(job.Ticket);
    Assert.That(restoredJob, Is.Not.Null);
    Assert.That(restoredJob.Agent, Is.EqualTo("agent-1"));
    Assert.That(restoredJob.Label, Is.EqualTo("test-label"));
    Assert.That(restoredJob.CausesDomainReload, Is.True);
    Assert.That(restoredJob.Tier, Is.EqualTo(ExecutionTier.Heavy));
}

[Test]
public void ToJson_FromJson_PreservesNextId()
{
    _store.CreateJob("a", "j1", false, ExecutionTier.Smooth);
    _store.CreateJob("b", "j2", false, ExecutionTier.Heavy);

    string json = _store.ToJson();
    var restored = new TicketStore();
    restored.FromJson(json);

    // Next ticket should not collide
    var j3 = restored.CreateJob("c", "j3", false, ExecutionTier.Instant);
    Assert.That(j3.Ticket, Is.EqualTo("t-000002"));
}

[Test]
public void FromJson_RunningJobs_MarkedFailed()
{
    var job = _store.CreateJob("agent-1", "test", false, ExecutionTier.Heavy);
    job.Status = JobStatus.Running;

    string json = _store.ToJson();
    var restored = new TicketStore();
    restored.FromJson(json);

    var restoredJob = restored.GetJob(job.Ticket);
    Assert.That(restoredJob.Status, Is.EqualTo(JobStatus.Failed));
    Assert.That(restoredJob.Error, Does.Contain("domain reload"));
}

[Test]
public void FromJson_QueuedJobs_StayQueued()
{
    var job = _store.CreateJob("agent-1", "test", false, ExecutionTier.Heavy);
    // Status is Queued by default

    string json = _store.ToJson();
    var restored = new TicketStore();
    restored.FromJson(json);

    var restoredJob = restored.GetJob(job.Ticket);
    Assert.That(restoredJob.Status, Is.EqualTo(JobStatus.Queued));
}

[Test]
public void FromJson_EmptyJson_NoError()
{
    var restored = new TicketStore();
    restored.FromJson("");
    Assert.That(restored.QueueDepth, Is.EqualTo(0));
}

[Test]
public void FromJson_NullJson_NoError()
{
    var restored = new TicketStore();
    restored.FromJson(null);
    Assert.That(restored.QueueDepth, Is.EqualTo(0));
}
```

Add required usings at top of `TicketStoreTests.cs`:
```csharp
using Newtonsoft.Json.Linq;
using System.Collections.Generic;
```

**Step 2: Run tests to verify they fail**

Expected: compile error — `ToJson`/`FromJson` don't exist on `TicketStore`.

**Step 3: Implement serialization on TicketStore**

Add to `MCPForUnity/Editor/Tools/TicketStore.cs`. Add usings at top:

```csharp
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
```

Add these methods after the existing `QueueDepth` property (after line 79):

```csharp
/// <summary>
/// Serialize all jobs and the next-ID counter to JSON for SessionState persistence.
/// </summary>
public string ToJson()
{
    var state = new JObject
    {
        ["next_id"] = _nextId,
        ["jobs"] = new JArray(_jobs.Values.Select(j => new JObject
        {
            ["ticket"] = j.Ticket,
            ["agent"] = j.Agent,
            ["label"] = j.Label,
            ["atomic"] = j.Atomic,
            ["tier"] = (int)j.Tier,
            ["status"] = (int)j.Status,
            ["causes_domain_reload"] = j.CausesDomainReload,
            ["created_at"] = j.CreatedAt.ToString("O"),
            ["completed_at"] = j.CompletedAt?.ToString("O"),
            ["error"] = j.Error,
            ["current_index"] = j.CurrentIndex,
            ["commands"] = new JArray((j.Commands ?? new List<BatchCommand>()).Select(c => new JObject
            {
                ["tool"] = c.Tool,
                ["tier"] = (int)c.Tier,
                ["causes_domain_reload"] = c.CausesDomainReload,
                ["params"] = c.Params
            }))
        }))
    };
    return state.ToString(Formatting.None);
}

/// <summary>
/// Restore jobs from JSON. Running jobs are marked Failed (interrupted by domain reload).
/// </summary>
public void FromJson(string json)
{
    if (string.IsNullOrWhiteSpace(json)) return;

    try
    {
        var state = JObject.Parse(json);
        _nextId = state.Value<int>("next_id");
        _jobs.Clear();

        var jobs = state["jobs"] as JArray;
        if (jobs == null) return;

        foreach (var jt in jobs)
        {
            if (jt is not JObject jo) continue;
            var ticket = jo.Value<string>("ticket");
            if (string.IsNullOrEmpty(ticket)) continue;

            var status = (JobStatus)jo.Value<int>("status");
            string error = jo.Value<string>("error");

            // Running jobs were interrupted by domain reload
            if (status == JobStatus.Running)
            {
                status = JobStatus.Failed;
                error = "Interrupted by domain reload";
            }

            var commands = new List<BatchCommand>();
            if (jo["commands"] is JArray cmds)
            {
                foreach (var ct in cmds)
                {
                    if (ct is not JObject co) continue;
                    commands.Add(new BatchCommand
                    {
                        Tool = co.Value<string>("tool"),
                        Tier = (ExecutionTier)co.Value<int>("tier"),
                        CausesDomainReload = co.Value<bool>("causes_domain_reload"),
                        Params = co["params"] as JObject ?? new JObject()
                    });
                }
            }

            var completedStr = jo.Value<string>("completed_at");

            _jobs[ticket] = new BatchJob
            {
                Ticket = ticket,
                Agent = jo.Value<string>("agent") ?? "anonymous",
                Label = jo.Value<string>("label") ?? "",
                Atomic = jo.Value<bool>("atomic"),
                Tier = (ExecutionTier)jo.Value<int>("tier"),
                Status = status,
                CausesDomainReload = jo.Value<bool>("causes_domain_reload"),
                CreatedAt = DateTime.TryParse(jo.Value<string>("created_at"), out var ca) ? ca : DateTime.UtcNow,
                CompletedAt = !string.IsNullOrEmpty(completedStr) && DateTime.TryParse(completedStr, out var comp) ? comp : null,
                Error = error,
                CurrentIndex = jo.Value<int>("current_index"),
                Commands = commands,
                Results = new List<object>()
            };
        }
    }
    catch
    {
        // Best-effort restore; never block editor load
    }
}
```

**Step 4: Run tests to verify they pass**

Expected: all 6 new serialization tests PASS.

**Step 5: Wire SessionState in CommandGatewayState**

Modify `MCPForUnity/Editor/Tools/CommandGatewayState.cs` to persist and restore. Replace entire file:

```csharp
using MCPForUnity.Editor.Services;
using UnityEditor;

namespace MCPForUnity.Editor.Tools
{
    /// <summary>
    /// Singleton state for the command gateway queue.
    /// Hooks into EditorApplication.update for tick processing.
    /// Persists queue state across domain reloads via SessionState.
    /// </summary>
    [InitializeOnLoad]
    public static class CommandGatewayState
    {
        const string SessionKey = "MCPForUnity.GatewayQueueV1";

        public static readonly CommandQueue Queue = new();

        static CommandGatewayState()
        {
            // Restore queue state from before domain reload
            string json = SessionState.GetString(SessionKey, "");
            if (!string.IsNullOrEmpty(json))
                Queue.RestoreFromJson(json);

            Queue.IsEditorBusy = () =>
                TestJobManager.HasRunningJob
                || EditorApplication.isCompiling;

            AssemblyReloadEvents.beforeAssemblyReload += OnBeforeReload;
            EditorApplication.update += OnUpdate;
        }

        static void OnBeforeReload()
        {
            SessionState.SetString(SessionKey, Queue.PersistToJson());
        }

        static void OnUpdate()
        {
            Queue.ProcessTick(async (tool, @params) =>
                await CommandRegistry.InvokeCommandAsync(tool, @params));
        }
    }
}
```

Add these delegation methods to `MCPForUnity/Editor/Tools/CommandQueue.cs` (after `GetBlockedReason`):

```csharp
/// <summary>
/// Serialize queue state for SessionState persistence.
/// </summary>
public string PersistToJson() => _store.ToJson();

/// <summary>
/// Restore queue state after domain reload. Re-enqueues any queued heavy jobs.
/// </summary>
public void RestoreFromJson(string json)
{
    _store.FromJson(json);

    // Re-populate the heavy queue from restored queued jobs
    foreach (var job in _store.GetQueuedJobs())
    {
        if (job.Tier == ExecutionTier.Heavy)
            _heavyQueue.Enqueue(job.Ticket);
    }
}
```

**Step 6: Run all gateway tests**

Expected: all existing + new tests PASS.

**Step 7: Commit**

```bash
cd /home/liory/Github/unity-mcp-fork
git add MCPForUnity/Editor/Tools/TicketStore.cs MCPForUnity/Editor/Tools/CommandQueue.cs MCPForUnity/Editor/Tools/CommandGatewayState.cs TestProjects/UnityMCPTests/Assets/Tests/EditMode/Tools/TicketStoreTests.cs
git commit -m "feat(tools): persist gateway queue state across domain reloads

TicketStore serializes to JSON via SessionState. Running jobs are
marked Failed on restore (interrupted by domain reload). Queued jobs
survive and re-enter the heavy queue. Follows TestJobManager pattern."
```

---

### Task 7: MCP integration verification

**Files:** None (manual testing only)

**Step 1: Verify compile clean**

```
refresh_unity(scope=all, compile=request, wait_for_ready=true)
read_console(types=["error"])  → 0 errors
```

**Step 2: Run all gateway tests**

```
run_tests(mode=EditMode, assembly_names=["MCPForUnity.Tests.Editor"])
get_test_job(job_id=..., wait_timeout=60, include_failed_tests=true)
```

Expected: 0 failures.

**Step 3: Integration test — concurrent operations**

Test the original scenario:

```
# 1. Submit test run via async gateway
execute_custom_tool("batch_execute", {
  commands: [{tool: "run_tests", params: {mode: "EditMode", assembly_names: ["FineTuner.Tests"]}}],
  async: true, agent: "agent-1", label: "Test Suite Run"
})
→ returns ticket t-NNNNNN

# 2. Immediately submit refresh
execute_custom_tool("batch_execute", {
  commands: [{tool: "refresh_unity", params: {scope: "all", compile: "request"}}],
  async: true, agent: "agent-2", label: "Unity Refresh"
})
→ returns ticket t-NNNNNN+1

# 3. Poll the refresh ticket
execute_custom_tool("batch_execute", {
  commands: [{tool: "poll_job", params: {ticket: "t-NNNNNN+1"}}]
})
→ status: "queued", blocked_by: "tests_running"

# 4. Poll the test ticket
execute_custom_tool("batch_execute", {
  commands: [{tool: "get_test_job", params: {job_id: "..."}}]
})
→ status: "running" or "succeeded"

# 5. After tests complete, poll refresh again
→ status should change from "queued" to "running" or "done"
```

**Step 4: Commit any integration test fixes**

If any issues found, fix and commit with `fix(tools):` prefix.
