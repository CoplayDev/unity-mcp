using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Models;
using MCPForUnity.Editor.Services;
using MCPForUnity.Editor.Tools;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;

namespace MCPForUnity.Editor.Services.Transport
{
    /// <summary>
    /// Centralised command execution pipeline shared by all transport implementations.
    /// Guarantees that MCP commands are executed on the Unity main thread while preserving
    /// the legacy response format expected by the server.
    /// </summary>
    [InitializeOnLoad]
    internal static class TransportCommandDispatcher
    {
        private static SynchronizationContext _mainThreadContext;
        private static int _mainThreadId;
        private static int _processingFlag;

        private sealed class PendingCommand
        {
            public PendingCommand(
                string commandJson,
                TaskCompletionSource<string> completionSource,
                CancellationToken cancellationToken,
                CancellationTokenRegistration registration)
            {
                CommandJson = commandJson;
                CompletionSource = completionSource;
                CancellationToken = cancellationToken;
                CancellationRegistration = registration;
                QueuedAt = DateTime.UtcNow;
            }

            public string CommandJson { get; }
            public TaskCompletionSource<string> CompletionSource { get; }
            public CancellationToken CancellationToken { get; }
            public CancellationTokenRegistration CancellationRegistration { get; }
            public bool IsExecuting { get; set; }
            public DateTime QueuedAt { get; }

            public void Dispose()
            {
                CancellationRegistration.Dispose();
            }

            public void TrySetResult(string payload)
            {
                CompletionSource.TrySetResult(payload);
            }

            public void TrySetCanceled()
            {
                CompletionSource.TrySetCanceled(CancellationToken);
            }
        }

        private static readonly Dictionary<string, PendingCommand> Pending = new();
        /// <summary>
        /// Maps command JSON content hash → pending ID for deduplication.
        /// When a duplicate command arrives while an identical one is still pending,
        /// the duplicate shares the original's TaskCompletionSource instead of queueing again.
        /// </summary>
        private static readonly Dictionary<string, string> ContentHashToPendingId = new();
        private static readonly object PendingLock = new();
        private static bool updateHooked;
        private static bool initialised;

        static TransportCommandDispatcher()
        {
            // Ensure this runs on the Unity main thread at editor load.
            _mainThreadContext = SynchronizationContext.Current;
            _mainThreadId = Thread.CurrentThread.ManagedThreadId;

            EnsureInitialised();

            // Always keep the update hook installed so commands arriving from background
            // websocket tasks don't depend on a background-thread event subscription.
            if (!updateHooked)
            {
                updateHooked = true;
                EditorApplication.update += ProcessQueue;
            }
        }

        /// <summary>
        /// Schedule a command for execution on the Unity main thread and await its JSON response.
        /// </summary>
        public static Task<string> ExecuteCommandJsonAsync(string commandJson, CancellationToken cancellationToken)
        {
            if (commandJson is null)
            {
                throw new ArgumentNullException(nameof(commandJson));
            }

            EnsureInitialised();

            // --- Deduplication: if an identical command is already pending, share its result ---
            var contentHash = ComputeContentHash(commandJson);

            lock (PendingLock)
            {
                if (contentHash != null
                    && ContentHashToPendingId.TryGetValue(contentHash, out var existingId)
                    && Pending.TryGetValue(existingId, out var existingPending)
                    && !existingPending.CancellationToken.IsCancellationRequested)
                {
                    McpLog.Info($"[Dispatcher] Dedup: identical command already pending (id={existingId}). Sharing result.");
                    // Return the existing task — caller gets the same result when the original completes.
                    return existingPending.CompletionSource.Task;
                }
            }

            var id = Guid.NewGuid().ToString("N");
            var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

            var registration = cancellationToken.CanBeCanceled
                ? cancellationToken.Register(() => CancelPending(id, cancellationToken))
                : default;

            var pending = new PendingCommand(commandJson, tcs, cancellationToken, registration);

            lock (PendingLock)
            {
                Pending[id] = pending;
                if (contentHash != null)
                    ContentHashToPendingId[contentHash] = id;
            }

            // Proactively wake up the main thread execution loop. This improves responsiveness
            // in scenarios where EditorApplication.update is throttled or temporarily not firing
            // (e.g., Unity unfocused, compiling, or during domain reload transitions).
            RequestMainThreadPump();

            return tcs.Task;
        }

