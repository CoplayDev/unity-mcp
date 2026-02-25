using System;
using System.Collections.Generic;
using System.Linq;

namespace MCPForUnity.Editor.Tools
{
    /// <summary>
    /// Store for batch job tickets. Manages job lifecycle with auto-cleanup.
    /// </summary>
    public class TicketStore
    {
        readonly Dictionary<string, BatchJob> _jobs = new();
        int _nextId;

        public BatchJob CreateJob(string agent, string label, bool atomic, ExecutionTier tier)
        {
            var job = new BatchJob
            {
                Ticket = $"t-{_nextId++:D6}",
                Agent = agent ?? "anonymous",
                Label = label ?? "",
                Atomic = atomic,
                Tier = tier
            };
            _jobs[job.Ticket] = job;
            return job;
        }

        public BatchJob GetJob(string ticket)
        {
            return _jobs.TryGetValue(ticket, out var job) ? job : null;
        }

        public void CleanExpired(TimeSpan expiry)
        {
            var expired = _jobs
                .Where(kvp => (kvp.Value.Status == JobStatus.Done || kvp.Value.Status == JobStatus.Failed)
                              && kvp.Value.CompletedAt.HasValue
                              && DateTime.UtcNow - kvp.Value.CompletedAt.Value > expiry)
                .Select(kvp => kvp.Key)
                .ToList();
            foreach (var key in expired)
                _jobs.Remove(key);
        }

        public List<BatchJob> GetQueuedJobs()
        {
            return _jobs.Values
                .Where(j => j.Status == JobStatus.Queued)
                .OrderBy(j => j.CreatedAt)
                .ToList();
        }

        public List<BatchJob> GetRunningJobs()
        {
            return _jobs.Values.Where(j => j.Status == JobStatus.Running).ToList();
        }

        public Dictionary<string, AgentStats> GetAgentStats()
        {
            var stats = new Dictionary<string, AgentStats>();
            foreach (var job in _jobs.Values)
            {
                if (!stats.TryGetValue(job.Agent, out var s))
                {
                    s = new AgentStats();
                    stats[job.Agent] = s;
                }
                switch (job.Status)
                {
                    case JobStatus.Running: s.Active++; break;
                    case JobStatus.Queued: s.Queued++; break;
                    case JobStatus.Done: s.Completed++; break;
                }
            }
            return stats;
        }

        public int QueueDepth => _jobs.Values.Count(j => j.Status == JobStatus.Queued);
    }
}
