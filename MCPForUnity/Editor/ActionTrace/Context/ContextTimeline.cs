using UnityEditor;
using MCPForUnity.Editor.ActionTrace.Core.Store;
using System;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.ActionTrace.Core.Models;

namespace MCPForUnity.Editor.ActionTrace.Context
{
    /// <summary>
    /// Automatically associates events with the current context.
    /// Subscribes to EventStore.EventRecorded and creates mappings.
    ///
    /// This is the "glue" that connects the event layer to the context layer.
    /// Events are immutable - context is attached via side-table mapping.
    ///
    /// Threading safety:
    /// - EventStore.EventRecorded is raised via delayCall (next editor update)
    /// - This callback runs on main thread, safe to call AddContextMapping
    /// - AddContextMapping is thread-safe (uses _queryLock internally)
    /// </summary>
    [InitializeOnLoad]
    public static class ContextTrace
    {
        static ContextTrace()
        {
            // Subscribe to event recording
            // EventStore already uses delayCall, so this won't cause re-entrancy
            EventStore.EventRecorded += OnEventRecorded;
        }

        /// <summary>
        /// Called when an event is recorded.
        /// Associates the event with the current context (if any).
        /// </summary>
        private static void OnEventRecorded(EditorEvent @event)
        {
            try
            {
                var currentContext = ContextStack.Current;
                if (currentContext != null)
                {
                    // Create the mapping
                    var mapping = new ContextMapping(
                        eventSequence: @event.Sequence,
                        contextId: currentContext.ContextId
                    );

                    // Store in EventStore's side-table
                    EventStore.AddContextMapping(mapping);
                }
            }
            catch (System.Exception ex)
            {
                McpLog.Warn(
                    $"[ContextTrace] Failed to create context mapping: {ex.Message}");
            }
        }

        /// <summary>
        /// Manually associate an event with a context.
        /// Use this for batch operations or deferred association.
        /// </summary>
        public static void Associate(long eventSequence, Guid contextId)
        {
            var mapping = new ContextMapping(eventSequence, contextId);
            EventStore.AddContextMapping(mapping);
        }

        /// <summary>
        /// Remove all mappings for a specific context.
        /// Useful for cleanup after a batch operation.
        /// </summary>
        public static void DisassociateContext(Guid contextId)
        {
            EventStore.RemoveContextMappings(contextId);
        }
    }
}
