using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using MCPForUnity.Editor.Helpers;

namespace MCPForUnity.Editor.ActionTrace.Context
{
    /// <summary>
    /// Thread-local operation context stack for tracking operation source.
    /// This is a "light marker" system - it doesn't control flow,
    /// it only annotates operations with their source context.
    ///
    /// Design principle:
    /// - Stack is lightweight (just references)
    /// - No blocking operations
    /// - Fast push/pop for using() pattern
    /// - Thread-safe via ThreadStatic (each thread has its own stack)
    ///
    /// Threading model:
    /// - Each thread maintains its own isolated context stack
    /// - Unity Editor callbacks (delayCall, AssetPostprocessor) may run on different threads
    /// - Context does not leak across thread boundaries
    /// - Debug mode logs thread ID for diagnostics
    /// </summary>
    /// TODO-A Better clear strategy
    public static class ContextStack
    {
        [ThreadStatic]
        private static Stack<OperationContext> _stack;

        [ThreadStatic]
        private static int _threadId;  // For debug diagnostics

        /// <summary>
        /// Get the current operation context (if any).
        /// Returns null if no context is active.
        /// </summary>
        public static OperationContext Current
        {
            get
            {
                var stack = GetStack();
                return stack.Count > 0 ? stack.Peek() : null;
            }
        }

        /// <summary>
        /// Get the depth of the context stack.
        /// </summary>
        public static int Depth
        {
            get
            {
                return GetStack().Count;
            }
        }

        /// <summary>
        /// Get the thread-local stack, initializing if necessary.
        /// </summary>
        private static Stack<OperationContext> GetStack()
        {
            if (_stack == null)
            {
                _stack = new Stack<OperationContext>();
                _threadId = Thread.CurrentThread.ManagedThreadId;

#if DEBUG
                McpLog.Info(
                    $"[ContextStack] Initialized new stack for thread {_threadId}");
#endif
            }
            return _stack;
        }

        /// <summary>
        /// Push a context onto the stack.
        /// Returns a disposable that will pop the context when disposed.
        /// </summary>
        public static IDisposable Push(OperationContext context)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));

            var stack = GetStack();
            stack.Push(context);

#if DEBUG
                McpLog.Info(
                    $"[ContextStack] Push context {context.ContextId} on thread {_threadId}, depth: {stack.Count}");
#endif

            return new ContextDisposable(context);
        }

        /// <summary>
        /// Pop the top context from the stack.
        /// Validates that the popped context matches the expected one.
        /// </summary>
        public static bool Pop(OperationContext expectedContext)
        {
            var stack = GetStack();
            if (stack.Count == 0)
            {
#if DEBUG
                McpLog.Warn(
                    $"[ContextStack] Pop on empty stack (thread {_threadId}, expected {expectedContext?.ContextId})");
#endif
                return false;
            }

            var top = stack.Peek();
            if (top.Equals(expectedContext))
            {
                stack.Pop();

#if DEBUG
                McpLog.Info(
                    $"[ContextStack] Pop context {expectedContext.ContextId} on thread {_threadId}, remaining depth: {stack.Count}");
#endif

                return true;
            }

            // Stack mismatch - this indicates a programming error
            // Improvement: Only remove mismatched context, preserve valid ones
            var currentThreadId = Thread.CurrentThread.ManagedThreadId;
            var stackSnapshot = string.Join(", ", stack.Select(c => SafeGetShortId(c.ContextId)));

            // Try to find and remove the mismatched context
            var tempStack = new Stack<OperationContext>();
            bool found = false;

            while (stack.Count > 0)
            {
                var item = stack.Pop();
                if (item.Equals(expectedContext))
                {
                    found = true;
                    break;
                }
                tempStack.Push(item);
            }

            // Restore valid contexts
            while (tempStack.Count > 0)
            {
                stack.Push(tempStack.Pop());
            }

            if (!found)
            {
                McpLog.Warn(
                    $"[ContextStack] Expected context {expectedContext.ContextId} not found on thread {currentThreadId}\n" +
                    $"  Stack snapshot: [{stackSnapshot}]\n" +
                    $"  No changes made to stack.");
            }

            return found;
        }

        /// <summary>
        /// Mark the current operation as an AI operation.
        /// Returns a disposable for automatic cleanup.
        ///
        /// Usage:
        ///   using (ContextStack.MarkAsAiOperation("claude-opus"))
        ///   {
        ///       // All events recorded here are tagged as AI
        ///   }
        /// </summary>
        public static IDisposable MarkAsAiOperation(string agentId, string sessionId = null)
        {
            var context = OperationContextFactory.CreateAiContext(agentId, sessionId);
            return Push(context);
        }

        /// <summary>
        /// Mark the current operation as a human operation.
        /// Returns a disposable for automatic cleanup.
        /// </summary>
        public static IDisposable MarkAsHumanOperation(string sessionId = null)
        {
            var context = OperationContextFactory.CreateHumanContext(sessionId);
            return Push(context);
        }

        /// <summary>
        /// Mark the current operation as a system operation.
        /// Returns a disposable for automatic cleanup.
        /// </summary>
        public static IDisposable MarkAsSystemOperation(string sessionId = null)
        {
            var context = OperationContextFactory.CreateSystemContext(sessionId);
            return Push(context);
        }

        /// <summary>
        /// Check if the current context is from an AI source.
        /// </summary>
        public static bool IsAiOperation
        {
            get
            {
                var current = Current;
                return current != null && current.Source == OperationSource.AI;
            }
        }

        /// <summary>
        /// Get the current agent ID (if AI operation).
        /// </summary>
        public static string CurrentAgentId
        {
            get
            {
                var current = Current;
                return current?.Source == OperationSource.AI ? current.AgentId : null;
            }
        }

        /// <summary>
        /// Clear the entire stack (for error recovery).
        /// Thread-safe: only clears the current thread's stack.
        /// </summary>
        public static void Clear()
        {
            var stack = GetStack();
            stack.Clear();

#if DEBUG
                McpLog.Info(
                    $"[ContextStack] Cleared stack on thread {Thread.CurrentThread.ManagedThreadId}");
#endif
        }

        /// <summary>
        /// Safely extracts a short ID from a Guid, preventing null/empty exceptions.
        /// </summary>
        private static string SafeGetShortId(Guid guid)
        {
            var str = guid.ToString();
            return str.Length >= 8 ? str.Substring(0, 8) : str;
        }

        /// <summary>
        /// Disposable that pops the context when disposed.
        /// Validates the context matches to prevent stack corruption.
        /// </summary>
        private sealed class ContextDisposable : IDisposable
        {
            private readonly OperationContext _context;
            private bool _disposed;

            public ContextDisposable(OperationContext context)
            {
                _context = context ?? throw new ArgumentNullException(nameof(context));
            }

            public void Dispose()
            {
                if (_disposed)
                    return;

                Pop(_context);
                _disposed = true;
            }
        }
    }
}