        internal static Task<T> RunOnMainThreadAsync<T>(Func<T> func, CancellationToken cancellationToken)
        {
            if (func is null)
            {
                throw new ArgumentNullException(nameof(func));
            }

            var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);

            var registration = cancellationToken.CanBeCanceled
                ? cancellationToken.Register(() => tcs.TrySetCanceled(cancellationToken))
                : default;

            void Invoke()
            {
                try
                {
                    if (tcs.Task.IsCompleted)
                    {
                        return;
                    }

                    var result = func();
                    tcs.TrySetResult(result);
                }
                catch (Exception ex)
                {
                    tcs.TrySetException(ex);
                }
                finally
                {
                    registration.Dispose();
                }
            }

            // Best-effort nudge: if we're posting from a background thread (e.g., websocket receive),
            // encourage Unity to run a loop iteration so the posted callback can execute even when unfocused.
            try { EditorApplication.QueuePlayerLoopUpdate(); } catch { }

            if (_mainThreadContext != null && Thread.CurrentThread.ManagedThreadId != _mainThreadId)
            {
                _mainThreadContext.Post(_ => Invoke(), null);
                return tcs.Task;
            }

            Invoke();
            return tcs.Task;
        }

        private static void RequestMainThreadPump()
        {
            void Pump()
            {
                try
                {
                    // Hint Unity to run a loop iteration soon.
                    EditorApplication.QueuePlayerLoopUpdate();
                }
                catch
                {
                    // Best-effort only.
                }

                ProcessQueue();
            }

            if (_mainThreadContext != null && Thread.CurrentThread.ManagedThreadId != _mainThreadId)
            {
                _mainThreadContext.Post(_ => Pump(), null);
                return;
            }

            Pump();
        }

        private static void EnsureInitialised()
        {
            if (initialised)
            {
                return;
            }

            CommandRegistry.Initialize();
            initialised = true;
        }

        private static void HookUpdate()
        {
            // Deprecated: we keep the update hook installed permanently (see static ctor).
            if (updateHooked) return;
            updateHooked = true;
            EditorApplication.update += ProcessQueue;
        }

        private static void UnhookUpdateIfIdle()
        {
            // Intentionally no-op: keep update hook installed so background commands always process.
            // This avoids "must focus Unity to re-establish contact" edge cases.
            return;
        }

        private static void ProcessQueue()
        {
            if (Interlocked.Exchange(ref _processingFlag, 1) == 1)
            {
                return;
            }

            try
            {
            List<(string id, PendingCommand pending)> ready;

            lock (PendingLock)
            {
                // Early exit inside lock to prevent per-frame List allocations (GitHub issue #577)
                if (Pending.Count == 0)
                {
                    return;
                }

                ready = new List<(string, PendingCommand)>(Pending.Count);
                foreach (var kvp in Pending)
                {
                    if (kvp.Value.IsExecuting)
                    {
                        continue;
                    }

                    kvp.Value.IsExecuting = true;
                    ready.Add((kvp.Key, kvp.Value));
                }

                if (ready.Count == 0)
                {
                    UnhookUpdateIfIdle();
                    return;
                }
            }

            foreach (var (id, pending) in ready)
            {
                ProcessCommand(id, pending);
            }
            }
            finally
            {
                Interlocked.Exchange(ref _processingFlag, 0);
            }
        }

