using System;

namespace MCPForUnity.Editor.ActionTrace.Capture
{
    /// <summary>
    /// Attribute to mark a class as an event capture point.
    /// Used for auto-discovery during initialization.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
    public sealed class EventCapturePointAttribute : Attribute
    {
        public string Id { get; }
        public string Description { get; }
        public int Priority { get; }

        public EventCapturePointAttribute(string id, string description = null, int priority = 0)
        {
            Id = id;
            Description = description ?? id;
            Priority = priority;
        }
    }
}
