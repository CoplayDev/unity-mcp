using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

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

        /// <summary>
        /// Get all jobs ordered by creation time (most recent first). Used for queue summary.
        /// </summary>
        public List<BatchJob> GetAllJobs()
        {
            return _jobs.Values.OrderByDescending(j => j.CreatedAt).ToList();
        }

        /// <summary>
        /// Serialize all jobs to JSON for SessionState persistence.
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
        /// Restore jobs from JSON. Running jobs are marked failed (interrupted by domain reload).
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

                    // Jobs that were running when domain reload hit are now dead
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
    }
}
