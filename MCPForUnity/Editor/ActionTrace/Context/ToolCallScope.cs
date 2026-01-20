using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading;
using MCPForUnity.Editor.ActionTrace.Core.Models;
using MCPForUnity.Editor.ActionTrace.Core.Store;
using UnityEditor;

namespace MCPForUnity.Editor.ActionTrace.Context
{
    /// <summary>
    /// Represents a single tool call invocation scope.
    /// Tracks the lifetime, events, and metadata of a tool call.
    /// </summary>
    public sealed class ToolCallScope : IDisposable
    {
        private static readonly ThreadLocal<Stack<ToolCallScope>> _scopeStack =
            new(() => new Stack<ToolCallScope>());

        private readonly string _toolName;
        private readonly string _toolId;
        private readonly Dictionary<string, object> _parameters;
        private readonly List<EditorEvent> _capturedEvents;
        private readonly long _startTimestampMs;
        private readonly List<ToolCallScope> _childScopes;
        private readonly ToolCallScope _parentScope;
        private readonly int _createdThreadId;  // Track thread where scope was created

        private long _endTimestampMs;
        private bool _isCompleted;
        private string _result;
        private string _errorMessage;
        private bool _isDisposed;

        /// <summary>
        /// Unique identifier for this tool call.
        /// </summary>
        public string CallId { get; }

        /// <summary>
        /// Name of the tool being called.
        /// </summary>
        public string ToolName => _toolName;

        /// <summary>
        /// Optional tool identifier (for distinguishing overloaded tools).
        /// </summary>
        public string ToolId => _toolId;

        /// <summary>
        /// Parameters passed to the tool.
        /// </summary>
        public IReadOnlyDictionary<string, object> Parameters => _parameters;

        /// <summary>
        /// Events captured during this tool call.
        /// </summary>
        public IReadOnlyList<EditorEvent> CapturedEvents => _capturedEvents;

        /// <summary>
        /// Child tool calls made during this scope.
        /// </summary>
        public IReadOnlyList<ToolCallScope> ChildScopes => _childScopes;

        /// <summary>
        /// Parent scope if this is a nested call.
        /// </summary>
        public ToolCallScope Parent => _parentScope;

        /// <summary>
        /// Duration of the tool call in milliseconds.
        /// </summary>
        public long DurationMs => _endTimestampMs > 0
            ? _endTimestampMs - _startTimestampMs
            : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - _startTimestampMs;

        /// <summary>
        /// Whether the tool call completed successfully.
        /// </summary>
        public bool IsCompleted => _isCompleted;

        /// <summary>
        /// Result of the tool call (if successful).
        /// </summary>
        public string Result => _result;

        /// <summary>
        /// Error message (if the call failed).
        /// </summary>
        public string ErrorMessage => _errorMessage;

        /// <summary>
        /// Current active scope for this thread.
        /// </summary>
        public static ToolCallScope Current => _scopeStack.Value.Count > 0 ? _scopeStack.Value.Peek() : null;

        /// <summary>
        /// Create a new tool call scope.
        /// </summary>
        public ToolCallScope(string toolName, string toolId = null, Dictionary<string, object> parameters = null)
        {
            _toolName = toolName ?? throw new ArgumentNullException(nameof(toolName));
            _toolId = toolId ?? toolName;
            _parameters = parameters ?? new Dictionary<string, object>();
            _capturedEvents = new List<EditorEvent>();
            _childScopes = new List<ToolCallScope>();
            _startTimestampMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            _parentScope = Current;
            _createdThreadId = Thread.CurrentThread.ManagedThreadId;

            CallId = GenerateCallId();

            // Push to stack (only if on the same thread as creation)
            _scopeStack.Value.Push(this);

            // Notify parent
            _parentScope?._childScopes.Add(this);

            // Record start event
            RecordStartEvent();
        }

        /// <summary>
        /// Complete the tool call with a result.
        /// </summary>
        public void Complete(string result = null)
        {
            if (_isCompleted) return;

            _result = result;
            _isCompleted = true;
            _endTimestampMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            RecordCompletionEvent();
        }

        /// <summary>
        /// Complete the tool call with an error.
        /// </summary>
        public void Fail(string errorMessage)
        {
            if (_isCompleted) return;

            _errorMessage = errorMessage;
            _isCompleted = true;
            _endTimestampMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            RecordErrorEvent();
        }

        /// <summary>
        /// Record an event that occurred during this tool call.
        /// </summary>
        public void RecordEvent(EditorEvent evt)
        {
            if (evt != null && !_isDisposed)
            {
                _capturedEvents.Add(evt);
            }
        }

        /// <summary>
        /// Get all events from this scope and all child scopes (flattened).
        /// </summary>
        public List<EditorEvent> GetAllEventsFlattened()
        {
            var allEvents = new List<EditorEvent>(_capturedEvents);

            foreach (var child in _childScopes)
            {
                allEvents.AddRange(child.GetAllEventsFlattened());
            }

            return allEvents;
        }

