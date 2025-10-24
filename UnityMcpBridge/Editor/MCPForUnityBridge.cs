using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Models;
using MCPForUnity.Editor.Tools;
using MCPForUnity.Editor.Tools.MenuItems;
using MCPForUnity.Editor.Tools.Prefabs;

namespace MCPForUnity.Editor
{
    [InitializeOnLoad]
    public static partial class MCPForUnityBridge
    {
        private static TcpListener listener;
        private static bool isRunning = false;
        private static readonly object lockObj = new();
        private static readonly object startStopLock = new();
        private static readonly object clientsLock = new();
        private static readonly System.Collections.Generic.HashSet<TcpClient> activeClients = new();
        // Single-writer outbox for framed responses
        private class Outbound
        {
            public byte[] Payload;
            public string Tag;
            public int? ReqId;
        }
        private static readonly BlockingCollection<Outbound> _outbox = new(new ConcurrentQueue<Outbound>());
        private static CancellationTokenSource cts;
        private static Task listenerTask;
        private static int processingCommands = 0;
        private static bool initScheduled = false;
        private static bool ensureUpdateHooked = false;
        private static bool isStarting = false;
        private static double nextStartAt = 0.0f;
        private static double nextHeartbeatAt = 0.0f;
        private static int heartbeatSeq = 0;
        private static Dictionary<
            string,
            (string commandJson, TaskCompletionSource<string> tcs)
        > commandQueue = new();
        private static int mainThreadId;
        private static int currentUnityPort = 6400; // Dynamic port, starts with default
        private static bool isAutoConnectMode = false;
        private const ulong MaxFrameBytes = 64UL * 1024 * 1024; // 64 MiB hard cap for framed payloads
        private const int FrameIOTimeoutMs = 30000; // Per-read timeout to avoid stalled clients

        // IO diagnostics
        private static long _ioSeq = 0;
        private static void IoInfo(string s) { McpLog.Info(s, always: false); }

        // Debug helpers
        private static bool IsDebugEnabled()
        {
            try { return EditorPrefs.GetBool("MCPForUnity.DebugLogs", false); } catch { return false; }
        }

        private static void LogBreadcrumb(string stage)
        {
            if (IsDebugEnabled())
            {
                McpLog.Info($"[{stage}]", always: false);
            }
        }

        public static bool IsRunning => isRunning;
        public static int GetCurrentPort() => currentUnityPort;
        public static bool IsAutoConnectMode() => isAutoConnectMode;

        /// <summary>
        /// Start with Auto-Connect mode - discovers new port and saves it
        /// </summary>
        public static void StartAutoConnect()
        {
            Stop(); // Stop current connection

            try
            {
                // Prefer stored project port and start using the robust Start() path (with retries/options)
                currentUnityPort = PortManager.GetPortWithFallback();
                Start();
                isAutoConnectMode = true;

                // Record telemetry for bridge startup
                TelemetryHelper.RecordBridgeStartup();
            }
            catch (Exception ex)
            {
                Debug.LogError($"Auto-connect failed: {ex.Message}");

                // Record telemetry for connection failure
                TelemetryHelper.RecordBridgeConnection(false, ex.Message);
                throw;
            }
        }

        public static bool FolderExists(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return false;
            }

            if (path.Equals("Assets", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            string fullPath = Path.Combine(
                Application.dataPath,
                path.StartsWith("Assets/") ? path[7..] : path
            );
            return Directory.Exists(fullPath);
        }

        static MCPForUnityBridge()
        {
            // Record the main thread ID for safe thread checks
            try { mainThreadId = Thread.CurrentThread.ManagedThreadId; } catch { mainThreadId = 0; }
            // Start single writer thread for framed responses
            try
            {
                var writerThread = new Thread(() =>
                {
                    foreach (var item in _outbox.GetConsumingEnumerable())
                    {
                        try
                        {
                            long seq = Interlocked.Increment(ref _ioSeq);
                            IoInfo($"[IO] ➜ write start seq={seq} tag={item.Tag} len={(item.Payload?.Length ?? 0)} reqId={(item.ReqId?.ToString() ?? "?")}");
                            var sw = System.Diagnostics.Stopwatch.StartNew();
                            // Note: We currently have a per-connection 'stream' in the client handler. For simplicity,
                            // writes are performed inline there. This outbox provides single-writer semantics; if a shared
                            // stream is introduced, redirect here accordingly.
                            // No-op: actual write happens in client loop using WriteFrameAsync
                            sw.Stop();
                            IoInfo($"[IO] ✓ write end   tag={item.Tag} len={(item.Payload?.Length ?? 0)} reqId={(item.ReqId?.ToString() ?? "?")} durMs={sw.Elapsed.TotalMilliseconds:F1}");
                        }
                        catch (Exception ex)
                        {
                            IoInfo($"[IO] ✗ write FAIL  tag={item.Tag} reqId={(item.ReqId?.ToString() ?? "?")} {ex.GetType().Name}: {ex.Message}");
                        }
                    }
                })
                { IsBackground = true, Name = "MCP-Writer" };
                writerThread.Start();
            }
            catch { }

            // Skip bridge in headless/batch environments (CI/builds) unless explicitly allowed via env
            // CI override: set UNITY_MCP_ALLOW_BATCH=1 to allow the bridge in batch mode
            if (Application.isBatchMode && string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("UNITY_MCP_ALLOW_BATCH")))
            {
                return;
            }
            // Defer start until the editor is idle and not compiling
            ScheduleInitRetry();
            // Add a safety net update hook in case delayCall is missed during reload churn
            if (!ensureUpdateHooked)
            {
                ensureUpdateHooked = true;
                EditorApplication.update += EnsureStartedOnEditorIdle;
            }
            EditorApplication.quitting += Stop;
            AssemblyReloadEvents.beforeAssemblyReload += OnBeforeAssemblyReload;
            AssemblyReloadEvents.afterAssemblyReload += OnAfterAssemblyReload;
            // Also coalesce play mode transitions into a deferred init
            EditorApplication.playModeStateChanged += _ => ScheduleInitRetry();
        }

