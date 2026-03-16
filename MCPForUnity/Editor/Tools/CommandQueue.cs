using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MCPForUnity.Editor.Helpers;
using Newtonsoft.Json.Linq;
using UnityEditor;

namespace MCPForUnity.Editor.Tools
{
    /// <summary>
    /// Tier-aware command queue. Processes jobs via EditorApplication.update.
    /// Instant: execute inline. Smooth: execute when no heavy active. Heavy: exclusive.
    /// </summary>
    public class CommandQueue
    {
        readonly TicketStore _store = new();
        readonly Queue<string> _heavyQueue = new();
        readonly List<string> _smoothInFlight = new();
        string _activeHeavyTicket;

        static readonly TimeSpan TicketExpiry = TimeSpan.FromMinutes(5);

        // Watchdog: detect when IsEditorBusy blocks the queue for too long
        static readonly TimeSpan BusyWatchdogTimeout = TimeSpan.FromSeconds(90);
        DateTime _busyBlockedSince = DateTime.MaxValue;
        bool _watchdogWarned;

        /// <summary>
        /// Predicate that returns true when domain-reload operations should be deferred.
        /// Default always returns false. CommandGatewayState wires this to the real checks.
        /// </summary>
        public Func<bool> IsEditorBusy { get; set; } = () => false;

        public bool HasActiveHeavy => _activeHeavyTicket != null;
        public int QueueDepth => _store.QueueDepth;
        public int SmoothInFlight => _smoothInFlight.Count;

        /// <summary>
        /// Submit a batch of commands. Returns the BatchJob with ticket.
        /// Deduplicates multi-command batches: if an identical batch (same tools + params in same order)
        /// from the same agent is already queued/running, returns the existing job.
        /// </summary>
        public BatchJob Submit(string agent, string label, bool atomic, List<BatchCommand> commands)
        {
            // --- Dedup check for multi-command batches ---
            var existing = FindDuplicateBatch(agent, commands);
            if (existing != null)
            {
                existing.Deduplicated = true;
                McpLog.Info($"[CommandQueue] Dedup: identical batch already queued/running as {existing.Ticket}. Returning existing job.");
                return existing;
            }

            var batchTier = CommandClassifier.ClassifyBatch(
                commands.Select(c => (c.Tool, c.Tier, c.Params)).ToArray());

            var job = _store.CreateJob(agent, label, atomic, batchTier);
            job.Commands = commands;
            job.CausesDomainReload = commands.Any(c => c.CausesDomainReload);

            if (batchTier == ExecutionTier.Heavy)
                _heavyQueue.Enqueue(job.Ticket);
            // Smooth and Instant are handled differently (see ProcessTick)

            return job;
        }

        /// <summary>
        /// Look for an existing queued/running job with identical commands.
        /// </summary>
        private BatchJob FindDuplicateBatch(string agent, List<BatchCommand> commands)
        {
            foreach (var existing in _store.GetAllJobs())
            {
                if (existing.Status != JobStatus.Queued && existing.Status != JobStatus.Running)
                    continue;
                if (existing.Commands == null || existing.Commands.Count != commands.Count)
                    continue;

                bool match = true;
                for (int i = 0; i < commands.Count; i++)
                {
                    if (existing.Commands[i].Tool != commands[i].Tool)
                    {
                        match = false;
                        break;
                    }
                    var existingParams = existing.Commands[i].Params?.ToString(Newtonsoft.Json.Formatting.None) ?? "";
                    var newParams = commands[i].Params?.ToString(Newtonsoft.Json.Formatting.None) ?? "";
                    if (existingParams != newParams)
                    {
                        match = false;
                        break;
                    }
                }

                if (match)
                    return existing;
            }
            return null;
        }

        /// <summary>
        /// Submit a single command as a 1-command batch job. Convenience wrapper for
        /// TransportCommandDispatcher gateway routing.
        /// Deduplicates: if an identical command from the same agent is already queued/running,
        /// returns the existing job instead of creating a duplicate.
        /// </summary>
        public BatchJob SubmitSingle(string tool, JObject parameters, string agent)
        {
            // --- Dedup check: look for an identical queued/running single-command job ---
            var paramStr = parameters?.ToString(Newtonsoft.Json.Formatting.None) ?? "";
            foreach (var existing in _store.GetAllJobs())
            {
                if (existing.Status != JobStatus.Queued && existing.Status != JobStatus.Running)
                    continue;
                if (existing.Commands == null || existing.Commands.Count != 1)
                    continue;
                if (existing.Commands[0].Tool != tool)
                    continue;
                var existingParamStr = existing.Commands[0].Params?.ToString(Newtonsoft.Json.Formatting.None) ?? "";
                if (existingParamStr == paramStr)
                {
                    existing.Deduplicated = true;
                    McpLog.Info($"[CommandQueue] Dedup: '{tool}' already queued/running as {existing.Ticket}. Returning existing job.");
                    return existing;
                }
            }

            var toolTier = CommandRegistry.GetToolTier(tool);
            var effectiveTier = CommandClassifier.Classify(tool, toolTier, parameters);

            var commands = new List<BatchCommand>(1)
            {
                new BatchCommand
                {
                    Tool = tool,
                    Params = parameters ?? new JObject(),
                    Tier = effectiveTier,
                    CausesDomainReload = CommandClassifier.CausesDomainReload(tool, parameters)
                }
            };

            return Submit(agent, tool, atomic: false, commands);
        }

