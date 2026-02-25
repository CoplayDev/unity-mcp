# Queue Summary UI — Design

## Goal

Add a real-time "Queue" tab to the MCP for Unity editor window that displays the command gateway queue state: active heavy job, queue depth, and a scrollable job list with color-coded status.

## Architecture

A new `McpQueueSection` component following the existing section pattern (UXML + USS + C# controller). Integrated as a 6th tab in `MCPForUnityEditorWindow`. Data sourced directly from `CommandGatewayState.Queue` — no network calls, no MCP overhead.

## Layout

```
┌───────────────────────────────────────────────┐
│ Connect │ Tools │ Resources │ Scripts │ Adv │ Q│
├───────────────────────────────────────────────┤
│  Status Bar                                    │
│  ● Heavy: t-000003 (agent-1: "run tests")     │
│  Queued: 2  │  Running: 1  │  Done: 5         │
├───────────────────────────────────────────────┤
│  Job List (ScrollView, most recent first)      │
│  ┌───────────────────────────────────────────┐ │
│  │ ● t-000006  done     agent-2  "refresh"   │ │
│  │ ● t-000005  running  agent-1  "tests" 2/5 │ │
│  │ ● t-000004  queued   agent-3  "build" BLK │ │
│  │ ● t-000003  failed   agent-1  "compile"   │ │
│  │ ● t-000002  done     agent-2  "find"      │ │
│  └───────────────────────────────────────────┘ │
└───────────────────────────────────────────────┘
```

## Components

### Status bar
- Active heavy ticket with agent + label (or "No active heavy job")
- Three counters: Queued / Running / Done+Failed
- Uses existing `.section` and `.setting-row` styles

### Job list
- One row per `BatchJob` from `TicketStore.GetAllJobs()`, most recent first
- Each row: status dot + ticket (monospace) + status text + agent + label + progress
- Status dot colors: green=done, yellow=running, orange=queued, red=failed, grey=cancelled
- "BLOCKED" badge for queued jobs where `CausesDomainReload && IsEditorBusy()`
- Truncated error tooltip for failed jobs

### Refresh
- 1-second `EditorApplication.update` timer, independent of the main 2-second throttle
- Only refreshes while Queue tab is visible (lazy — zero overhead on other tabs)
- Calls `Repaint()` when data changes to update the visual tree

## Data flow

```
CommandGatewayState.Queue
  → TicketStore.GetAllJobs()     → job list rows
  → Queue.HasActiveHeavy         → status bar heavy ticket
  → Queue.QueueDepth             → status bar counter
  → Queue.SmoothInFlight         → status bar counter
  → Queue.IsEditorBusy()         → blocked badge
```

## Files

| Action | Path |
|--------|------|
| Create | `Editor/Windows/Components/Queue/McpQueueSection.cs` |
| Create | `Editor/Windows/Components/Queue/McpQueueSection.uxml` |
| Create | `Editor/Windows/Components/Queue/McpQueueSection.uss` |
| Modify | `Editor/Windows/MCPForUnityEditorWindow.uxml` — add queue tab + panel |
| Modify | `Editor/Windows/MCPForUnityEditorWindow.cs` — add Queue to ActivePanel enum, wire tab, load section, 1s refresh timer |
| Modify | `Editor/Tools/CommandQueue.cs` — expose `GetAllJobs()` (delegates to TicketStore) |

## Testing

Pure editor UI — visual verification only. No unit tests needed.
