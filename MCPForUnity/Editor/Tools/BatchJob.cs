using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace MCPForUnity.Editor.Tools
{
    public enum JobStatus { Queued, Running, Done, Failed, Cancelled }

    /// <summary>
    /// Represents a queued batch of MCP commands with ticket tracking.
    /// </summary>
    public class BatchJob
    {
        public string Ticket { get; set; }
        public string Agent { get; set; }
        public string Label { get; set; }
        public bool Atomic { get; set; }
        public ExecutionTier Tier { get; set; }
        public JobStatus Status { get; set; } = JobStatus.Queued;

        public List<BatchCommand> Commands { get; set; } = new();
        public List<object> Results { get; set; } = new();
        public int CurrentIndex { get; set; }
        public string Error { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? CompletedAt { get; set; }

        public int UndoGroup { get; set; } = -1;
    }

    public class BatchCommand
    {
        public string Tool { get; set; }
        public JObject Params { get; set; }
        public ExecutionTier Tier { get; set; }
    }

    public class AgentStats
    {
        public int Active { get; set; }
        public int Queued { get; set; }
        public int Completed { get; set; }
    }
}