        /// <summary>
        /// Poll a job's status.
        /// </summary>
        public BatchJob Poll(string ticket) => _store.GetJob(ticket);

        /// <summary>
        /// Cancel a queued job. Only the owning agent can cancel.
        /// </summary>
        public bool Cancel(string ticket, string agent)
        {
            var job = _store.GetJob(ticket);
            if (job == null || job.Status != JobStatus.Queued) return false;
            if (job.Agent != agent && agent != null) return false;

            job.Status = JobStatus.Cancelled;
            job.CompletedAt = DateTime.UtcNow;
            return true;
        }

        /// <summary>
        /// Get jobs ahead of the given ticket in the queue.
        /// </summary>
        public List<BatchJob> GetAheadOf(string ticket)
        {
            var queued = _store.GetQueuedJobs();
            var result = new List<BatchJob>();
            foreach (var j in queued)
            {
                if (j.Ticket == ticket) break;
                result.Add(j);
            }
            // Also include the active heavy job
            if (_activeHeavyTicket != null)
            {
                var active = _store.GetJob(_activeHeavyTicket);
                if (active != null)
                    result.Insert(0, active);
            }
            return result;
        }

        /// <summary>
        /// Returns the reason a queued job is blocked, or null if not blocked.
        /// </summary>
        public string GetBlockedReason(string ticket)
        {
            var job = _store.GetJob(ticket);
            if (job == null || job.Status != JobStatus.Queued) return null;
            if (!job.CausesDomainReload) return null;
            if (!IsEditorBusy()) return null;

            if (MCPForUnity.Editor.Services.TestJobManager.HasRunningJob
                || MCPForUnity.Editor.Services.TestRunStatus.IsRunning)
                return "tests_running";
            if (UnityEditor.EditorApplication.isCompiling)
                return "compiling";
            return "editor_busy";
        }

        /// <summary>
        /// Remove a completed job from the store by ticket.
        /// Clears the active heavy slot if the removed job was the active heavy.
        /// Returns the removed job, or null if not found.
        /// </summary>
        public BatchJob Remove(string ticket)
        {
            if (_activeHeavyTicket == ticket)
                _activeHeavyTicket = null;
            _smoothInFlight.Remove(ticket);
            return _store.Remove(ticket);
        }

        /// <summary>
        /// Emergency flush: cancel ALL jobs (queued, running, smooth in-flight),
        /// signal CancellationTokens on running jobs, and reset queue state.
        /// Called from the emergency flush menu item to unfreeze the editor.
        /// </summary>
        public int FlushAll()
        {
            int flushed = 0;

            // Cancel the active heavy job
            if (_activeHeavyTicket != null)
            {
                var heavy = _store.GetJob(_activeHeavyTicket);
                if (heavy != null)
                {
                    heavy.Cts?.Cancel();
                    heavy.Status = JobStatus.Cancelled;
                    heavy.Error = "Flushed by emergency flush.";
                    heavy.CompletedAt = DateTime.UtcNow;
                    flushed++;
                }
                _activeHeavyTicket = null;
            }

            // Cancel smooth in-flight jobs
            foreach (var ticket in _smoothInFlight.ToList())
            {
                var job = _store.GetJob(ticket);
                if (job != null)
                {
                    job.Cts?.Cancel();
                    job.Status = JobStatus.Cancelled;
                    job.Error = "Flushed by emergency flush.";
                    job.CompletedAt = DateTime.UtcNow;
                    flushed++;
                }
            }
            _smoothInFlight.Clear();

            // Cancel all queued jobs
            while (_heavyQueue.Count > 0)
            {
                var ticket = _heavyQueue.Dequeue();
                var job = _store.GetJob(ticket);
                if (job != null && job.Status == JobStatus.Queued)
                {
                    job.Status = JobStatus.Cancelled;
                    job.Error = "Flushed by emergency flush.";
                    job.CompletedAt = DateTime.UtcNow;
                    flushed++;
                }
            }

            // Cancel any remaining queued jobs in the store
            foreach (var job in _store.GetQueuedJobs().ToList())
            {
                job.Status = JobStatus.Cancelled;
                job.Error = "Flushed by emergency flush.";
                job.CompletedAt = DateTime.UtcNow;
                flushed++;
            }

            // Reset watchdog
            _busyBlockedSince = DateTime.MaxValue;
            _watchdogWarned = false;

            McpLog.Warn($"[CommandQueue] Emergency flush: cancelled {flushed} job(s).");
            return flushed;
        }

