using System;

namespace MCPForUnity.Editor.ActionTrace.Context
{
    /// <summary>
    /// Operation source type.
    /// Human: Manual editor operation
    /// AI: AI-assisted operation (Claude, Cursor, etc.)
    /// System: Automated system operation
    /// </summary>
    public enum OperationSource
    {
        Human,
        AI,
        System
    }

    /// <summary>
    /// Immutable context metadata for an operation.
    /// This is a "light marker" - minimal data that doesn't interfere with event storage.
    /// Associated with events via Side-Table (ContextMapping), not embedded in EditorEvent.
    /// </summary>
    public sealed class OperationContext : IEquatable<OperationContext>
    {
        /// <summary>
        /// Unique identifier for this context instance.
        /// </summary>
        public Guid ContextId { get; }

        /// <summary>
        /// Source of the operation (Human, AI, or System).
        /// </summary>
        public OperationSource Source { get; }

        /// <summary>
        /// Agent identifier (e.g., "claude-opus", "cursor", "vscode-copilot").
        /// Null for Human/System operations.
        /// </summary>
        public string AgentId { get; }

        /// <summary>
        /// Operation start time in UTC milliseconds since Unix epoch.
        /// </summary>
        public long StartTimeUnixMs { get; }

        /// <summary>
        /// Optional user/session identifier for correlation.
        /// </summary>
        public string SessionId { get; }

        public OperationContext(
            Guid contextId,
            OperationSource source,
            string agentId = null,
            long startTimeUnixMs = 0,
            string sessionId = null)
        {
            ContextId = contextId;
            Source = source;
            AgentId = agentId;
            StartTimeUnixMs = startTimeUnixMs > 0
                ? startTimeUnixMs
                : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            SessionId = sessionId;
        }

        public bool Equals(OperationContext other)
        {
            if (ReferenceEquals(null, other)) return false;
            if (ReferenceEquals(this, other)) return true;
            return ContextId.Equals(other.ContextId);
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as OperationContext);
        }

        public override int GetHashCode()
        {
            return ContextId.GetHashCode();
        }

        public static bool operator ==(OperationContext left, OperationContext right)
        {
            return Equals(left, right);
        }

        public static bool operator !=(OperationContext left, OperationContext right)
        {
            return !Equals(left, right);
        }
    }

    /// <summary>
    /// Factory for creating common context types.
    /// </summary>
    public static class OperationContextFactory
    {
        /// <summary>
        /// Create a context for an AI operation.
        /// </summary>
        public static OperationContext CreateAiContext(string agentId, string sessionId = null)
        {
            return new OperationContext(
                Guid.NewGuid(),
                OperationSource.AI,
                agentId,
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                sessionId
            );
        }

        /// <summary>
        /// Create a context for a human operation.
        /// </summary>
        public static OperationContext CreateHumanContext(string sessionId = null)
        {
            return new OperationContext(
                Guid.NewGuid(),
                OperationSource.Human,
                null,
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                sessionId
            );
        }

        /// <summary>
        /// Create a context for a system operation.
        /// </summary>
        public static OperationContext CreateSystemContext(string sessionId = null)
        {
            return new OperationContext(
                Guid.NewGuid(),
                OperationSource.System,
                null,
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                sessionId
            );
        }
    }
}