        private static void ProcessCommand(string id, PendingCommand pending)
        {
            if (pending.CancellationToken.IsCancellationRequested)
            {
                RemovePending(id, pending);
                pending.TrySetCanceled();
                return;
            }

            string commandText = pending.CommandJson?.Trim();
            if (string.IsNullOrEmpty(commandText))
            {
                pending.TrySetResult(SerializeError("Empty command received"));
                RemovePending(id, pending);
                return;
            }

            if (string.Equals(commandText, "ping", StringComparison.OrdinalIgnoreCase))
            {
                var pingResponse = new
                {
                    status = "success",
                    result = new { message = "pong" }
                };
                pending.TrySetResult(JsonConvert.SerializeObject(pingResponse));
                RemovePending(id, pending);
                return;
            }

            if (!IsValidJson(commandText))
            {
                var invalidJsonResponse = new
                {
                    status = "error",
                    error = "Invalid JSON format",
                    receivedText = commandText.Length > 50 ? commandText[..50] + "..." : commandText
                };
                pending.TrySetResult(JsonConvert.SerializeObject(invalidJsonResponse));
                RemovePending(id, pending);
                return;
            }

            try
            {
                var command = JsonConvert.DeserializeObject<Command>(commandText);
                if (command == null)
                {
                    pending.TrySetResult(SerializeError("Command deserialized to null", "Unknown", commandText));
                    RemovePending(id, pending);
                    return;
                }

                if (string.IsNullOrWhiteSpace(command.type))
                {
                    pending.TrySetResult(SerializeError("Command type cannot be empty"));
                    RemovePending(id, pending);
                    return;
                }

                if (string.Equals(command.type, "ping", StringComparison.OrdinalIgnoreCase))
                {
                    var pingResponse = new
                    {
                        status = "success",
                        result = new { message = "pong" }
                    };
                    pending.TrySetResult(JsonConvert.SerializeObject(pingResponse));
                    RemovePending(id, pending);
                    return;
                }

                var parameters = command.@params ?? new JObject();

                // Block execution of disabled resources
                var resourceMeta = MCPServiceLocator.ResourceDiscovery.GetResourceMetadata(command.type);
                if (resourceMeta != null && !MCPServiceLocator.ResourceDiscovery.IsResourceEnabled(command.type))
                {
                    pending.TrySetResult(SerializeError(
                        $"Resource '{command.type}' is disabled in the Unity Editor."));
                    RemovePending(id, pending);
                    return;
                }

                // Block execution of disabled tools
                var toolMeta = MCPServiceLocator.ToolDiscovery.GetToolMetadata(command.type);
                if (toolMeta != null && !MCPServiceLocator.ToolDiscovery.IsToolEnabled(command.type))
                {
                    pending.TrySetResult(SerializeError(
                        $"Tool '{command.type}' is disabled in the Unity Editor."));
                    RemovePending(id, pending);
                    return;
                }

                var logType = resourceMeta != null ? "resource" : toolMeta != null ? "tool" : "unknown";

                // --- Tier-aware dispatch ---
                var declaredTier = CommandRegistry.GetToolTier(command.type);
                var effectiveTier = CommandClassifier.Classify(command.type, declaredTier, parameters);

                if (effectiveTier != ExecutionTier.Instant)
                {
                    // Route Smooth/Heavy through the gateway queue for tier-aware scheduling,
                    // heavy exclusivity, domain-reload guards, and CancellationToken support.
                    var job = CommandGatewayState.Queue.SubmitSingle(command.type, parameters, "transport");
                    var gatewaySw = McpLogRecord.IsEnabled ? System.Diagnostics.Stopwatch.StartNew() : null;

                    async void AwaitGateway()
                    {
                        try
                        {
                            var gatewayResult = await CommandGatewayState.AwaitJob(job);
                            gatewaySw?.Stop();
                            if (gatewayResult is IMcpResponse mcpResp && !mcpResp.Success)
                            {
                                McpLogRecord.Log(command.type, parameters, logType, "ERROR", gatewaySw?.ElapsedMilliseconds ?? 0, (gatewayResult as ErrorResponse)?.Error);
                                var errResponse = new { status = "error", result = gatewayResult, _queue = new { ticket = job.Ticket, tier = job.Tier.ToString().ToLowerInvariant(), queued = true } };
                                pending.TrySetResult(JsonConvert.SerializeObject(errResponse));
                            }
                            else
                            {
                                McpLogRecord.Log(command.type, parameters, logType, "SUCCESS", gatewaySw?.ElapsedMilliseconds ?? 0, null);
                                var okResponse = new { status = "success", result = gatewayResult, _queue = new { ticket = job.Ticket, tier = job.Tier.ToString().ToLowerInvariant(), queued = true } };
                                pending.TrySetResult(JsonConvert.SerializeObject(okResponse));
                            }
                        }
                        catch (Exception ex)
                        {
                            gatewaySw?.Stop();
                            McpLogRecord.Log(command.type, parameters, logType, "ERROR", gatewaySw?.ElapsedMilliseconds ?? 0, ex.Message);
                            pending.TrySetResult(SerializeError(ex.Message, command.type, ex.StackTrace));
                        }
                        finally
                        {
                            EditorApplication.delayCall += () => RemovePending(id, pending);
                        }
                    }

                    AwaitGateway();
                    return;
                }

                // --- Instant tier: execute directly (existing path) ---
                var sw = McpLogRecord.IsEnabled ? System.Diagnostics.Stopwatch.StartNew() : null;
                var result = CommandRegistry.ExecuteCommand(command.type, parameters, pending.CompletionSource);

                if (result == null)
                {
                    // Async command – cleanup after completion on next editor frame to preserve order.
                    var capturedType = command.type;
                    var capturedParams = parameters;
                    var capturedLogType = logType;
                    pending.CompletionSource.Task.ContinueWith(t =>
                    {
                        sw?.Stop();
                        var logStatus = "SUCCESS";
                        string logError = null;
                        if (t.IsFaulted)
                        {
                            logStatus = "ERROR";
                            logError = t.Exception?.InnerException?.Message;
                        }
                        else if (t.IsCompletedSuccessfully && t.Result != null)
                        {
                            try
                            {
                                var resultObj = JObject.Parse(t.Result);
                                if (string.Equals(resultObj.Value<string>("status"), "error", StringComparison.OrdinalIgnoreCase))
                                {
                                    logStatus = "ERROR";
                                    logError = resultObj.Value<string>("error");
                                }
                            }
                            catch { }
                        }
                        McpLogRecord.Log(capturedType, capturedParams, capturedLogType,
                            logStatus, sw?.ElapsedMilliseconds ?? 0, logError);
                        EditorApplication.delayCall += () => RemovePending(id, pending);
                    }, TaskScheduler.Default);
                    return;
                }

                sw?.Stop();

                string syncLogStatus = "SUCCESS";
                string syncLogError = null;
                if (result is ErrorResponse errResp)
                {
                    syncLogStatus = "ERROR";
                    syncLogError = errResp.Error;
                }
                McpLogRecord.Log(command.type, parameters, logType, syncLogStatus, sw?.ElapsedMilliseconds ?? 0, syncLogError);

                var directResponse = new { status = "success", result };
                pending.TrySetResult(JsonConvert.SerializeObject(directResponse));
                RemovePending(id, pending);
            }
            catch (Exception ex)
            {
                McpLog.Error($"Error processing command: {ex.Message}\n{ex.StackTrace}");
                pending.TrySetResult(SerializeError(ex.Message, "Unknown (error during processing)", ex.StackTrace));
                RemovePending(id, pending);
            }
        }