        /// <summary>
        /// Get a summary of this tool call.
        /// </summary>
        public string GetSummary()
        {
            var summary = new StringBuilder();

            summary.Append(_toolName);

            if (_parameters.Count > 0)
            {
                summary.Append("(");
                int i = 0;
                foreach (var kvp in _parameters)
                {
                    if (i > 0) summary.Append(", ");
                    summary.Append(kvp.Key).Append("=").Append(FormatValue(kvp.Value));
                    i++;
                    if (i >= 3)
                    {
                        summary.Append("...");
                        break;
                    }
                }
                summary.Append(")");
            }

            summary.Append($" [{DurationMs}ms]");

            if (_errorMessage != null)
            {
                summary.Append($" ERROR: {_errorMessage}");
            }
            else if (_isCompleted)
            {
                summary.Append(" ✓");
            }

            if (_capturedEvents.Count > 0)
            {
                summary.Append($" ({_capturedEvents.Count} events)");
            }

            if (_childScopes.Count > 0)
            {
                summary.Append($" +{_childScopes.Count} nested calls");
            }

            return summary.ToString();
        }

        /// <summary>
        /// Get detailed information about this tool call.
        /// </summary>
        public string GetDetails()
        {
            var details = new StringBuilder();

            details.AppendLine($"=== Tool Call: {_toolName} ===");
            details.AppendLine($"Call ID: {CallId}");
            details.AppendLine($"Duration: {DurationMs}ms");
            details.AppendLine($"Status: {_errorMessage ?? (_isCompleted ? "Completed" : "Running")}");

            if (_parameters.Count > 0)
            {
                details.AppendLine("Parameters:");
                foreach (var kvp in _parameters)
                {
                    details.AppendLine($"  {kvp.Key}: {FormatValue(kvp.Value)}");
                }
            }

            if (_capturedEvents.Count > 0)
            {
                details.AppendLine($"Captured Events ({_capturedEvents.Count}):");
                foreach (var evt in _capturedEvents)
                {
                    details.AppendLine($"  - [{evt.Type}] {evt.GetSummary()}");
                }
            }

            if (_childScopes.Count > 0)
            {
                details.AppendLine($"Nested Calls ({_childScopes.Count}):");
                foreach (var child in _childScopes)
                {
                    details.AppendLine($"  - {child.GetSummary()}");
                }
            }

            if (_result != null)
            {
                details.AppendLine($"Result: {_result}");
            }

            return details.ToString();
        }

        public void Dispose()
        {
            if (_isDisposed) return;

            // Auto-complete if not explicitly completed
            if (!_isCompleted)
            {
                Complete();
            }

            // Pop from stack (only if on the same thread as creation)
            // This prevents stack leaks when ExecuteAsync disposes on a different thread
            int currentThreadId = Thread.CurrentThread.ManagedThreadId;
            if (currentThreadId == _createdThreadId)
            {
                // Same thread: safe to pop from stack
                if (_scopeStack.Value.Count > 0 && _scopeStack.Value.Peek() == this)
                {
                    _scopeStack.Value.Pop();
                }
            }
            else
            {
                // Different thread: schedule cleanup on the original thread
                // Note: This is a best-effort cleanup. In most cases, the scope
                // will be properly cleaned up when the original thread processes its stack.
                EditorApplication.delayCall += () =>
                {
                    if (_scopeStack.Value.Count > 0 && _scopeStack.Value.Peek() == this)
                    {
                        _scopeStack.Value.Pop();
                    }
                };
            }

            _isDisposed = true;
        }

        private string GenerateCallId()
        {
            // Compact ID: tool name + timestamp + random suffix
            long timestamp = _startTimestampMs % 1000000; // Last 6 digits of timestamp
            int random = UnityEngine.Random.Range(1000, 9999);
            return $"{_toolId}_{timestamp}_{random}";
        }

        private void RecordStartEvent()
        {
            var payload = new Dictionary<string, object>
            {
                { "tool_name", _toolName },
                { "call_id", CallId },
                { "parent_call_id", _parentScope?.CallId ?? "" },
                { "parameter_count", _parameters.Count }
            };

            foreach (var kvp in _parameters)
            {
                // Add parameters (truncated if too long)
                string valueStr = FormatValue(kvp.Value);
                if (valueStr != null && valueStr.Length > 100)
                {
                    valueStr = valueStr.Substring(0, 97) + "...";
                }
                payload[$"param_{kvp.Key}"] = valueStr;
            }

            // Emit through EventStore
            var evt = new EditorEvent(
                sequence: 0, // Will be assigned by EventStore
                timestampUnixMs: _startTimestampMs,
                type: "ToolCallStarted",
                targetId: CallId,
                payload: payload
            );
            EventStore.Record(evt);
        }

        private void RecordCompletionEvent()
        {
            var payload = new Dictionary<string, object>
            {
                { "tool_name", _toolName },
                { "call_id", CallId },
                { "duration_ms", DurationMs },
                { "events_captured", _capturedEvents.Count },
                { "nested_calls", _childScopes.Count }
            };

            if (_result != null && _result.Length <= 200)
            {
                payload["result"] = _result;
            }

            var completedEvt = new EditorEvent(
                sequence: 0,
                timestampUnixMs: _endTimestampMs,
                type: "ToolCallCompleted",
                targetId: CallId,
                payload: payload
            );
            EventStore.Record(completedEvt);
        }