        /// <summary>
        /// Compressed summary of all jobs for batch status checks.
        /// Returns short field names for token efficiency.
        /// </summary>
        public object GetSummary()
        {
            var allJobs = _store.GetAllJobs();
            var summary = new List<object>(allJobs.Count);

            foreach (var j in allJobs)
            {
                var entry = new Dictionary<string, object>
                {
                    ["t"] = j.Ticket,
                    ["s"] = j.Status.ToString().ToLowerInvariant(),
                    ["a"] = j.Agent,
                    ["l"] = j.Label
                };

                if (j.Commands != null && j.Commands.Count > 0)
                    entry["p"] = $"{j.CurrentIndex + (j.Status == JobStatus.Done ? 0 : 1)}/{j.Commands.Count}";

                if (j.Status == JobStatus.Queued && j.CausesDomainReload && IsEditorBusy())
                    entry["b"] = "blocked";

                if (j.Status == JobStatus.Failed && j.Error != null)
                    entry["e"] = j.Error.Length > 80 ? j.Error.Substring(0, 80) + "..." : j.Error;

                summary.Add(entry);
            }

            return new
            {
                jobs = summary,
                heavy = _activeHeavyTicket,
                qd = QueueDepth,
                sf = _smoothInFlight.Count
            };
        }

        /// <summary>
        /// Get all jobs ordered by creation time (most recent first).
        /// </summary>
        public List<BatchJob> GetAllJobs() => _store.GetAllJobs();

        /// <summary>
        /// Get overall queue status.
        /// </summary>
        public object GetStatus()
        {
            var activeHeavy = _activeHeavyTicket != null ? _store.GetJob(_activeHeavyTicket) : null;
            return new
            {
                queue_depth = QueueDepth,
                active_heavy = activeHeavy != null ? new
                {
                    ticket = activeHeavy.Ticket,
                    agent = activeHeavy.Agent,
                    label = activeHeavy.Label,
                    progress = $"{activeHeavy.CurrentIndex}/{activeHeavy.Commands.Count}"
                } : null,
                smooth_in_flight = _smoothInFlight.Count,
                agents = _store.GetAgentStats()
            };
        }

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

        /// <summary>
        /// Called every EditorApplication.update frame. Processes the queue.
        /// </summary>
        public void ProcessTick(Func<string, JObject, CancellationToken, Task<object>> executeCommand)
        {
            _store.CleanExpired(TicketExpiry);

            // Watchdog: if IsEditorBusy has been blocking the heavy queue for too long,
            // auto-clear stuck test jobs. This prevents orphaned test jobs (from domain
            // reloads mid-test) from permanently stalling the queue.
            if (IsEditorBusy())
            {
                if (_busyBlockedSince == DateTime.MaxValue)
                    _busyBlockedSince = DateTime.UtcNow;
                else if (DateTime.UtcNow - _busyBlockedSince > BusyWatchdogTimeout)
                {
                    if (!_watchdogWarned)
                    {
                        _watchdogWarned = true;
                        var elapsed = DateTime.UtcNow - _busyBlockedSince;
                        MCPForUnity.Editor.Helpers.McpLog.Warn(
                            $"[CommandQueue] Watchdog: IsEditorBusy has been true for {elapsed.TotalSeconds:F0}s. " +
                            $"Auto-clearing stuck test job to unblock queue.");
                    }

                    // Auto-clear the stuck test job
                    if (MCPForUnity.Editor.Services.TestJobManager.HasRunningJob)
                        MCPForUnity.Editor.Services.TestJobManager.ClearStuckJob();
                }
            }
            else
            {
                _busyBlockedSince = DateTime.MaxValue;
                _watchdogWarned = false;
            }

            // Clean completed smooth jobs
            _smoothInFlight.RemoveAll(ticket =>
            {
                var j = _store.GetJob(ticket);
                return j == null || j.Status == JobStatus.Done || j.Status == JobStatus.Failed || j.Status == JobStatus.Cancelled;
            });

            // 1. Check if active heavy finished
            if (_activeHeavyTicket != null)
            {
                var heavy = _store.GetJob(_activeHeavyTicket);

                // Job was removed (e.g., auto-cleanup on poll) — release the slot
                if (heavy == null)
                {
                    // Still hold if editor is busy from side-effects of the removed job
                    if (IsEditorBusy())
                        return;

                    _activeHeavyTicket = null;
                    return; // One-frame cooldown
                }

                if (heavy.Status == JobStatus.Done || heavy.Status == JobStatus.Failed || heavy.Status == JobStatus.Cancelled)
                {
                    // Hold the heavy slot while the editor is still busy from side-effects.
                    // e.g., run_tests starts tests asynchronously and returns instantly, but
                    // the TestRunner is still running. We keep the slot occupied so domain-reload
                    // jobs can't be dequeued until the async operation completes.
                    // Skip the hold for cancelled jobs (emergency flush should unblock immediately).
                    if (heavy.Status != JobStatus.Cancelled && IsEditorBusy())
                        return;

                    _activeHeavyTicket = null;
                    // One-frame cooldown: don't immediately dequeue the next heavy job.
                    // Gives async state one editor frame to settle before the guard check.
                    return;
                }

                // Running job with all commands dispatched, waiting for async side-effects
                // (e.g., test runner still executing). Transition to Done once editor settles.
                if (heavy.Status == JobStatus.Running
                    && heavy.Commands != null
                    && heavy.CurrentIndex >= heavy.Commands.Count - 1
                    && heavy.Results.Count >= heavy.Commands.Count)
                {
                    if (!IsEditorBusy())
                    {
                        heavy.Status = JobStatus.Done;
                        heavy.CompletedAt = DateTime.UtcNow;
                    }
                    return; // Either way, wait this frame
                }

                return; // Heavy still running, wait
            }

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
                    return;
                }
            }

