using System;

namespace MCPForUnity.Editor.ActionTrace.Context
{
    /// <summary>
    /// Side-Table mapping between events and contexts.
    /// This keeps the "bedrock" event layer pure while allowing context association.
    /// Events remain immutable - context is stored separately.
    ///
    /// Design principle:
    /// - EditorEvent = immutable facts (what happened)
    /// - ContextMapping = mutable metadata (who did it, why)
    /// </summary>
    public sealed class ContextMapping : IEquatable<ContextMapping>
    {
        /// <summary>
        /// The sequence number of the associated EditorEvent.
        /// </summary>
        public long EventSequence { get; }

        /// <summary>
        /// The unique identifier of the OperationContext.
        /// </summary>
        public Guid ContextId { get; }

        public ContextMapping(long eventSequence, Guid contextId)
        {
            EventSequence = eventSequence;
            ContextId = contextId;
        }

        public bool Equals(ContextMapping other)
        {
            if (ReferenceEquals(null, other)) return false;
            if (ReferenceEquals(this, other)) return true;
            return EventSequence == other.EventSequence
                && ContextId.Equals(other.ContextId);
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as ContextMapping);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(EventSequence, ContextId);
        }

        public static bool operator ==(ContextMapping left, ContextMapping right)
        {
            return Equals(left, right);
        }

        public static bool operator !=(ContextMapping left, ContextMapping right)
        {
            return !Equals(left, right);
        }
    }
}
