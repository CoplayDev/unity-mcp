# Queue Summary UI Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Add a real-time "Queue" tab to the MCP for Unity editor window showing command gateway queue state with auto-refresh.

**Architecture:** New `McpQueueSection` UIElements component (UXML + USS + C# controller) integrated as a 6th tab. Data sourced directly from `CommandGatewayState.Queue` with 1-second refresh while visible. Follows the existing section controller pattern (receive root VisualElement, cache elements, expose Refresh).

**Tech Stack:** Unity UIElements (UXML/USS), C# EditorWindow, CommandQueue/TicketStore APIs

**Design doc:** `docs/plans/2026-02-25-queue-summary-ui-design.md`

---

### Task 1: Expose GetAllJobs on CommandQueue

**Files:**
- Modify: `MCPForUnity/Editor/Tools/CommandQueue.cs`

**Context:** `TicketStore.GetAllJobs()` already exists (added in the queue_status commit). CommandQueue needs a public method that delegates to it so the UI section can read job data without reaching into the store directly.

**Step 1: Add GetAllJobs to CommandQueue**

In `MCPForUnity/Editor/Tools/CommandQueue.cs`, add after the `GetSummary()` method (around line 150):

```csharp
/// <summary>
/// Get all jobs ordered by creation time (most recent first).
/// </summary>
public List<BatchJob> GetAllJobs() => _store.GetAllJobs();
```

**Step 2: Compile**

```
refresh_unity(scope=all, compile=request, wait_for_ready=true)
read_console(types=["error"])  → 0 errors
```

**Step 3: Commit**

```bash
git add MCPForUnity/Editor/Tools/CommandQueue.cs
git commit -m "feat(gateway): expose GetAllJobs on CommandQueue for UI consumption"
```

---

### Task 2: Create McpQueueSection UXML layout

**Files:**
- Create: `MCPForUnity/Editor/Windows/Components/Queue/McpQueueSection.uxml`

**Context:** This defines the visual tree for the Queue tab. It has two sections: a status bar with counters, and a scrollable container where job rows are dynamically added by the C# controller.

The UXML follows the same structure as `McpToolsSection.uxml` — a single `.section` wrapper with `.section-title` and `.section-content`.

**Step 1: Create the UXML file**

Create `MCPForUnity/Editor/Windows/Components/Queue/McpQueueSection.uxml`:

```xml
<ui:UXML xmlns:ui="UnityEngine.UIElements" xmlns:uie="UnityEditor.UIElements" editor-extension-mode="True">
    <ui:VisualElement name="queue-section" class="section">
        <ui:Label text="Command Queue" class="section-title" />
        <ui:VisualElement class="section-content">
            <!-- Status bar -->
            <ui:VisualElement name="queue-status-bar" class="queue-status-bar">
                <ui:VisualElement class="setting-row">
                    <ui:Label text="Active Heavy:" class="setting-label" />
                    <ui:Label name="heavy-ticket-label" text="None" class="setting-value" />
                </ui:VisualElement>
                <ui:VisualElement class="queue-counters">
                    <ui:Label name="queued-count" text="Queued: 0" class="queue-counter" />
                    <ui:Label name="running-count" text="Running: 0" class="queue-counter" />
                    <ui:Label name="done-count" text="Done: 0" class="queue-counter" />
                    <ui:Label name="failed-count" text="Failed: 0" class="queue-counter" />
                </ui:VisualElement>
            </ui:VisualElement>
            <!-- Job list header -->
            <ui:VisualElement name="job-list-header" class="queue-job-header">
                <ui:Label text="Ticket" class="queue-col-ticket" />
                <ui:Label text="Status" class="queue-col-status" />
                <ui:Label text="Agent" class="queue-col-agent" />
                <ui:Label text="Label" class="queue-col-label" />
                <ui:Label text="Progress" class="queue-col-progress" />
            </ui:VisualElement>
            <!-- Dynamic job rows inserted here by C# -->
            <ui:VisualElement name="job-list-container" class="queue-job-list" />
            <!-- Empty state -->
            <ui:Label name="empty-label" text="No jobs in queue." class="help-text" />
        </ui:VisualElement>
    </ui:VisualElement>
</ui:UXML>
```

**Step 2: Commit**

```bash
git add MCPForUnity/Editor/Windows/Components/Queue/McpQueueSection.uxml
git commit -m "feat(gateway-ui): add McpQueueSection UXML layout"
```

---

### Task 3: Create McpQueueSection USS styles

**Files:**
- Create: `MCPForUnity/Editor/Windows/Components/Queue/McpQueueSection.uss`

**Context:** Custom styles for the queue tab. Status dot colors, counter row layout, job row grid, monospace ticket text. Reuses existing `.section`, `.section-title`, `.section-content`, `.setting-row`, `.setting-label`, `.setting-value`, `.help-text` from `Common.uss`.

**Step 1: Create the USS file**

Create `MCPForUnity/Editor/Windows/Components/Queue/McpQueueSection.uss`:

```css
/* Status bar */
.queue-status-bar {
    margin-bottom: 8px;
    padding-bottom: 8px;
    border-bottom-width: 1px;
    border-bottom-color: rgba(255, 255, 255, 0.08);
}

/* Counter row */
.queue-counters {
    flex-direction: row;
    margin-top: 4px;
}

.queue-counter {
    font-size: 11px;
    margin-right: 16px;
    color: rgba(180, 180, 180, 1);
}

/* Job list header */
.queue-job-header {
    flex-direction: row;
    padding: 4px 8px;
    margin-bottom: 2px;
    border-bottom-width: 1px;
    border-bottom-color: rgba(255, 255, 255, 0.06);
}

.queue-job-header > Label {
    font-size: 10px;
    -unity-font-style: bold;
    color: rgba(150, 150, 150, 1);
}

/* Job list container */
.queue-job-list {
    flex-direction: column;
}

/* Individual job row */
.queue-job-row {
    flex-direction: row;
    align-items: center;
    padding: 3px 8px;
    margin-bottom: 1px;
    border-radius: 2px;
}

.queue-job-row:hover {
    background-color: rgba(255, 255, 255, 0.04);
}

/* Column widths */
.queue-col-ticket {
    width: 72px;
    min-width: 72px;
    font-size: 11px;
    -unity-font-style: bold;
    overflow: hidden;
}

.queue-col-status {
    width: 70px;
    min-width: 70px;
    font-size: 11px;
}

.queue-col-agent {
    width: 80px;
    min-width: 80px;
    font-size: 11px;
    overflow: hidden;
}

.queue-col-label {
    flex-grow: 1;
    font-size: 11px;
    overflow: hidden;
}

.queue-col-progress {
    width: 50px;
    min-width: 50px;
    font-size: 11px;
    -unity-text-align: middle-right;
}

/* Status dot in job rows */
.queue-status-dot {
    width: 8px;
    height: 8px;
    border-radius: 4px;
    margin-right: 6px;
    flex-shrink: 0;
}

.queue-status-dot.status-queued {
    background-color: rgba(255, 180, 0, 1);
}

.queue-status-dot.status-running {
    background-color: rgba(100, 200, 255, 1);
}

.queue-status-dot.status-done {
    background-color: rgba(0, 200, 100, 1);
}

.queue-status-dot.status-failed {
    background-color: rgba(200, 50, 50, 1);
}

.queue-status-dot.status-cancelled {
    background-color: rgba(150, 150, 150, 1);
}

/* Blocked badge */
.queue-blocked-badge {
    font-size: 9px;
    padding: 1px 4px;
    margin-left: 4px;
    background-color: rgba(255, 100, 50, 0.3);
    border-radius: 3px;
    color: rgba(255, 150, 100, 1);
}

/* Light theme overrides */
.unity-theme-light .queue-status-bar {
    border-bottom-color: rgba(0, 0, 0, 0.1);
}

.unity-theme-light .queue-counter {
    color: rgba(80, 80, 80, 1);
}

.unity-theme-light .queue-job-header {
    border-bottom-color: rgba(0, 0, 0, 0.08);
}

.unity-theme-light .queue-job-header > Label {
    color: rgba(100, 100, 100, 1);
}

.unity-theme-light .queue-job-row:hover {
    background-color: rgba(0, 0, 0, 0.04);
}

.unity-theme-light .queue-blocked-badge {
    background-color: rgba(255, 100, 50, 0.2);
    color: rgba(200, 80, 30, 1);
}
```

**Step 2: Commit**

```bash
git add MCPForUnity/Editor/Windows/Components/Queue/McpQueueSection.uss
git commit -m "feat(gateway-ui): add McpQueueSection USS styles"
```

---

### Task 4: Create McpQueueSection C# controller

**Files:**
- Create: `MCPForUnity/Editor/Windows/Components/Queue/McpQueueSection.cs`

**Context:** This is the main controller. It follows the same pattern as `McpToolsSection`:
1. Constructor receives root VisualElement
2. `CacheUIElements()` via `Q<T>(name)`
3. `Refresh()` reads queue data and rebuilds the job list

Data source: `CommandGatewayState.Queue` — specifically `GetAllJobs()` for the job list, `HasActiveHeavy` and `QueueDepth` for the status bar.

**Step 1: Create the C# controller**

Create `MCPForUnity/Editor/Windows/Components/Queue/McpQueueSection.cs`:

```csharp
using System.Collections.Generic;
using System.Linq;
using MCPForUnity.Editor.Tools;
using UnityEngine.UIElements;

namespace MCPForUnity.Editor.Windows.Components.Queue
{
    /// <summary>
    /// Controller for the Queue tab in the MCP For Unity editor window.
    /// Displays real-time command gateway queue state.
    /// </summary>
    public class McpQueueSection
    {
        private Label heavyTicketLabel;
        private Label queuedCount;
        private Label runningCount;
        private Label doneCount;
        private Label failedCount;
        private VisualElement jobListContainer;
        private VisualElement jobListHeader;
        private Label emptyLabel;

        public VisualElement Root { get; private set; }

        public McpQueueSection(VisualElement root)
        {
            Root = root;
            CacheUIElements();
        }

        private void CacheUIElements()
        {
            heavyTicketLabel = Root.Q<Label>("heavy-ticket-label");
            queuedCount = Root.Q<Label>("queued-count");
            runningCount = Root.Q<Label>("running-count");
            doneCount = Root.Q<Label>("done-count");
            failedCount = Root.Q<Label>("failed-count");
            jobListContainer = Root.Q<VisualElement>("job-list-container");
            jobListHeader = Root.Q<VisualElement>("job-list-header");
            emptyLabel = Root.Q<Label>("empty-label");
        }

        /// <summary>
        /// Refresh the queue display from current queue state.
        /// Called every 1 second while the Queue tab is visible.
        /// </summary>
        public void Refresh()
        {
            var queue = CommandGatewayState.Queue;
            var allJobs = queue.GetAllJobs();

            // Update status bar
            UpdateStatusBar(queue, allJobs);

            // Update job list
            UpdateJobList(allJobs, queue);
        }

        private void UpdateStatusBar(CommandQueue queue, List<BatchJob> allJobs)
        {
            // Active heavy
            if (queue.HasActiveHeavy)
            {
                var running = allJobs.FirstOrDefault(j => j.Status == JobStatus.Running && j.Tier == ExecutionTier.Heavy);
                heavyTicketLabel?.SetText(running != null
                    ? $"{running.Ticket} ({running.Agent}: \"{Truncate(running.Label, 20)}\")"
                    : "Yes (details pending)");
            }
            else
            {
                heavyTicketLabel?.SetText("None");
            }

            // Counters
            int queued = 0, running = 0, done = 0, failed = 0;
            foreach (var j in allJobs)
            {
                switch (j.Status)
                {
                    case JobStatus.Queued: queued++; break;
                    case JobStatus.Running: running++; break;
                    case JobStatus.Done: done++; break;
                    case JobStatus.Failed: failed++; break;
                }
            }

            queuedCount?.SetText($"Queued: {queued}");
            runningCount?.SetText($"Running: {running}");
            doneCount?.SetText($"Done: {done}");
            failedCount?.SetText($"Failed: {failed}");
        }

        private void UpdateJobList(List<BatchJob> allJobs, CommandQueue queue)
        {
            if (jobListContainer == null) return;

            jobListContainer.Clear();

            bool hasJobs = allJobs.Count > 0;
            if (jobListHeader != null)
                jobListHeader.style.display = hasJobs ? DisplayStyle.Flex : DisplayStyle.None;
            if (emptyLabel != null)
                emptyLabel.style.display = hasJobs ? DisplayStyle.None : DisplayStyle.Flex;

            foreach (var job in allJobs)
            {
                var row = CreateJobRow(job, queue);
                jobListContainer.Add(row);
            }
        }

        private VisualElement CreateJobRow(BatchJob job, CommandQueue queue)
        {
            var row = new VisualElement();
            row.AddToClassList("queue-job-row");

            // Status dot
            var dot = new VisualElement();
            dot.AddToClassList("queue-status-dot");
            dot.AddToClassList($"status-{job.Status.ToString().ToLowerInvariant()}");
            row.Add(dot);

            // Ticket
            var ticket = new Label(job.Ticket);
            ticket.AddToClassList("queue-col-ticket");
            row.Add(ticket);

            // Status text
            var statusText = job.Status.ToString().ToLowerInvariant();
            var status = new Label(statusText);
            status.AddToClassList("queue-col-status");
            row.Add(status);

            // Blocked badge (for queued jobs waiting on editor busy)
            if (job.Status == JobStatus.Queued && job.CausesDomainReload && queue.IsEditorBusy())
            {
                var badge = new Label("BLK");
                badge.AddToClassList("queue-blocked-badge");
                row.Add(badge);
            }

            // Agent
            var agent = new Label(Truncate(job.Agent, 12));
            agent.AddToClassList("queue-col-agent");
            agent.tooltip = job.Agent;
            row.Add(agent);

            // Label
            var label = new Label(Truncate(job.Label, 24));
            label.AddToClassList("queue-col-label");
            label.tooltip = job.Label;
            row.Add(label);

            // Progress
            string progressText = "";
            if (job.Commands != null && job.Commands.Count > 0)
            {
                int idx = job.Status == JobStatus.Done ? job.Commands.Count : job.CurrentIndex + 1;
                progressText = $"{idx}/{job.Commands.Count}";
            }
            var progress = new Label(progressText);
            progress.AddToClassList("queue-col-progress");
            row.Add(progress);

            // Tooltip for failed jobs
            if (job.Status == JobStatus.Failed && !string.IsNullOrEmpty(job.Error))
                row.tooltip = $"Error: {job.Error}";

            return row;
        }

        private static string Truncate(string text, int maxLength)
        {
            if (string.IsNullOrEmpty(text)) return "";
            return text.Length <= maxLength ? text : text.Substring(0, maxLength - 1) + "\u2026";
        }
    }

    /// <summary>
    /// Extension to avoid null-check boilerplate on Label.text assignment.
    /// </summary>
    internal static class LabelExtensions
    {
        public static void SetText(this Label label, string text)
        {
            if (label != null) label.text = text;
        }
    }
}
```

**Step 2: Compile**

```
refresh_unity(scope=all, compile=request, wait_for_ready=true)
read_console(types=["error"])  → 0 errors
```

**Step 3: Commit**

```bash
git add MCPForUnity/Editor/Windows/Components/Queue/
git commit -m "feat(gateway-ui): add McpQueueSection controller with status bar and job list"
```

---

### Task 5: Wire Queue tab into the editor window

**Files:**
- Modify: `MCPForUnity/Editor/Windows/MCPForUnityEditorWindow.uxml`
- Modify: `MCPForUnity/Editor/Windows/MCPForUnityEditorWindow.cs`

**Context:** This is the integration task. We need to:
1. Add a "Queue" ToolbarToggle and ScrollView panel to the UXML
2. Add `Queue` to the `ActivePanel` enum
3. Add queue section controller field, tab toggle, panel reference
4. Load the Queue section UXML in `CreateGUI()`
5. Wire the tab in `SetupTabs()` and `SwitchPanel()`
6. Add 1-second refresh timer that only fires while Queue tab is visible
7. Load the Queue USS stylesheet

**Step 1: Modify the UXML**

In `MCPForUnity/Editor/Windows/MCPForUnityEditorWindow.uxml`, add the queue tab toggle after the advanced tab (line 15), and add the queue panel after the resources panel (line 31):

Replace the full UXML content with:

```xml
<ui:UXML xmlns:ui="UnityEngine.UIElements" xmlns:uie="UnityEditor.UIElements" editor-extension-mode="True">
    <ui:VisualElement name="root-container" class="root-layout">
        <ui:VisualElement name="header-bar" class="header-bar">
            <ui:Label text="MCP For Unity" name="title" class="header-title" />
            <ui:Label text="v9.1.0" name="version-label" class="header-version" />
        </ui:VisualElement>
        <ui:VisualElement name="update-notification" class="update-notification">
            <ui:Label name="update-notification-text" class="update-notification-text" />
        </ui:VisualElement>
        <uie:Toolbar name="tab-toolbar" class="tab-toolbar">
            <uie:ToolbarToggle name="clients-tab" text="Connect" value="true" />
            <uie:ToolbarToggle name="tools-tab" text="Tools" />
            <uie:ToolbarToggle name="resources-tab" text="Resources" />
            <uie:ToolbarToggle name="validation-tab" text="Scripts" />
            <uie:ToolbarToggle name="advanced-tab" text="Advanced" />
            <uie:ToolbarToggle name="queue-tab" text="Queue" />
        </uie:Toolbar>
        <ui:ScrollView name="clients-panel" class="panel-scroll" style="flex-grow: 1;">
            <ui:VisualElement name="clients-container" class="section-stack" />
        </ui:ScrollView>
        <ui:ScrollView name="validation-panel" class="panel-scroll hidden" style="flex-grow: 1;">
            <ui:VisualElement name="validation-container" class="section-stack" />
        </ui:ScrollView>
        <ui:ScrollView name="advanced-panel" class="panel-scroll hidden" style="flex-grow: 1;">
            <ui:VisualElement name="advanced-container" class="section-stack" />
        </ui:ScrollView>
        <ui:ScrollView name="tools-panel" class="panel-scroll hidden" style="flex-grow: 1;">
            <ui:VisualElement name="tools-container" class="section-stack" />
        </ui:ScrollView>
        <ui:ScrollView name="resources-panel" class="panel-scroll hidden" style="flex-grow: 1;">
            <ui:VisualElement name="resources-container" class="section-stack" />
        </ui:ScrollView>
        <ui:ScrollView name="queue-panel" class="panel-scroll hidden" style="flex-grow: 1;">
            <ui:VisualElement name="queue-container" class="section-stack" />
        </ui:ScrollView>
    </ui:VisualElement>
</ui:UXML>
```

**Step 2: Modify the C# window**

In `MCPForUnity/Editor/Windows/MCPForUnityEditorWindow.cs`, make these changes:

**2a. Add using directive** (after line 13):

```csharp
using MCPForUnity.Editor.Windows.Components.Queue;
```

**2b. Add section controller field** (after line 29, the `resourcesSection` field):

```csharp
private McpQueueSection queueSection;
```

**2c. Add UI element fields** (after line 45, the `resourcesPanel` field):

```csharp
private ToolbarToggle queueTabToggle;
private VisualElement queuePanel;
```

**2d. Add Queue to ActivePanel enum** (after `Resources` on line 61):

```csharp
Queue
```

**2e. Add queue panel caching in CreateGUI** (after line 156 where `resourcesPanel` is cached):

```csharp
queuePanel = rootVisualElement.Q<VisualElement>("queue-panel");
```

And after line 161 where `resourcesContainer` is cached:

```csharp
var queueContainer = rootVisualElement.Q<VisualElement>("queue-container");
```

**2f. Add null check for queuePanel** (in the null-check block around line 163, add `queuePanel` to the condition):

Add after the existing panel null checks:

```csharp
if (queuePanel == null)
{
    McpLog.Error("Failed to find queue-panel in UXML");
    return;
}
```

And add a null check for queueContainer:

```csharp
if (queueContainer == null)
{
    McpLog.Error("Failed to find queue-container in UXML");
    return;
}
```

**2g. Load Queue section UXML and USS** (after the Resources section loading block, around line 317):

```csharp
// Load and initialize Queue section
var queueTree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(
    $"{basePath}/Editor/Windows/Components/Queue/McpQueueSection.uxml"
);
if (queueTree != null)
{
    var queueRoot = queueTree.Instantiate();
    queueContainer.Add(queueRoot);

    // Load Queue section USS
    var queueStyleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(
        $"{basePath}/Editor/Windows/Components/Queue/McpQueueSection.uss"
    );
    if (queueStyleSheet != null)
        rootVisualElement.styleSheets.Add(queueStyleSheet);

    queueSection = new McpQueueSection(queueRoot);
}
```

**2h. Wire queue tab in SetupTabs** (after line 520 where `resourcesTabToggle` is cached):

```csharp
queueTabToggle = rootVisualElement.Q<ToolbarToggle>("queue-tab");
```

After line 526 (the `resourcesPanel?.RemoveFromClassList("hidden")` line):

```csharp
queuePanel?.RemoveFromClassList("hidden");
```

After the resources tab callback registration (around line 565):

```csharp
if (queueTabToggle != null)
{
    queueTabToggle.RegisterValueChangedCallback(evt =>
    {
        if (evt.newValue) SwitchPanel(ActivePanel.Queue);
    });
}
```

**2i. Update SwitchPanel** — hide queue panel and add Queue case:

In the "Hide all panels" section (around line 600-603), add after the resources hide:

```csharp
if (queuePanel != null)
{
    queuePanel.style.display = DisplayStyle.None;
}
```

In the switch statement (around line 626), add before the closing brace:

```csharp
case ActivePanel.Queue:
    if (queuePanel != null) queuePanel.style.display = DisplayStyle.Flex;
    queueSection?.Refresh();
    break;
```

**2j. Update toggle states in SwitchPanel** (after line 634):

```csharp
queueTabToggle?.SetValueWithoutNotify(panel == ActivePanel.Queue);
```

**2k. Add 1-second queue refresh timer**

Add a new field near the other timing fields (around line 447):

```csharp
private double _lastQueueRefreshTime;
private const double QueueRefreshIntervalSeconds = 1.0;
private ActivePanel _currentPanel = ActivePanel.Clients;
```

In `SwitchPanel`, track the current panel. Add at the end of the method (before the EditorPrefs line):

```csharp
_currentPanel = panel;
```

In `OnEditorUpdate` (around line 474), add after the connection status update (line 489):

```csharp
// Queue tab: 1-second refresh (independent of the 2-second connection throttle)
if (_currentPanel == ActivePanel.Queue && queueSection != null)
{
    if (now - _lastQueueRefreshTime >= QueueRefreshIntervalSeconds)
    {
        _lastQueueRefreshTime = now;
        queueSection.Refresh();
    }
}
```

Note: This uses the `now` variable that's already computed on line 479. The queue refresh runs at 1s but the connection throttle is at 2s, so the queue check must be OUTSIDE the 2s early-return guard. Move the queue refresh check to BEFORE the 2-second guard, or restructure the method:

Actually, the simplest approach: the queue refresh check should happen every frame but internally throttle to 1s. Change `OnEditorUpdate` to:

```csharp
private void OnEditorUpdate()
{
    double now = EditorApplication.timeSinceStartup;

    // Queue tab: 1-second refresh (faster than the general 2-second throttle)
    if (_currentPanel == ActivePanel.Queue && queueSection != null)
    {
        if (now - _lastQueueRefreshTime >= QueueRefreshIntervalSeconds)
        {
            _lastQueueRefreshTime = now;
            queueSection.Refresh();
        }
    }

    // Throttle connection status to 2-second intervals instead of every frame.
    if (now - _lastEditorUpdateTime < EditorUpdateIntervalSeconds)
    {
        return;
    }
    _lastEditorUpdateTime = now;

    if (rootVisualElement == null || rootVisualElement.childCount == 0)
        return;

    connectionSection?.UpdateConnectionStatus();
}
```

**Step 3: Compile**

```
refresh_unity(scope=all, compile=request, wait_for_ready=true)
read_console(types=["error"])  → 0 errors
```

**Step 4: Visual verification**

Open the MCP For Unity window (Window > MCP For Unity). Click the "Queue" tab. Verify:
- Status bar shows "Active Heavy: None", counters all 0
- "No jobs in queue." message visible
- Tab switches cleanly between Queue and other tabs

**Step 5: Commit**

```bash
git add MCPForUnity/Editor/Windows/MCPForUnityEditorWindow.uxml
git add MCPForUnity/Editor/Windows/MCPForUnityEditorWindow.cs
git commit -m "feat(gateway-ui): wire Queue tab into MCP editor window with 1s auto-refresh"
```

---

### Task 6: Integration test — submit jobs and verify live UI

**Context:** This is a manual verification task. No code changes — just confirm the UI updates in real-time.

**Step 1: Submit a batch via MCP**

Use the `batch_execute` tool with `async: true` to submit a heavy job:

```
batch_execute(commands=[{"tool": "run_tests", "params": {"mode": "EditMode", "test_names": ["MCPForUnity.Tests.Editor.CommandQueueTests.Submit_ReturnsJobWithTicket"]}}], async=true)
```

**Step 2: Open Queue tab and observe**

- Job should appear in the list with status "running" (blue dot)
- Active Heavy should show the ticket
- Running counter should show 1
- After completion, status changes to "done" (green dot)
- Active Heavy returns to "None"

**Step 3: Submit a domain-reload job while tests run**

```
batch_execute(commands=[{"tool": "refresh_unity", "params": {"compile": "request"}}], async=true)
```

- Second job should appear as "queued" with "BLK" badge
- After first job completes, second job should start

This confirms the UI reflects real-time queue state.