            // 3. No heavy active — process smooth jobs
            var smoothQueued = _store.GetQueuedJobs()
                .Where(j => j.Tier == ExecutionTier.Smooth)
                .ToList();
            foreach (var job in smoothQueued)
            {
                _smoothInFlight.Add(job.Ticket);
                _ = ExecuteJob(job, executeCommand);
            }
        }

        /// <summary>
        /// Execute all commands in a job sequentially, respecting CancellationToken.
        /// </summary>
        async Task ExecuteJob(BatchJob job, Func<string, JObject, CancellationToken, Task<object>> executeCommand)
        {
            job.Cts = new CancellationTokenSource();
            job.Status = JobStatus.Running;
            var ct = job.Cts.Token;

            if (job.Atomic)
            {
                Undo.IncrementCurrentGroup();
                job.UndoGroup = Undo.GetCurrentGroup();
                Undo.SetCurrentGroupName($"Gateway: {job.Label}");
            }

            try
            {
                for (int i = 0; i < job.Commands.Count; i++)
                {
                    ct.ThrowIfCancellationRequested();

                    job.CurrentIndex = i;
                    var cmd = job.Commands[i];

                    var result = await executeCommand(cmd.Tool, cmd.Params, ct);
                    job.Results.Add(result);

                    // Check for failure
                    if (result is IMcpResponse resp && !resp.Success)
                    {
                        if (job.Atomic)
                        {
                            Undo.RevertAllInCurrentGroup();
                            job.Error = $"Command {i} ({cmd.Tool}) failed. Batch rolled back.";
                            job.Status = JobStatus.Failed;
                            job.CompletedAt = DateTime.UtcNow;
                            return;
                        }
                    }
                }

                if (job.Atomic && job.UndoGroup >= 0)
                    Undo.CollapseUndoOperations(job.UndoGroup);

                // If the batch triggered async side-effects (e.g., run_tests starts
                // the test runner which keeps going after the command returns), keep
                // the job in Running so poll_job shows it as in-progress. ProcessTick
                // will transition it to Done once IsEditorBusy() returns false.
                if (job.Tier == ExecutionTier.Heavy && IsEditorBusy())
                {
                    job.Status = JobStatus.Running;
                    // Mark all commands as dispatched so progress shows full count
                    job.CurrentIndex = job.Commands.Count - 1;
                }
                else
                {
                    job.Status = JobStatus.Done;
                    job.CompletedAt = DateTime.UtcNow;
                }
            }
            catch (OperationCanceledException)
            {
                if (job.Atomic && job.UndoGroup >= 0)
                    Undo.RevertAllInCurrentGroup();
                job.Error = "Cancelled.";
                job.Status = JobStatus.Cancelled;
                job.CompletedAt = DateTime.UtcNow;
            }
            catch (Exception ex)
            {
                if (job.Atomic && job.UndoGroup >= 0)
                    Undo.RevertAllInCurrentGroup();
                job.Error = ex.Message;
                job.Status = JobStatus.Failed;
                job.CompletedAt = DateTime.UtcNow;
            }
            finally
            {
                job.Cts?.Dispose();
                job.Cts = null;
            }
        }
    }
}