        private static void CancelPending(string id, CancellationToken token)
        {
            PendingCommand pending = null;
            lock (PendingLock)
            {
                if (Pending.Remove(id, out pending))
                {
                    CleanContentHash(id);
                    UnhookUpdateIfIdle();
                }
            }

            pending?.TrySetCanceled();
            pending?.Dispose();
        }

        private static void RemovePending(string id, PendingCommand pending)
        {
            lock (PendingLock)
            {
                Pending.Remove(id);
                CleanContentHash(id);
                UnhookUpdateIfIdle();
            }

            pending.Dispose();
        }

        /// <summary>
        /// Remove the content hash entry that points to the given pending ID.
        /// Must be called under PendingLock.
        /// </summary>
        private static void CleanContentHash(string pendingId)
        {
            string hashToRemove = null;
            foreach (var kvp in ContentHashToPendingId)
            {
                if (kvp.Value == pendingId)
                {
                    hashToRemove = kvp.Key;
                    break;
                }
            }
            if (hashToRemove != null)
                ContentHashToPendingId.Remove(hashToRemove);
        }

        /// <summary>
        /// Compute a stable content hash for command deduplication.
        /// Returns null for non-JSON commands (e.g., "ping") which are cheap enough to not need dedup.
        /// </summary>
        private static string ComputeContentHash(string commandJson)
        {
            if (string.IsNullOrWhiteSpace(commandJson)) return null;
            var trimmed = commandJson.Trim();
            if (!trimmed.StartsWith("{")) return null; // Skip non-JSON (ping, etc.)

            // Use the raw JSON string as the hash key. Retries from the same client produce
            // byte-identical JSON, so this is both fast and correct for the dedup use case.
            // For very large payloads, a proper hash could be used, but MCP commands are small.
            return trimmed;
        }

        private static string SerializeError(string message, string commandType = null, string stackTrace = null)
        {
            var errorResponse = new
            {
                status = "error",
                error = message,
                command = commandType ?? "Unknown",
                stackTrace
            };
            return JsonConvert.SerializeObject(errorResponse);
        }

        private static bool IsValidJson(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            text = text.Trim();
            if ((text.StartsWith("{") && text.EndsWith("}")) || (text.StartsWith("[") && text.EndsWith("]")))
            {
                try
                {
                    JToken.Parse(text);
                    return true;
                }
                catch
                {
                    return false;
                }
            }

            return false;
        }
    }
}