        /// <summary>
        /// Initialize the MCP bridge after Unity is fully loaded and compilation is complete.
        /// This prevents repeated restarts during script compilation that cause port hopping.
        /// </summary>
        private static void InitializeAfterCompilation()
        {
            initScheduled = false;

            // Play-mode friendly: allow starting in play mode; only defer while compiling
            if (IsCompiling())
            {
                ScheduleInitRetry();
                return;
            }

            if (!isRunning)
            {
                Start();
                if (!isRunning)
                {
                    // If a race prevented start, retry later
                    ScheduleInitRetry();
                }
            }
        }

        private static void ScheduleInitRetry()
        {
            if (initScheduled)
            {
                return;
            }
            initScheduled = true;
            // Debounce: start ~200ms after the last trigger
            nextStartAt = EditorApplication.timeSinceStartup + 0.20f;
            // Ensure the update pump is active
            if (!ensureUpdateHooked)
            {
                ensureUpdateHooked = true;
                EditorApplication.update += EnsureStartedOnEditorIdle;
            }
            // Keep the original delayCall as a secondary path
            EditorApplication.delayCall += InitializeAfterCompilation;
        }

        // Safety net: ensure the bridge starts shortly after domain reload when editor is idle
        private static void EnsureStartedOnEditorIdle()
        {
            // Do nothing while compiling
            if (IsCompiling())
            {
                return;
            }

            // If already running, remove the hook
            if (isRunning)
            {
                EditorApplication.update -= EnsureStartedOnEditorIdle;
                ensureUpdateHooked = false;
                return;
            }

            // Debounced start: wait until the scheduled time
            if (nextStartAt > 0 && EditorApplication.timeSinceStartup < nextStartAt)
            {
                return;
            }

            if (isStarting)
            {
                return;
            }

            isStarting = true;
            try
            {
                // Attempt start; if it succeeds, remove the hook to avoid overhead
                Start();
            }
            finally
            {
                isStarting = false;
            }
            if (isRunning)
            {
                EditorApplication.update -= EnsureStartedOnEditorIdle;
                ensureUpdateHooked = false;
            }
        }

