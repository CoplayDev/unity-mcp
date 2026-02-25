using System;
using System.Collections.Generic;
using System.Linq;
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
        /// </summary>
        public BatchJob Submit(string agent, string label, bool atomic, List<BatchCommand> commands)
        {
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
        /// Returns the removed job, or null if not found.
        /// </summary>
        public BatchJob Remove(string ticket) => _store.Remove(ticket);

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
        public void ProcessTick(Func<string, JObject, Task<object>> executeCommand)
        {
            _store.CleanExpired(TicketExpiry);

            // Clean completed smooth jobs
            _smoothInFlight.RemoveAll(ticket =>
            {
                var j = _store.GetJob(ticket);
                return j == null || j.Status == JobStatus.Done || j.Status == JobStatus.Failed;
            });

            // 1. Check if active heavy finished
            if (_activeHeavyTicket != null)
            {
                var heavy = _store.GetJob(_activeHeavyTicket);
                if (heavy != null && (heavy.Status == JobStatus.Done || heavy.Status == JobStatus.Failed))
                {
                    // Hold the heavy slot while the editor is still busy from side-effects.
                    // e.g., run_tests starts tests asynchronously and returns instantly, but
                    // the TestRunner is still running. We keep the slot occupied so domain-reload
                    // jobs can't be dequeued until the async operation completes.
                    if (IsEditorBusy())
                        return;

                    _activeHeavyTicket = null;
                    // One-frame cooldown: don't immediately dequeue the next heavy job.
                    // Gives async state one editor frame to settle before the guard check.
                    return;
                }
                else
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
        /// Execute all commands in a job sequentially.
        /// </summary>
        async Task ExecuteJob(BatchJob job, Func<string, JObject, Task<object>> executeCommand)
        {
            job.Status = JobStatus.Running;

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
                    job.CurrentIndex = i;
                    var cmd = job.Commands[i];

                    var result = await executeCommand(cmd.Tool, cmd.Params);
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

                job.Status = JobStatus.Done;
            }
            catch (Exception ex)
            {
                if (job.Atomic && job.UndoGroup >= 0)
                    Undo.RevertAllInCurrentGroup();
                job.Error = ex.Message;
                job.Status = JobStatus.Failed;
            }
            finally
            {
                job.CompletedAt = DateTime.UtcNow;
            }
        }
    }
}
