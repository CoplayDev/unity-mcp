using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using MCPForUnity.Editor.Constants;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Models;
using MCPForUnity.Editor.Services;
using MCPForUnity.Editor.Services.Transport;
using MCPForUnity.Editor.Tools;
using MCPForUnity.Editor.Tools.Prefabs;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace MCPForUnity.Editor.Services.Transport.Transports
{
    class Outbound
    {
        public byte[] Payload;
        public string Tag;
        public int? ReqId;
    }

    class QueuedCommand
    {
        public string CommandJson;
        public TaskCompletionSource<string> Tcs;
        public bool IsExecuting;
    }

    [InitializeOnLoad]
    public static class StdioBridgeHost
    {
        private static TcpListener listener;
        private static bool isRunning = false;
        private static readonly object lockObj = new();
        private static readonly object startStopLock = new();
        private static readonly object clientsLock = new();
        private static readonly HashSet<TcpClient> activeClients = new();
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
        private static Dictionary<string, QueuedCommand> commandQueue = new();
        private static int mainThreadId;
        private static int currentUnityPort = 6400;
        private static bool isAutoConnectMode = false;
        private const ulong MaxFrameBytes = 64UL * 1024 * 1024;
        private const int FrameIOTimeoutMs = 30000;

        private static long _ioSeq = 0;
        private static void IoInfo(string s) { McpLog.Info(s, always: false); }

        private static bool IsDebugEnabled()
        {
            try { return EditorPrefs.GetBool(EditorPrefKeys.DebugLogs, false); } catch { return false; }
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

        public static void StartAutoConnect()
        {
            Stop();

            try
            {
                currentUnityPort = PortManager.GetPortWithFallback();
                Start();
                isAutoConnectMode = true;

                TelemetryHelper.RecordBridgeStartup();
            }
            catch (Exception ex)
            {
                McpLog.Error($"Auto-connect failed: {ex.Message}");
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

        static StdioBridgeHost()
        {
            try { mainThreadId = Thread.CurrentThread.ManagedThreadId; } catch { mainThreadId = 0; }
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

            if (Application.isBatchMode && string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("UNITY_MCP_ALLOW_BATCH")))
            {
                return;
            }
            if (ShouldAutoStartBridge())
            {
                ScheduleInitRetry();
                if (!ensureUpdateHooked)
                {
                    ensureUpdateHooked = true;
                    EditorApplication.update += EnsureStartedOnEditorIdle;
                }
            }
            EditorApplication.quitting += Stop;
            EditorApplication.playModeStateChanged += _ =>
            {
                if (ShouldAutoStartBridge())
                {
                    ScheduleInitRetry();
                }
            };
        }

        private static void InitializeAfterCompilation()
        {
            initScheduled = false;

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
            nextStartAt = EditorApplication.timeSinceStartup + 0.20f;
            if (!ensureUpdateHooked)
            {
                ensureUpdateHooked = true;
                EditorApplication.update += EnsureStartedOnEditorIdle;
            }
            EditorApplication.delayCall += InitializeAfterCompilation;
        }

        private static bool ShouldAutoStartBridge()
        {
            try
            {
                bool useHttpTransport = EditorConfigurationCache.Instance.UseHttpTransport;
                return !useHttpTransport;
            }
            catch
            {
                return true;
            }
        }

        private static void EnsureStartedOnEditorIdle()
        {
            if (IsCompiling())
            {
                return;
            }

            if (isRunning)
            {
                EditorApplication.update -= EnsureStartedOnEditorIdle;
                ensureUpdateHooked = false;
                return;
            }

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

        private static bool IsCompiling()
        {
            if (EditorApplication.isCompiling)
            {
                return true;
            }
            try
            {
                Type pipeline = Type.GetType("UnityEditor.Compilation.CompilationPipeline, UnityEditor");
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
                if (isRunning && listener != null)
                {
                    if (IsDebugEnabled())
                    {
                        McpLog.Info($"StdioBridgeHost already running on port {currentUnityPort}");
                    }
                    return;
                }

                Stop();

                try
                {
                    currentUnityPort = PortManager.GetPortWithFallback();

                    LogBreadcrumb("Start");

                    const int maxImmediateRetries = 3;
                    const int retrySleepMs = 75;
                    int attempt = 0;
                    for (; ; )
                    {
                        try
                        {
                            listener = CreateConfiguredListener(currentUnityPort);
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
                            int oldPort = currentUnityPort;

                            // Before switching ports, give the old one a brief chance to release if it looks like ours
                            try
                            {
                                if (PortManager.IsPortUsedByMCPForUnity(oldPort))
                                {
                                    const int waitStepMs = 100;
                                    int waited = 0;
                                    while (waited < 300 && !PortManager.IsPortAvailable(oldPort))
                                    {
                                        Thread.Sleep(waitStepMs);
                                        waited += waitStepMs;
                                    }
                                }
                            }
                            catch { }

                            currentUnityPort = PortManager.DiscoverNewPort();

                            // Persist the new port so next time we start on this port
                            try
                            {
                                EditorPrefs.SetInt(EditorPrefKeys.UnitySocketPort, currentUnityPort);
                            }
                            catch { }

                            if (IsDebugEnabled())
                            {
                                if (currentUnityPort == oldPort)
                                {
                                    McpLog.Info($"Port {oldPort} became available, proceeding");
                                }
                                else
                                {
                                    McpLog.Info($"Port {oldPort} occupied, switching to port {currentUnityPort}");
                                }
                            }

                            listener = CreateConfiguredListener(currentUnityPort);
                            listener.Start();
                            break;
                        }
                    }

                    isRunning = true;
                    isAutoConnectMode = false;
                    string platform = Application.platform.ToString();
                    string serverVer = AssetPathUtility.GetPackageVersion();
                    McpLog.Info($"StdioBridgeHost started on port {currentUnityPort}. (OS={platform}, server={serverVer})");
                    cts = new CancellationTokenSource();
                    listenerTask = Task.Run(() => ListenerLoopAsync(cts.Token));
                    CommandRegistry.Initialize();
                    EditorApplication.update += ProcessCommands;
                    try { EditorApplication.quitting -= Stop; } catch { }
                    try { EditorApplication.quitting += Stop; } catch { }
                    heartbeatSeq++;
                    WriteHeartbeat(false, "ready");
                    nextHeartbeatAt = EditorApplication.timeSinceStartup + 0.5f;
                }
                catch (SocketException ex)
                {
                    McpLog.Error($"Failed to start TCP listener: {ex.Message}");
                }
            }
        }

        private static TcpListener CreateConfiguredListener(int port)
        {
            var newListener = new TcpListener(IPAddress.Loopback, port);
#if !UNITY_EDITOR_OSX
            newListener.Server.SetSocketOption(
                SocketOptionLevel.Socket,
                SocketOptionName.ReuseAddress,
                true
            );
#endif
#if UNITY_EDITOR_WIN
            try
            {
                newListener.ExclusiveAddressUse = false;
            }
            catch { }
#endif
            try
            {
                newListener.Server.LingerState = new LingerOption(true, 0);
            }
            catch (Exception)
            {
            }
            return newListener;
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
                    isRunning = false;

                    var cancel = cts;
                    cts = null;
                    try { cancel?.Cancel(); } catch { }

                    try { listener?.Stop(); } catch { }
                    listener = null;

                    toWait = listenerTask;
                    listenerTask = null;
                }
                catch (Exception ex)
                {
                    McpLog.Error($"Error stopping StdioBridgeHost: {ex.Message}");
                }
            }

            TcpClient[] toClose;
            lock (clientsLock)
            {
                toClose = activeClients.ToArray();
                activeClients.Clear();
            }
            foreach (var c in toClose)
            {
                try { c.Close(); } catch { }
            }

            if (toWait != null)
            {
                try { toWait.Wait(100); } catch { }
            }

            try { EditorApplication.update -= ProcessCommands; } catch { }
            try { EditorApplication.quitting -= Stop; } catch { }

            try
            {
                string dir = Environment.GetEnvironmentVariable("UNITY_MCP_STATUS_DIR");
                if (string.IsNullOrWhiteSpace(dir))
                {
                    dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".unity-mcp");
                }
                string statusFile = Path.Combine(dir, $"unity-mcp-status-{ComputeProjectHash(Application.dataPath)}.json");
                if (File.Exists(statusFile))
                {
                    File.Delete(statusFile);
                    if (IsDebugEnabled()) McpLog.Info($"Deleted status file: {statusFile}");
                }
            }
            catch (Exception ex)
            {
                if (IsDebugEnabled()) McpLog.Warn($"Failed to delete status file: {ex.Message}");
            }

            if (IsDebugEnabled()) McpLog.Info("StdioBridgeHost stopped.");
        }

        private static async Task ListenerLoopAsync(CancellationToken token)
        {
            while (isRunning && !token.IsCancellationRequested)
            {
                try
                {
                    TcpClient client = await listener.AcceptTcpClientAsync();
                    client.Client.SetSocketOption(
                        SocketOptionLevel.Socket,
                        SocketOptionName.KeepAlive,
                        true
                    );

                    client.ReceiveTimeout = 60000;

                    _ = Task.Run(() => HandleClientAsync(client, token), token);
                }
                catch (ObjectDisposedException)
                {
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
                        if (IsDebugEnabled()) McpLog.Error($"Listener error: {ex.Message}");
                    }
                }
            }
        }

        private static async Task HandleClientAsync(TcpClient client, CancellationToken token)
        {
            using (client)
            using (NetworkStream stream = client.GetStream())
            {
                lock (clientsLock) { activeClients.Add(client); }
                try
                {
                    try
                    {
                        if (IsDebugEnabled())
                        {
                            var ep = client.Client?.RemoteEndPoint?.ToString() ?? "unknown";
                            McpLog.Info($"Client connected {ep}");
                        }
                    }
                    catch { }
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
                        if (IsDebugEnabled()) McpLog.Info("Sent handshake FRAMING=1 (strict)", always: false);
                    }
                    catch (Exception ex)
                    {
                        if (IsDebugEnabled()) McpLog.Warn($"Handshake failed: {ex.Message}");
                        return;
                    }

                    while (isRunning && !token.IsCancellationRequested)
                    {
                        try
                        {
                            string commandText = await ReadFrameAsUtf8Async(stream, FrameIOTimeoutMs, token).ConfigureAwait(false);

                            try
                            {
                                if (IsDebugEnabled())
                                {
                                    var preview = commandText.Length > 120 ? commandText.Substring(0, 120) + "…" : commandText;
                                    McpLog.Info($"recv framed: {preview}", always: false);
                                }
                            }
                            catch { }
                            string commandId = Guid.NewGuid().ToString();
                            var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

                            if (commandText.Trim() == "ping")
                            {
                                byte[] pingResponseBytes = System.Text.Encoding.UTF8.GetBytes(
                                    "{\"status\":\"success\",\"result\":{\"message\":\"pong\"}}"
                                );
                                await WriteFrameAsync(stream, pingResponseBytes);
                                continue;
                            }

                            lock (lockObj)
                            {
                                commandQueue[commandId] = new QueuedCommand
                                {
                                    CommandJson = commandText,
                                    Tcs = tcs,
                                    IsExecuting = false
                                };
                            }

                            string response;
                            try
                            {
                                using var respCts = new CancellationTokenSource(FrameIOTimeoutMs);
                                var completed = await Task.WhenAny(tcs.Task, Task.Delay(FrameIOTimeoutMs, respCts.Token)).ConfigureAwait(false);
                                if (completed == tcs.Task)
                                {
                                    respCts.Cancel();
                                    response = tcs.Task.Result;
                                }
                                else
                                {
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
                                try { McpLog.Info("[MCP] sending framed response", always: false); } catch { }
                            }
                            long seq = Interlocked.Increment(ref _ioSeq);
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
                            string msg = ex.Message ?? string.Empty;
                            bool isBenign =
                                msg.IndexOf("Connection closed before reading expected bytes", StringComparison.OrdinalIgnoreCase) >= 0
                                || msg.IndexOf("Read timed out", StringComparison.OrdinalIgnoreCase) >= 0
                                || ex is IOException;
                            if (isBenign)
                            {
                                if (IsDebugEnabled()) McpLog.Info($"Client handler: {msg}", always: false);
                            }
                            else
                            {
                                McpLog.Error($"Client handler error: {msg}");
                            }
                            break;
                        }
                    }
                }
                finally
                {
                    lock (clientsLock) { activeClients.Remove(client); }
                }
            }
        }

        private static async Task<byte[]> ReadExactAsync(NetworkStream stream, int count, int timeoutMs, CancellationToken cancel = default)
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

                if (remainingTimeout != Timeout.Infinite && remainingTimeout <= 0)
                {
                    throw new IOException("Read timed out");
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
                        throw new IOException("Connection closed before reading expected bytes");
                    }
                    offset += read;
                }
                catch (OperationCanceledException) when (!cancel.IsCancellationRequested)
                {
                    throw new IOException("Read timed out");
                }
            }

            return buffer;
        }

        private static Task WriteFrameAsync(NetworkStream stream, byte[] payload)
        {
            using var cts = new CancellationTokenSource(FrameIOTimeoutMs);
            return WriteFrameAsync(stream, payload, cts.Token);
        }

        private static async Task WriteFrameAsync(NetworkStream stream, byte[] payload, CancellationToken cancel)
        {
            if (payload == null)
            {
                throw new ArgumentNullException(nameof(payload));
            }
            if ((ulong)payload.LongLength > MaxFrameBytes)
            {
                throw new IOException($"Frame too large: {payload.LongLength}");
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

        private static async Task<string> ReadFrameAsUtf8Async(NetworkStream stream, int timeoutMs, CancellationToken cancel)
        {
            byte[] header = await ReadExactAsync(stream, 8, timeoutMs, cancel).ConfigureAwait(false);
            ulong payloadLen = ReadUInt64BigEndian(header);
            if (payloadLen > MaxFrameBytes)
            {
                throw new IOException($"Invalid framed length: {payloadLen}");
            }
            if (payloadLen == 0UL)
                throw new IOException("Zero-length frames are not allowed");
            if (payloadLen > int.MaxValue)
            {
                throw new IOException("Frame too large for buffer");
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
                throw new ArgumentException("Destination buffer too small for UInt64");
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
            if (Interlocked.Exchange(ref processingCommands, 1) == 1) return;
            try
            {
                double now = EditorApplication.timeSinceStartup;
                if (now >= nextHeartbeatAt)
                {
                    WriteHeartbeat(false);
                    nextHeartbeatAt = now + 0.5f;
                }

                List<(string id, QueuedCommand command)> work;
                lock (lockObj)
                {
                    // Early exit inside lock to prevent per-frame List allocations (GitHub issue #577)
                    if (commandQueue.Count == 0)
                    {
                        return;
                    }

                    work = new List<(string, QueuedCommand)>(commandQueue.Count);
                    foreach (var kvp in commandQueue)
                    {
                        var queued = kvp.Value;
                        if (queued.IsExecuting) continue;
                        queued.IsExecuting = true;
                        work.Add((kvp.Key, queued));
                    }
                }

                foreach (var item in work)
                {
                    string id = item.id;
                    QueuedCommand queuedCommand = item.command;
                    string commandText = queuedCommand.CommandJson;
                    TaskCompletionSource<string> tcs = queuedCommand.Tcs;

                    if (string.IsNullOrWhiteSpace(commandText))
                    {
                        var emptyResponse = new
                        {
                            status = "error",
                            error = "Empty command received",
                        };
                        tcs.SetResult(JsonConvert.SerializeObject(emptyResponse));
                        lock (lockObj) { commandQueue.Remove(id); }
                        continue;
                    }

                    commandText = commandText.Trim();
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

                    ExecuteQueuedCommand(id, commandText, tcs);
                }
            }
            finally
            {
                Interlocked.Exchange(ref processingCommands, 0);
            }
        }

        private static void ExecuteQueuedCommand(string commandId, string payload, TaskCompletionSource<string> completionSource)
        {
            async void Runner()
            {
                try
                {
                    using var cts = new CancellationTokenSource(FrameIOTimeoutMs);
                    string response = await TransportCommandDispatcher.ExecuteCommandJsonAsync(payload, cts.Token).ConfigureAwait(true);
                    completionSource.TrySetResult(response);
                }
                catch (OperationCanceledException)
                {
                    var timeoutResponse = new
                    {
                        status = "error",
                        error = $"Command processing timed out after {FrameIOTimeoutMs} ms",
                    };
                    completionSource.TrySetResult(JsonConvert.SerializeObject(timeoutResponse));
                }
                catch (Exception ex)
                {
                    McpLog.Error($"Error processing command: {ex.Message}\n{ex.StackTrace}");
                    var response = new
                    {
                        status = "error",
                        error = ex.Message,
                        receivedText = payload?.Length > 50
                            ? payload[..50] + "..."
                            : payload,
                    };
                    completionSource.TrySetResult(JsonConvert.SerializeObject(response));
                }
                finally
                {
                    lock (lockObj)
                    {
                        commandQueue.Remove(commandId);
                    }
                }
            }

            Runner();
        }

        private static object InvokeOnMainThreadWithTimeout(Func<object> func, int timeoutMs)
        {
            if (func == null) return null;
            try
            {
                if (mainThreadId == 0)
                {
                    try { return func(); }
                    catch (Exception ex) { throw new InvalidOperationException($"Main thread handler error: {ex.Message}", ex); }
                }
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

                bool completed = tcs.Task.Wait(timeoutMs);
                if (!completed)
                {
                    return null;
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

        private static bool IsValidJson(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            text = text.Trim();
            if (
                (text.StartsWith("{") && text.EndsWith("}"))
                ||
                (text.StartsWith("[") && text.EndsWith("]"))
            )
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


        public static void WriteHeartbeat(bool reloading, string reason = null)
        {
            try
            {
                string dir = Environment.GetEnvironmentVariable("UNITY_MCP_STATUS_DIR");
                if (string.IsNullOrWhiteSpace(dir))
                {
                    dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".unity-mcp");
                }
                Directory.CreateDirectory(dir);
                string filePath = Path.Combine(dir, $"unity-mcp-status-{ComputeProjectHash(Application.dataPath)}.json");

                string projectName = "Unknown";
                try
                {
                    string projectPath = Application.dataPath;
                    if (!string.IsNullOrEmpty(projectPath))
                    {
                        projectPath = projectPath.TrimEnd('/', '\\');
                        if (projectPath.EndsWith("Assets", StringComparison.OrdinalIgnoreCase))
                        {
                            projectPath = projectPath.Substring(0, projectPath.Length - 6).TrimEnd('/', '\\');
                        }
                        projectName = Path.GetFileName(projectPath);
                        if (string.IsNullOrEmpty(projectName))
                        {
                            projectName = "Unknown";
                        }
                    }
                }
                catch { }

                var payload = new
                {
                    unity_port = currentUnityPort,
                    reloading,
                    reason = reason ?? (reloading ? "reloading" : "ready"),
                    seq = heartbeatSeq,
                    project_path = Application.dataPath,
                    project_name = projectName,
                    unity_version = Application.unityVersion,
                    last_heartbeat = DateTime.UtcNow.ToString("O")
                };
                File.WriteAllText(filePath, JsonConvert.SerializeObject(payload), new System.Text.UTF8Encoding(false));
            }
            catch (Exception)
            {
            }
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