        private void RecordErrorEvent()
        {
            var payload = new Dictionary<string, object>
            {
                { "tool_name", _toolName },
                { "call_id", CallId },
                { "duration_ms", DurationMs },
                { "error", _errorMessage ?? "Unknown error" },
                { "events_captured", _capturedEvents.Count }
            };

            var errorEvt = new EditorEvent(
                sequence: 0,
                timestampUnixMs: _endTimestampMs,
                type: "ToolCallFailed",
                targetId: CallId,
                payload: payload
            );
            EventStore.Record(errorEvt);
        }

        private static string FormatValue(object value)
        {
            if (value == null) return "null";
            if (value is string str) return $"\"{str}\"";
            if (value is bool b) return b.ToString().ToLower();
            return value.ToString();
        }

        // ========== Static Helper Methods ==========

        /// <summary>
        /// Create a new scope with automatic disposal.
        /// Usage: using (ToolCallScope.Begin("manage_gameobject", params)) { ... }
        /// </summary>
        public static ToolCallScope Begin(string toolName, string toolId = null, Dictionary<string, object> parameters = null)
        {
            return new ToolCallScope(toolName, toolId, parameters);
        }

        /// <summary>
        /// Get the current scope's call ID (returns empty if no active scope).
        /// </summary>
        public static string GetCurrentCallId()
        {
            return Current?.CallId ?? "";
        }

        /// <summary>
        /// Record an event in the current scope (if any).
        /// </summary>
        public static void RecordEventInCurrentScope(EditorEvent evt)
        {
            Current?.RecordEvent(evt);
        }

        /// <summary>
        /// Get all active scopes in the current thread's hierarchy.
        /// </summary>
        public static List<ToolCallScope> GetActiveHierarchy()
        {
            var hierarchy = new List<ToolCallScope>();
            var stack = _scopeStack.Value;

            foreach (var scope in stack)
            {
                hierarchy.Add(scope);
            }

            hierarchy.Reverse(); // Root first
            return hierarchy;
        }

        /// <summary>
        /// Get the root scope (outermost call) in the current hierarchy.
        /// </summary>
        public static ToolCallScope GetRootScope()
        {
            var stack = _scopeStack.Value;
            if (stack.Count == 0) return null;

            // The bottom of the stack is the root
            return stack.ToArray()[^1];
        }
    }

    /// <summary>
    /// Helper methods for common tool call instrumentation patterns.
    /// </summary>
    public static class ToolCall
    {
        /// <summary>
        /// Execute a function within a tool call scope, automatically recording duration and result.
        /// </summary>
        public static T Execute<T>(string toolName, Func<T> func, string toolId = null, Dictionary<string, object> parameters = null)
        {
            using var scope = new ToolCallScope(toolName, toolId, parameters);

            try
            {
                T result = func();
                scope.Complete(result?.ToString() ?? "");
                return result;
            }
            catch (Exception ex)
            {
                scope.Fail(ex.Message);
                throw;
            }
        }

        /// <summary>
        /// Execute an async function within a tool call scope.
        /// The scope is disposed when the async operation completes or faults.
        /// </summary>
        public static System.Threading.Tasks.Task<T> ExecuteAsync<T>(
            string toolName,
            Func<System.Threading.Tasks.Task<T>> func,
            string toolId = null,
            Dictionary<string, object> parameters = null)
        {
            var scope = new ToolCallScope(toolName, toolId, parameters);

            var task = func();

            return task.ContinueWith(t =>
            {
                try
                {
                    if (t.IsFaulted)
                    {
                        scope.Fail(t.Exception?.Message ?? "Async faulted");
                        throw t.Exception ?? new Exception("Async task faulted");
                    }
                    else
                    {
                        scope.Complete(t.Result?.ToString() ?? "");
                        return t.Result;
                    }
                }
                finally
                {
                    // Always dispose to prevent stack leak
                    scope.Dispose();
                }
            }, System.Threading.Tasks.TaskScheduler.Default);
        }

        /// <summary>
        /// Execute an action within a tool call scope.
        /// </summary>
        public static void Execute(string toolName, Action action, string toolId = null, Dictionary<string, object> parameters = null)
        {
            using var scope = new ToolCallScope(toolName, toolId, parameters);

            try
            {
                action();
                scope.Complete();
            }
            catch (Exception ex)
            {
                scope.Fail(ex.Message);
                throw;
            }
        }

        /// <summary>
        /// Measure execution time of a function without creating a scope.
        /// </summary>
        public static (T result, long ms) Measure<T>(Func<T> func)
        {
            var sw = Stopwatch.StartNew();
            T result = func();
            sw.Stop();
            return (result, sw.ElapsedMilliseconds);
        }

        /// <summary>
        /// Measure execution time of an action without creating a scope.
        /// </summary>
        public static long Measure(Action action)
        {
            var sw = Stopwatch.StartNew();
            action();
            sw.Stop();
            return sw.ElapsedMilliseconds;
        }
    }
}