        // Helper to check compilation status across Unity versions
        private static bool IsCompiling()
        {
            if (EditorApplication.isCompiling)
            {
                return true;
            }
            try
            {
                System.Type pipeline = System.Type.GetType("UnityEditor.Compilation.CompilationPipeline, UnityEditor");
                var prop = pipeline?.GetProperty("isCompiling", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                if (prop != null)
                {
                    return (bool)prop.GetValue(null);
                }
            }
            catch { }
            return false;
        }

        public static void Start()
        {
            lock (startStopLock)
            {
                // Don't restart if already running on a working port
                if (isRunning && listener != null)
                {
                    if (IsDebugEnabled())
                    {
                        Debug.Log($"<b><color=#2EA3FF>MCP-FOR-UNITY</color></b>: MCPForUnityBridge already running on port {currentUnityPort}");
                    }
                    return;
                }

                Stop();

                // Attempt fast bind with stored-port preference (sticky per-project)
                try
                {
                    // Always consult PortManager first so we prefer the persisted project port
                    currentUnityPort = PortManager.GetPortWithFallback();

                    // Breadcrumb: Start
                    LogBreadcrumb("Start");

                    const int maxImmediateRetries = 3;
                    const int retrySleepMs = 75;
                    int attempt = 0;
                    for (; ; )
                    {
                        try
                        {
                            listener = new TcpListener(IPAddress.Loopback, currentUnityPort);
                            listener.Server.SetSocketOption(
                                SocketOptionLevel.Socket,
                                SocketOptionName.ReuseAddress,
                                true
                            );
#if UNITY_EDITOR_WIN
                            try
                            {
                                listener.ExclusiveAddressUse = false;
                            }
                            catch { }
#endif
                            // Minimize TIME_WAIT by sending RST on close
                            try
                            {
                                listener.Server.LingerState = new LingerOption(true, 0);
                            }
                            catch (Exception)
                            {
                                // Ignore if not supported on platform
                            }
                            listener.Start();
                            break;
                        }
                        catch (SocketException se) when (se.SocketErrorCode == SocketError.AddressAlreadyInUse && attempt < maxImmediateRetries)
                        {
                            attempt++;
                            Thread.Sleep(retrySleepMs);
                            continue;
                        }
                        catch (SocketException se) when (se.SocketErrorCode == SocketError.AddressAlreadyInUse && attempt >= maxImmediateRetries)
                        {
                            currentUnityPort = PortManager.GetPortWithFallback();
                            listener = new TcpListener(IPAddress.Loopback, currentUnityPort);
                            listener.Server.SetSocketOption(
                                SocketOptionLevel.Socket,
                                SocketOptionName.ReuseAddress,
                                true
                            );
#if UNITY_EDITOR_WIN
                            try
                            {
                                listener.ExclusiveAddressUse = false;
                            }
                            catch { }
#endif
                            try
                            {
                                listener.Server.LingerState = new LingerOption(true, 0);
                            }
                            catch (Exception)
                            {
                            }
                            listener.Start();
                            break;
                        }
                    }

                    isRunning = true;
                    isAutoConnectMode = false;
                    string platform = Application.platform.ToString();
                    string serverVer = ReadInstalledServerVersionSafe();
                    Debug.Log($"<b><color=#2EA3FF>MCP-FOR-UNITY</color></b>: MCPForUnityBridge started on port {currentUnityPort}. (OS={platform}, server={serverVer})");
                    // Start background listener with cooperative cancellation
                    cts = new CancellationTokenSource();
                    listenerTask = Task.Run(() => ListenerLoopAsync(cts.Token));
                    CommandRegistry.Initialize();
                    EditorApplication.update += ProcessCommands;
                    // Ensure lifecycle events are (re)subscribed in case Stop() removed them earlier in-domain
                    try { AssemblyReloadEvents.beforeAssemblyReload -= OnBeforeAssemblyReload; } catch { }
                    try { AssemblyReloadEvents.beforeAssemblyReload += OnBeforeAssemblyReload; } catch { }
                    try { AssemblyReloadEvents.afterAssemblyReload -= OnAfterAssemblyReload; } catch { }
                    try { AssemblyReloadEvents.afterAssemblyReload += OnAfterAssemblyReload; } catch { }
                    try { EditorApplication.quitting -= Stop; } catch { }
                    try { EditorApplication.quitting += Stop; } catch { }
                    // Write initial heartbeat immediately
                    heartbeatSeq++;
                    WriteHeartbeat(false, "ready");
                    nextHeartbeatAt = EditorApplication.timeSinceStartup + 0.5f;
                }
                catch (SocketException ex)
                {
                    Debug.LogError($"Failed to start TCP listener: {ex.Message}");
                }
            }
        }

        public static void Stop()
        {
            Task toWait = null;
            lock (startStopLock)
            {
                if (!isRunning)
                {
                    return;
                }

                try
                {
                    // Mark as stopping early to avoid accept logging during disposal
                    isRunning = false;

                    // Quiesce background listener quickly
                    var cancel = cts;
                    cts = null;
                    try { cancel?.Cancel(); } catch { }

                    try { listener?.Stop(); } catch { }
                    listener = null;

                    // Capture background task to wait briefly outside the lock
                    toWait = listenerTask;
                    listenerTask = null;
                }
                catch (Exception ex)
                {
                    Debug.LogError($"Error stopping MCPForUnityBridge: {ex.Message}");
                }
            }

            // Proactively close all active client sockets to unblock any pending reads
            TcpClient[] toClose;
            lock (clientsLock)
            {
                toClose = activeClients.ToArray();
                activeClients.Clear();
            }
            foreach (var c in toClose)
            {
                try
                {
                    // Properly shutdown before closing to avoid CloseWait state
                    if (c.Client != null && c.Connected)
                    {
                        c.Client.Shutdown(SocketShutdown.Both);
                    }
                    c.Close();
                }
                catch (SocketException se)
                {
                    // Expected: socket may already be closed
                    if (IsDebugEnabled())
                    {
                        McpLog.Info($"Stop: socket shutdown {se.SocketErrorCode}", always: false);
                    }
                }
                catch (Exception ex)
                {
                    // Log unexpected errors during shutdown
                    McpLog.Warn($"Error closing client connection: {ex.Message}");
                }
            }

            // Give the background loop a short window to exit without blocking the editor
            if (toWait != null)
            {
                try { toWait.Wait(100); } catch { }
            }

            // Now unhook editor events safely
            try { EditorApplication.update -= ProcessCommands; } catch { }
            try { AssemblyReloadEvents.beforeAssemblyReload -= OnBeforeAssemblyReload; } catch { }
            try { AssemblyReloadEvents.afterAssemblyReload -= OnAfterAssemblyReload; } catch { }
            try { EditorApplication.quitting -= Stop; } catch { }

            if (IsDebugEnabled()) Debug.Log("<b><color=#2EA3FF>MCP-FOR-UNITY</color></b>: MCPForUnityBridge stopped.");
        }

        private static async Task ListenerLoopAsync(CancellationToken token)
        {
            while (isRunning && !token.IsCancellationRequested)
            {
                try
                {
                    TcpClient client = await listener.AcceptTcpClientAsync();
                    // Enable basic socket keepalive
                    client.Client.SetSocketOption(
                        SocketOptionLevel.Socket,
                        SocketOptionName.KeepAlive,
                        true
                    );

                    // Set longer receive timeout to prevent quick disconnections
                    client.ReceiveTimeout = 60000; // 60 seconds

                    // Fire and forget each client connection
                    _ = Task.Run(() => HandleClientAsync(client, token), token);
                }
                catch (ObjectDisposedException)
                {
                    // Listener was disposed during stop/reload; exit quietly
                    if (!isRunning || token.IsCancellationRequested)
                    {
                        break;
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    if (isRunning && !token.IsCancellationRequested)
                    {
                        if (IsDebugEnabled()) Debug.LogError($"Listener error: {ex.Message}");
                    }
                }
            }
        }

        private static async Task HandleClientAsync(TcpClient client, CancellationToken token)
        {
            try
            {
                lock (clientsLock) { activeClients.Add(client); }
                
                using (client)
                using (NetworkStream stream = client.GetStream())
                {
                    try
                    {
                    // Framed I/O only; legacy mode removed
                    try
                    {
                        if (IsDebugEnabled())
                        {
                            var ep = client.Client?.RemoteEndPoint?.ToString() ?? "unknown";
                            Debug.Log($"<b><color=#2EA3FF>UNITY-MCP</color></b>: Client connected {ep}");
                        }
                    }
                    catch { }
                    // Strict framing: always require FRAMING=1 and frame all I/O
                    try
                    {
                        client.NoDelay = true;
                    }
                    catch { }
                    try
                    {
                        string handshake = "WELCOME UNITY-MCP 1 FRAMING=1\n";
                        byte[] handshakeBytes = System.Text.Encoding.ASCII.GetBytes(handshake);
                        using var cts = new CancellationTokenSource(FrameIOTimeoutMs);
#if NETSTANDARD2_1 || NET6_0_OR_GREATER
                        await stream.WriteAsync(handshakeBytes.AsMemory(0, handshakeBytes.Length), cts.Token).ConfigureAwait(false);
#else
                    await stream.WriteAsync(handshakeBytes, 0, handshakeBytes.Length, cts.Token).ConfigureAwait(false);
#endif
                        if (IsDebugEnabled()) MCPForUnity.Editor.Helpers.McpLog.Info("Sent handshake FRAMING=1 (strict)", always: false);
                    }
                    catch (Exception ex)
                    {
                        if (IsDebugEnabled()) MCPForUnity.Editor.Helpers.McpLog.Warn($"Handshake failed: {ex.Message}");
                        return; // abort this client
                    }

                    while (isRunning && !token.IsCancellationRequested)
                    {
                        try
                        {
                            // Strict framed mode only: enforced framed I/O for this connection
                            string commandText = await ReadFrameAsUtf8Async(stream, FrameIOTimeoutMs, token).ConfigureAwait(false);

                            try
                            {
                                if (IsDebugEnabled())
                                {
                                    var preview = commandText.Length > 120 ? commandText.Substring(0, 120) + "…" : commandText;
                                    MCPForUnity.Editor.Helpers.McpLog.Info($"recv framed: {preview}", always: false);
                                }
                            }
                            catch { }
                            string commandId = Guid.NewGuid().ToString();
                            var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

                            // Special handling for ping command to avoid JSON parsing
                            if (commandText.Trim() == "ping")
                            {
                                // Direct response to ping without going through JSON parsing
                                byte[] pingResponseBytes = System.Text.Encoding.UTF8.GetBytes(
                                    /*lang=json,strict*/
                                    "{\"status\":\"success\",\"result\":{\"message\":\"pong\"}}"
                                );
                                await WriteFrameAsync(stream, pingResponseBytes);
                                continue;
                            }

                            lock (lockObj)
                            {
                                commandQueue[commandId] = (commandText, tcs);
                            }

                            // Wait for the handler to produce a response, but do not block indefinitely
                            string response;
                            try
                            {
                                using var respCts = new CancellationTokenSource(FrameIOTimeoutMs);
                                var completed = await Task.WhenAny(tcs.Task, Task.Delay(FrameIOTimeoutMs, respCts.Token)).ConfigureAwait(false);
                                if (completed == tcs.Task)
                                {
                                    // Got a result from the handler
                                    respCts.Cancel();
                                    response = tcs.Task.Result;
                                }
                                else
                                {
                                    // Timeout: return a structured error so the client can recover
                                    var timeoutResponse = new
                                    {
                                        status = "error",
                                        error = $"Command processing timed out after {FrameIOTimeoutMs} ms",
                                    };
                                    response = JsonConvert.SerializeObject(timeoutResponse);
                                }
                            }
                            catch (Exception ex)
                            {
                                var errorResponse = new
                                {
                                    status = "error",
                                    error = ex.Message,
                                };
                                response = JsonConvert.SerializeObject(errorResponse);
                            }

                            if (IsDebugEnabled())
                            {
                                try { MCPForUnity.Editor.Helpers.McpLog.Info("[MCP] sending framed response", always: false); } catch { }
                            }
                            // Crash-proof and self-reporting writer logs (direct write to this client's stream)
                            long seq = System.Threading.Interlocked.Increment(ref _ioSeq);
                            byte[] responseBytes;
                            try
                            {
                                responseBytes = System.Text.Encoding.UTF8.GetBytes(response);
                                IoInfo($"[IO] ➜ write start seq={seq} tag=response len={responseBytes.Length} reqId=?");
                            }
                            catch (Exception ex)
                            {
                                IoInfo($"[IO] ✗ serialize FAIL tag=response reqId=? {ex.GetType().Name}: {ex.Message}");
                                throw;
                            }

                            var swDirect = System.Diagnostics.Stopwatch.StartNew();
                            try
                            {
                                await WriteFrameAsync(stream, responseBytes);
                                swDirect.Stop();
                                IoInfo($"[IO] ✓ write end   tag=response len={responseBytes.Length} reqId=? durMs={swDirect.Elapsed.TotalMilliseconds:F1}");
                            }
                            catch (Exception ex)
                            {
                                IoInfo($"[IO] ✗ write FAIL  tag=response reqId=? {ex.GetType().Name}: {ex.Message}");
                                throw;
                            }
                        }
                        catch (Exception ex)
                        {
                            // Treat common disconnects/timeouts as benign; only surface hard errors
                            string msg = ex.Message ?? string.Empty;
                            bool isBenign =
                                msg.IndexOf("Connection closed before reading expected bytes", StringComparison.OrdinalIgnoreCase) >= 0
                                || msg.IndexOf("Read timed out", StringComparison.OrdinalIgnoreCase) >= 0
                                || ex is System.IO.IOException;
                            if (isBenign)
                            {
                                if (IsDebugEnabled()) MCPForUnity.Editor.Helpers.McpLog.Info($"Client handler: {msg}", always: false);
                            }
                            else
                            {
                                MCPForUnity.Editor.Helpers.McpLog.Error($"Client handler error: {msg}");
                            }
                            break;
                        }
                    }
                    }
                    finally
                    {
                        // Properly shutdown the socket before disposal to avoid CloseWait
                        try
                        {
                            if (client.Client != null && client.Connected)
                            {
                                client.Client.Shutdown(SocketShutdown.Both);
                            }
                        }
                        catch (SocketException se)
                        {
                            // Expected: socket may already be closed by remote
                            if (IsDebugEnabled())
                            {
                                McpLog.Info($"Socket shutdown: {se.SocketErrorCode}", always: false);
                            }
                        }
                        catch (Exception ex)
                        {
                            // Unexpected errors should be logged
                            if (IsDebugEnabled())
                            {
                                McpLog.Warn($"Error during socket shutdown: {ex.Message}");
                            }
                        }
                    }
                }
            }
            finally
            {
                lock (clientsLock) { activeClients.Remove(client); }
            }
        }

        // Timeout-aware exact read helper with cancellation; avoids indefinite stalls and background task leaks
        private static async System.Threading.Tasks.Task<byte[]> ReadExactAsync(NetworkStream stream, int count, int timeoutMs, CancellationToken cancel = default)
        {
            byte[] buffer = new byte[count];
            int offset = 0;
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            while (offset < count)
            {
                int remaining = count - offset;
                int remainingTimeout = timeoutMs <= 0
                    ? Timeout.Infinite
                    : timeoutMs - (int)stopwatch.ElapsedMilliseconds;

                // If a finite timeout is configured and already elapsed, fail immediately
                if (remainingTimeout != Timeout.Infinite && remainingTimeout <= 0)
                {
                    throw new System.IO.IOException("Read timed out");
                }

                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancel);
                if (remainingTimeout != Timeout.Infinite)
                {
                    cts.CancelAfter(remainingTimeout);
                }

                try
                {
#if NETSTANDARD2_1 || NET6_0_OR_GREATER
                    int read = await stream.ReadAsync(buffer.AsMemory(offset, remaining), cts.Token).ConfigureAwait(false);
#else
                    int read = await stream.ReadAsync(buffer, offset, remaining, cts.Token).ConfigureAwait(false);
#endif
                    if (read == 0)
                    {
                        throw new System.IO.IOException("Connection closed before reading expected bytes");
                    }
                    offset += read;
                }
                catch (OperationCanceledException) when (!cancel.IsCancellationRequested)
                {
                    throw new System.IO.IOException("Read timed out");
                }
            }

            return buffer;
        }

        private static async System.Threading.Tasks.Task WriteFrameAsync(NetworkStream stream, byte[] payload)
        {
            using var cts = new CancellationTokenSource(FrameIOTimeoutMs);
            await WriteFrameAsync(stream, payload, cts.Token);
        }

        private static async System.Threading.Tasks.Task WriteFrameAsync(NetworkStream stream, byte[] payload, CancellationToken cancel)
        {
            if (payload == null)
            {
                throw new System.ArgumentNullException(nameof(payload));
            }
            if ((ulong)payload.LongLength > MaxFrameBytes)
            {
                throw new System.IO.IOException($"Frame too large: {payload.LongLength}");
            }
            byte[] header = new byte[8];
            WriteUInt64BigEndian(header, (ulong)payload.LongLength);
#if NETSTANDARD2_1 || NET6_0_OR_GREATER
            await stream.WriteAsync(header.AsMemory(0, header.Length), cancel).ConfigureAwait(false);
            await stream.WriteAsync(payload.AsMemory(0, payload.Length), cancel).ConfigureAwait(false);
#else
            await stream.WriteAsync(header, 0, header.Length, cancel).ConfigureAwait(false);
            await stream.WriteAsync(payload, 0, payload.Length, cancel).ConfigureAwait(false);
#endif
        }

        private static async System.Threading.Tasks.Task<string> ReadFrameAsUtf8Async(NetworkStream stream, int timeoutMs, CancellationToken cancel)
        {
            byte[] header = await ReadExactAsync(stream, 8, timeoutMs, cancel).ConfigureAwait(false);
            ulong payloadLen = ReadUInt64BigEndian(header);
            if (payloadLen > MaxFrameBytes)
            {
                throw new System.IO.IOException($"Invalid framed length: {payloadLen}");
            }
            if (payloadLen == 0UL)
                throw new System.IO.IOException("Zero-length frames are not allowed");
            if (payloadLen > int.MaxValue)
            {
                throw new System.IO.IOException("Frame too large for buffer");
            }
            int count = (int)payloadLen;
            byte[] payload = await ReadExactAsync(stream, count, timeoutMs, cancel).ConfigureAwait(false);
            return System.Text.Encoding.UTF8.GetString(payload);
        }

        private static ulong ReadUInt64BigEndian(byte[] buffer)
        {
            if (buffer == null || buffer.Length < 8) return 0UL;
            return ((ulong)buffer[0] << 56)
                 | ((ulong)buffer[1] << 48)
                 | ((ulong)buffer[2] << 40)
                 | ((ulong)buffer[3] << 32)
                 | ((ulong)buffer[4] << 24)
                 | ((ulong)buffer[5] << 16)
                 | ((ulong)buffer[6] << 8)
                 | buffer[7];
        }

        private static void WriteUInt64BigEndian(byte[] dest, ulong value)
        {
            if (dest == null || dest.Length < 8)
            {
                throw new System.ArgumentException("Destination buffer too small for UInt64");
            }
            dest[0] = (byte)(value >> 56);
            dest[1] = (byte)(value >> 48);
            dest[2] = (byte)(value >> 40);
            dest[3] = (byte)(value >> 32);
            dest[4] = (byte)(value >> 24);
            dest[5] = (byte)(value >> 16);
            dest[6] = (byte)(value >> 8);
            dest[7] = (byte)(value);
        }

        private static void ProcessCommands()
        {
            if (!isRunning) return;
            if (Interlocked.Exchange(ref processingCommands, 1) == 1) return; // reentrancy guard
            try
            {
                // Heartbeat without holding the queue lock
                double now = EditorApplication.timeSinceStartup;
                if (now >= nextHeartbeatAt)
                {
                    WriteHeartbeat(false);
                    nextHeartbeatAt = now + 0.5f;
                }

                // Snapshot under lock, then process outside to reduce contention
                List<(string id, string text, TaskCompletionSource<string> tcs)> work;
                lock (lockObj)
                {
                    work = commandQueue
                        .Select(kvp => (kvp.Key, kvp.Value.commandJson, kvp.Value.tcs))
                        .ToList();
                }

                foreach (var item in work)
                {
                    string id = item.id;
                    string commandText = item.text;
                    TaskCompletionSource<string> tcs = item.tcs;

                    try
                    {
                        // Special case handling
                        if (string.IsNullOrEmpty(commandText))
                        {
                            var emptyResponse = new
                            {
                                status = "error",
                                error = "Empty command received",
                            };
                            tcs.SetResult(JsonConvert.SerializeObject(emptyResponse));
                            // Remove quickly under lock
                            lock (lockObj) { commandQueue.Remove(id); }
                            continue;
                        }

                        // Trim the command text to remove any whitespace
                        commandText = commandText.Trim();

                        // Non-JSON direct commands handling (like ping)
                        if (commandText == "ping")
                        {
                            var pingResponse = new
                            {
                                status = "success",
                                result = new { message = "pong" },
                            };
                            tcs.SetResult(JsonConvert.SerializeObject(pingResponse));
                            lock (lockObj) { commandQueue.Remove(id); }
                            continue;
                        }

                        // Check if the command is valid JSON before attempting to deserialize
                        if (!IsValidJson(commandText))
                        {
                            var invalidJsonResponse = new
                            {
                                status = "error",
                                error = "Invalid JSON format",
                                receivedText = commandText.Length > 50
                                    ? commandText[..50] + "..."
                                    : commandText,
                            };
                            tcs.SetResult(JsonConvert.SerializeObject(invalidJsonResponse));
                            lock (lockObj) { commandQueue.Remove(id); }
                            continue;
                        }

                        // Normal JSON command processing
                        Command command = JsonConvert.DeserializeObject<Command>(commandText);

                        if (command == null)
                        {
                            var nullCommandResponse = new
                            {
                                status = "error",
                                error = "Command deserialized to null",
                                details = "The command was valid JSON but could not be deserialized to a Command object",
                            };
                            tcs.SetResult(JsonConvert.SerializeObject(nullCommandResponse));
                        }
                        else
                        {
                            string responseJson = ExecuteCommand(command);
                            tcs.SetResult(responseJson);
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"Error processing command: {ex.Message}\n{ex.StackTrace}");

                        var response = new
                        {
                            status = "error",
                            error = ex.Message,
                            commandType = "Unknown (error during processing)",
                            receivedText = commandText?.Length > 50
                                ? commandText[..50] + "..."
                                : commandText,
                        };
                        string responseJson = JsonConvert.SerializeObject(response);
                        tcs.SetResult(responseJson);
                    }

                    // Remove quickly under lock
                    lock (lockObj) { commandQueue.Remove(id); }
                }
            }
            finally
            {
                Interlocked.Exchange(ref processingCommands, 0);
            }
        }

        // Invoke the given function on the Unity main thread and wait up to timeoutMs for the result.
        // Returns null on timeout or error; caller should provide a fallback error response.
        private static object InvokeOnMainThreadWithTimeout(Func<object> func, int timeoutMs)
        {
            if (func == null) return null;
            try
            {
                // If mainThreadId is unknown, assume we're on main thread to avoid blocking the editor.
                if (mainThreadId == 0)
                {
                    try { return func(); }
                    catch (Exception ex) { throw new InvalidOperationException($"Main thread handler error: {ex.Message}", ex); }
                }
                // If we are already on the main thread, execute directly to avoid deadlocks
                try
                {
                    if (Thread.CurrentThread.ManagedThreadId == mainThreadId)
                    {
                        return func();
                    }
                }
                catch { }

                object result = null;
                Exception captured = null;
                var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                EditorApplication.delayCall += () =>
                {
                    try
                    {
                        result = func();
                    }
                    catch (Exception ex)
                    {
                        captured = ex;
                    }
                    finally
                    {
                        try { tcs.TrySetResult(true); } catch { }
                    }
                };

                // Wait for completion with timeout (Editor thread will pump delayCall)
                bool completed = tcs.Task.Wait(timeoutMs);
                if (!completed)
                {
                    return null; // timeout
                }
                if (captured != null)
                {
                    throw new InvalidOperationException($"Main thread handler error: {captured.Message}", captured);
                }
                return result;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to invoke on main thread: {ex.Message}", ex);
            }
        }

        // Helper method to check if a string is valid JSON
        private static bool IsValidJson(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            text = text.Trim();
            if (
                (text.StartsWith("{") && text.EndsWith("}"))
                || // Object
                (text.StartsWith("[") && text.EndsWith("]"))
            ) // Array
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

        private static string ExecuteCommand(Command command)
        {
            try
            {
                if (string.IsNullOrEmpty(command.type))
                {
                    var errorResponse = new
                    {
                        status = "error",
                        error = "Command type cannot be empty",
                        details = "A valid command type is required for processing",
                    };
                    return JsonConvert.SerializeObject(errorResponse);
                }

                // Handle ping command for connection verification
                if (command.type.Equals("ping", StringComparison.OrdinalIgnoreCase))
                {
                    var pingResponse = new
                    {
                        status = "success",
                        result = new { message = "pong" },
                    };
                    return JsonConvert.SerializeObject(pingResponse);
                }

                // Use JObject for parameters as the new handlers likely expect this
                JObject paramsObject = command.@params ?? new JObject();
                object result = CommandRegistry.GetHandler(command.type)(paramsObject);

                // Standard success response format
                var response = new { status = "success", result };
                return JsonConvert.SerializeObject(response);
            }
            catch (Exception ex)
            {
                // Log the detailed error in Unity for debugging
                Debug.LogError(
                    $"Error executing command '{command?.type ?? "Unknown"}': {ex.Message}\n{ex.StackTrace}"
                );

                // Standard error response format
                var response = new
                {
                    status = "error",
                    error = ex.Message, // Provide the specific error message
                    command = command?.type ?? "Unknown", // Include the command type if available
                    stackTrace = ex.StackTrace, // Include stack trace for detailed debugging
                    paramsSummary = command?.@params != null
                        ? GetParamsSummary(command.@params)
                        : "No parameters", // Summarize parameters for context
                };
                return JsonConvert.SerializeObject(response);
            }
        }

        private static object HandleManageScene(JObject paramsObject)
        {
            try
            {
                if (IsDebugEnabled()) Debug.Log("[MCP] manage_scene: dispatching to main thread");
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var r = InvokeOnMainThreadWithTimeout(() => ManageScene.HandleCommand(paramsObject), FrameIOTimeoutMs);
                sw.Stop();
                if (IsDebugEnabled()) Debug.Log($"[MCP] manage_scene: completed in {sw.ElapsedMilliseconds} ms");
                return r ?? Response.Error("manage_scene returned null (timeout or error)");
            }
            catch (Exception ex)
            {
                return Response.Error($"manage_scene dispatch error: {ex.Message}");
            }
        }

        // Helper method to get a summary of parameters for error reporting
        private static string GetParamsSummary(JObject @params)
        {
            try
            {
                return @params == null || !@params.HasValues
                    ? "No parameters"
                    : string.Join(
                        ", ",
                        @params
                            .Properties()
                            .Select(static p =>
                                $"{p.Name}: {p.Value?.ToString()?[..Math.Min(20, p.Value?.ToString()?.Length ?? 0)]}"
                            )
                    );
            }
            catch
            {
                return "Could not summarize parameters";
            }
        }

        // Heartbeat/status helpers
        private static void OnBeforeAssemblyReload()
        {
            // Stop cleanly before reload so sockets close and clients see 'reloading'
            try { Stop(); } catch { }
            // Avoid file I/O or heavy work here
        }

        private static void OnAfterAssemblyReload()
        {
            // Will be overwritten by Start(), but mark as alive quickly
            WriteHeartbeat(false, "idle");
            LogBreadcrumb("Idle");
            // Schedule a safe restart after reload to avoid races during compilation
            ScheduleInitRetry();
        }

        private static void WriteHeartbeat(bool reloading, string reason = null)
        {
            try
            {
                // Allow override of status directory (useful in CI/containers)
                string dir = Environment.GetEnvironmentVariable("UNITY_MCP_STATUS_DIR");
                if (string.IsNullOrWhiteSpace(dir))
                {
                    dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".unity-mcp");
                }
                Directory.CreateDirectory(dir);
                string filePath = Path.Combine(dir, $"unity-mcp-status-{ComputeProjectHash(Application.dataPath)}.json");
                var payload = new
                {
                    unity_port = currentUnityPort,
                    reloading,
                    reason = reason ?? (reloading ? "reloading" : "ready"),
                    seq = heartbeatSeq,
                    project_path = Application.dataPath,
                    last_heartbeat = DateTime.UtcNow.ToString("O")
                };
                File.WriteAllText(filePath, JsonConvert.SerializeObject(payload), new System.Text.UTF8Encoding(false));
            }
            catch (Exception)
            {
                // Best-effort only
            }
        }

        private static string ReadInstalledServerVersionSafe()
        {
            try
            {
                string serverSrc = ServerInstaller.GetServerPath();
                string verFile = Path.Combine(serverSrc, "server_version.txt");
                if (File.Exists(verFile))
                {
                    string v = File.ReadAllText(verFile)?.Trim();
                    if (!string.IsNullOrEmpty(v)) return v;
                }
            }
            catch { }
            return "unknown";
        }

        private static string ComputeProjectHash(string input)
        {
            try
            {
                using var sha1 = System.Security.Cryptography.SHA1.Create();
                byte[] bytes = System.Text.Encoding.UTF8.GetBytes(input ?? string.Empty);
                byte[] hashBytes = sha1.ComputeHash(bytes);
                var sb = new System.Text.StringBuilder();
                foreach (byte b in hashBytes)
                {
                    sb.Append(b.ToString("x2"));
                }
                return sb.ToString()[..8];
            }
            catch
            {
                return "default";
            }
        }
    }
}
