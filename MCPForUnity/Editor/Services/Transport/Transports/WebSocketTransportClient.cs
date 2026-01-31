using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MCPForUnity.Editor.Constants;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Services;
using MCPForUnity.Editor.Services.Transport;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace MCPForUnity.Editor.Services.Transport.Transports
{
    /// <summary>
    /// Maintains a persistent WebSocket connection to the MCP server plugin hub.
    /// Handles registration, keep-alives, and command dispatch back into Unity via
    /// <see cref="TransportCommandDispatcher"/>.
    /// </summary>
    public class WebSocketTransportClient : IMcpTransportClient, IDisposable
    {
        private const string TransportDisplayName = "websocket";
        private static readonly TimeSpan[] ReconnectSchedule =
        {
            TimeSpan.Zero,
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(3),
            TimeSpan.FromSeconds(5),
            TimeSpan.FromSeconds(10),
            TimeSpan.FromSeconds(30)
        };

        private static readonly TimeSpan DefaultKeepAliveInterval = TimeSpan.FromSeconds(15);
        private static readonly TimeSpan DefaultCommandTimeout = TimeSpan.FromSeconds(30);

        private readonly IToolDiscoveryService _toolDiscoveryService;
        private ClientWebSocket _socket;
        private CancellationTokenSource _lifecycleCts;
        private CancellationTokenSource _connectionCts;
        private Task _receiveTask;
        private Task _keepAliveTask;
        private readonly SemaphoreSlim _sendLock = new(1, 1);

        private Uri _endpointUri;
        private string _sessionId;
        private string _projectHash;
        private string _projectName;
        private string _projectPath;
        private string _unityVersion;
        private TimeSpan _keepAliveInterval = DefaultKeepAliveInterval;
        private TimeSpan _socketKeepAliveInterval = DefaultKeepAliveInterval;
        private volatile bool _isConnected;
        private int _isReconnectingFlag;
        private TransportState _state = TransportState.Disconnected(TransportDisplayName, "Transport not started");
        private string _apiKey;
        private bool _disposed;

        public WebSocketTransportClient(IToolDiscoveryService toolDiscoveryService = null)
        {
            _toolDiscoveryService = toolDiscoveryService;
        }

        public bool IsConnected => _isConnected;
        public string TransportName => TransportDisplayName;
        public TransportState State => _state;

        private Task<List<ToolMetadata>> GetEnabledToolsOnMainThreadAsync(CancellationToken token)
        {
            return TransportCommandDispatcher.RunOnMainThreadAsync(
                () => _toolDiscoveryService?.GetEnabledTools() ?? new List<ToolMetadata>(),
                token);
        }

        public async Task<bool> StartAsync()
        {
            // Capture identity values on the main thread before any async context switching
            _projectName = ProjectIdentityUtility.GetProjectName();
            _projectHash = ProjectIdentityUtility.GetProjectHash();
            _unityVersion = Application.unityVersion;
            _apiKey = HttpEndpointUtility.IsRemoteScope()
                ? EditorPrefs.GetString(EditorPrefKeys.ApiKey, string.Empty)
                : string.Empty;

            // Get project root path (strip /Assets from dataPath) for focus nudging
            string dataPath = Application.dataPath;
            if (!string.IsNullOrEmpty(dataPath))
            {
                string normalized = dataPath.TrimEnd('/', '\\');
                if (string.Equals(System.IO.Path.GetFileName(normalized), "Assets", StringComparison.Ordinal))
                {
                    _projectPath = System.IO.Path.GetDirectoryName(normalized) ?? normalized;
                }
                else
                {
                    _projectPath = normalized;  // Fallback if path doesn't end with Assets
                }
            }

            await StopAsync();

            _lifecycleCts = new CancellationTokenSource();
            _endpointUri = BuildWebSocketUri(HttpEndpointUtility.GetBaseUrl());
            _sessionId = null;

            if (!await EstablishConnectionAsync(_lifecycleCts.Token))
            {
                await StopAsync();
                return false;
            }

            // State is connected but session ID might be pending until 'registered' message
            _state = TransportState.Connected(TransportDisplayName, sessionId: "pending", details: _endpointUri.ToString());
            _isConnected = true;
            return true;
        }

        public async Task StopAsync()
        {
            if (_lifecycleCts == null)
            {
                return;
            }

            try
            {
                _lifecycleCts.Cancel();
            }
            catch { }

            await StopConnectionLoopsAsync().ConfigureAwait(false);

            if (_socket != null)
            {
                try
                {
                    if (_socket.State == WebSocketState.Open || _socket.State == WebSocketState.CloseReceived)
                    {
                        await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Shutdown", CancellationToken.None).ConfigureAwait(false);
                    }
                }
                catch { }
                finally
                {
                    _socket.Dispose();
                    _socket = null;
                }
            }

            _isConnected = false;
            _state = TransportState.Disconnected(TransportDisplayName);

            _lifecycleCts.Dispose();
            _lifecycleCts = null;
        }

        public async Task<bool> VerifyAsync()
        {
            if (_socket == null || _socket.State != WebSocketState.Open)
            {
                return false;
            }

            if (_lifecycleCts == null)
            {
                return false;
            }

            try
            {
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(_lifecycleCts.Token);
                timeoutCts.CancelAfter(TimeSpan.FromSeconds(5));
                await SendPongAsync(timeoutCts.Token).ConfigureAwait(false);
                return true;
            }
            catch (Exception ex)
            {
                McpLog.Warn($"[WebSocket] Verify ping failed: {ex.Message}");
                return false;
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            try
            {
                // Ensure background loops are stopped before disposing shared resources
                StopAsync().GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                McpLog.Warn($"[WebSocket] Dispose failed to stop cleanly: {ex.Message}");
            }

            _sendLock?.Dispose();
            _socket?.Dispose();
            _lifecycleCts?.Dispose();
            _disposed = true;
        }

        private async Task<bool> EstablishConnectionAsync(CancellationToken token)
        {
            await StopConnectionLoopsAsync().ConfigureAwait(false);

            _connectionCts?.Dispose();
            _connectionCts = CancellationTokenSource.CreateLinkedTokenSource(token);
            CancellationToken connectionToken = _connectionCts.Token;

            _socket?.Dispose();
            _socket = new ClientWebSocket();
            _socket.Options.KeepAliveInterval = _socketKeepAliveInterval;

            // Add API key header if configured (for remote-hosted mode)
            if (!string.IsNullOrEmpty(_apiKey))
            {
                _socket.Options.SetRequestHeader(AuthConstants.ApiKeyHeader, _apiKey);
            }

            try
            {
                await _socket.ConnectAsync(_endpointUri, connectionToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                string errorMsg = "Connection failed. Check that the server URL is correct, the server is running, and your API key (if required) is valid.";
                McpLog.Error($"[WebSocket] {errorMsg} (Detail: {ex.Message})");
                _state = TransportState.Disconnected(TransportDisplayName, errorMsg);
                return false;
            }

            StartBackgroundLoops(connectionToken);

            try
            {
                await SendRegisterAsync(connectionToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                string regMsg = $"Registration with server failed: {ex.Message}";
                McpLog.Error($"[WebSocket] {regMsg}");
                _state = TransportState.Disconnected(TransportDisplayName, regMsg);
                return false;
            }

            return true;
        }

        /// <summary>
        /// Stops the connection loops and disposes of the connection CTS.
        /// Particularly useful when reconnecting, we want to ensure that background loops are cancelled correctly before starting new oens
        /// </summary>
        /// <param name="awaitTasks">Whether to await the receive and keep alive tasks before disposing.</param>
        private async Task StopConnectionLoopsAsync(bool awaitTasks = true)
        {
            if (_connectionCts != null && !_connectionCts.IsCancellationRequested)
            {
                try { _connectionCts.Cancel(); } catch { }
            }

            if (_receiveTask != null)
            {
                if (awaitTasks)
                {
                    try { await _receiveTask.ConfigureAwait(false); } catch { }
                    _receiveTask = null;
                }
                else if (_receiveTask.IsCompleted)
                {
                    _receiveTask = null;
                }
            }

            if (_keepAliveTask != null)
            {
                if (awaitTasks)
                {
                    try { await _keepAliveTask.ConfigureAwait(false); } catch { }
                    _keepAliveTask = null;
                }
                else if (_keepAliveTask.IsCompleted)
                {
                    _keepAliveTask = null;
                }
            }

            if (_connectionCts != null)
            {
                _connectionCts.Dispose();
                _connectionCts = null;
            }
        }

        private void StartBackgroundLoops(CancellationToken token)
        {
            if ((_receiveTask != null && !_receiveTask.IsCompleted) || (_keepAliveTask != null && !_keepAliveTask.IsCompleted))
            {
                return;
            }

            _receiveTask = Task.Run(() => ReceiveLoopAsync(token), CancellationToken.None);
            _keepAliveTask = Task.Run(() => KeepAliveLoopAsync(token), CancellationToken.None);
        }

        private async Task ReceiveLoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    string message = await ReceiveMessageAsync(token).ConfigureAwait(false);
                    if (message == null)
                    {
                        continue;
                    }
                    await HandleMessageAsync(message, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (WebSocketException wse)
                {
                    McpLog.Warn($"[WebSocket] Receive loop error: {wse.Message}");
                    await HandleSocketClosureAsync(wse.Message).ConfigureAwait(false);
                    break;
                }
                catch (Exception ex)
                {
                    McpLog.Warn($"[WebSocket] Unexpected receive error: {ex.Message}");
                    await HandleSocketClosureAsync(ex.Message).ConfigureAwait(false);
                    break;
                }
            }
        }

        private async Task<string> ReceiveMessageAsync(CancellationToken token)
        {
            if (_socket == null)
            {
                return null;
            }

            byte[] rentedBuffer = System.Buffers.ArrayPool<byte>.Shared.Rent(8192);
            var buffer = new ArraySegment<byte>(rentedBuffer);
            using var ms = new MemoryStream(8192);

            try
            {
                while (!token.IsCancellationRequested)
                {
                    WebSocketReceiveResult result = await _socket.ReceiveAsync(buffer, token).ConfigureAwait(false);

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        await HandleSocketClosureAsync(result.CloseStatusDescription ?? "Server closed connection").ConfigureAwait(false);
                        return null;
                    }

                    if (result.Count > 0)
                    {
                        ms.Write(buffer.Array!, buffer.Offset, result.Count);
                    }

                    if (result.EndOfMessage)
                    {
                        break;
                    }
                }

                if (ms.Length == 0)
                {
                    return null;
                }

                return Encoding.UTF8.GetString(ms.ToArray());
            }
            finally
            {
                System.Buffers.ArrayPool<byte>.Shared.Return(rentedBuffer);
            }
        }

        private async Task HandleMessageAsync(string message, CancellationToken token)
        {
            JObject payload;
            try
            {
                payload = JObject.Parse(message);
            }
            catch (Exception ex)
            {
                McpLog.Warn($"[WebSocket] Invalid JSON payload: {ex.Message}");
                return;
            }

            string messageType = payload.Value<string>("type") ?? string.Empty;

            switch (messageType)
            {
                case "welcome":
                    ApplyWelcome(payload);
                    break;
                case "registered":
                    await HandleRegisteredAsync(payload, token).ConfigureAwait(false);
                    break;
                case "execute":
                    await HandleExecuteAsync(payload, token).ConfigureAwait(false);
                    break;
                case "ping":
                    await SendPongAsync(token).ConfigureAwait(false);
                    break;
                default:
                    // No-op for unrecognised types (keep-alives, telemetry, etc.)
                    break;
            }
        }

        private void ApplyWelcome(JObject payload)
        {
            int? keepAliveSeconds = payload.Value<int?>("keepAliveInterval");
            if (keepAliveSeconds.HasValue && keepAliveSeconds.Value > 0)
            {
                _keepAliveInterval = TimeSpan.FromSeconds(keepAliveSeconds.Value);
                _socketKeepAliveInterval = _keepAliveInterval;
            }

            int? serverTimeoutSeconds = payload.Value<int?>("serverTimeout");
            if (serverTimeoutSeconds.HasValue)
            {
                int sourceSeconds = keepAliveSeconds ?? serverTimeoutSeconds.Value;
                int safeSeconds = Math.Max(5, Math.Min(serverTimeoutSeconds.Value, sourceSeconds));
                _socketKeepAliveInterval = TimeSpan.FromSeconds(safeSeconds);
            }
        }

        private async Task HandleRegisteredAsync(JObject payload, CancellationToken token)
        {
            string newSessionId = payload.Value<string>("session_id");
            if (!string.IsNullOrEmpty(newSessionId))
            {
                _sessionId = newSessionId;
                ProjectIdentityUtility.SetSessionId(_sessionId);
                _state = TransportState.Connected(TransportDisplayName, sessionId: _sessionId, details: _endpointUri.ToString());
                McpLog.Info($"[WebSocket] Registered with session ID: {_sessionId}", false);

                await SendRegisterToolsAsync(token).ConfigureAwait(false);
            }
        }

        private async Task SendRegisterToolsAsync(CancellationToken token)
        {
            if (_toolDiscoveryService == null) return;

            token.ThrowIfCancellationRequested();
            var tools = await GetEnabledToolsOnMainThreadAsync(token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            McpLog.Info($"[WebSocket] Preparing to register {tools.Count} tool(s) with the bridge.", false);
            var toolsArray = new JArray();

            foreach (var tool in tools)
            {
                var toolObj = new JObject
                {
                    ["name"] = tool.Name,
                    ["description"] = tool.Description,
                    ["structured_output"] = tool.StructuredOutput,
                    ["requires_polling"] = tool.RequiresPolling,
                    ["poll_action"] = tool.PollAction
                };

                var paramsArray = new JArray();
                if (tool.Parameters != null)
                {
                    foreach (var p in tool.Parameters)
                    {
                        paramsArray.Add(new JObject
                        {
                            ["name"] = p.Name,
                            ["description"] = p.Description,
                            ["type"] = p.Type,
                            ["required"] = p.Required,
                            ["default_value"] = p.DefaultValue
                        });
                    }
                }
                toolObj["parameters"] = paramsArray;
                toolsArray.Add(toolObj);
            }

            var payload = new JObject
            {
                ["type"] = "register_tools",
                ["tools"] = toolsArray
            };

            await SendJsonAsync(payload, token).ConfigureAwait(false);
            McpLog.Info($"[WebSocket] Sent {tools.Count} tools registration", false);
        }

        private async Task HandleExecuteAsync(JObject payload, CancellationToken token)
        {
            string commandId = payload.Value<string>("id");
            string commandName = payload.Value<string>("name");
            JObject parameters = payload.Value<JObject>("params") ?? new JObject();
            int timeoutSeconds = payload.Value<int?>("timeout") ?? (int)DefaultCommandTimeout.TotalSeconds;

            if (string.IsNullOrEmpty(commandId) || string.IsNullOrEmpty(commandName))
            {
                McpLog.Warn("[WebSocket] Invalid execute payload (missing id or name)");
                return;
            }

            var commandEnvelope = new JObject
            {
                ["type"] = commandName,
                ["params"] = parameters
            };

            string responseJson;
            try
            {
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(token);
                timeoutCts.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, timeoutSeconds)));
                responseJson = await TransportCommandDispatcher.ExecuteCommandJsonAsync(commandEnvelope.ToString(Formatting.None), timeoutCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                responseJson = JsonConvert.SerializeObject(new
                {
                    status = "error",
                    error = $"Command '{commandName}' timed out after {timeoutSeconds} seconds"
                });
            }
            catch (Exception ex)
            {
                responseJson = JsonConvert.SerializeObject(new
                {
                    status = "error",
                    error = ex.Message
                });
            }

            JToken resultToken;
            try
            {
                resultToken = JToken.Parse(responseJson);
            }
            catch
            {
                resultToken = new JObject
                {
                    ["status"] = "error",
                    ["error"] = "Invalid response payload"
                };
            }

            var responsePayload = new JObject
            {
                ["type"] = "command_result",
                ["id"] = commandId,
                ["result"] = resultToken
            };

            await SendJsonAsync(responsePayload, token).ConfigureAwait(false);
        }

        private async Task KeepAliveLoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(_keepAliveInterval, token).ConfigureAwait(false);
                    if (_socket == null || _socket.State != WebSocketState.Open)
                    {
                        break;
                    }
                    await SendPongAsync(token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    McpLog.Warn($"[WebSocket] Keep-alive failed: {ex.Message}");
                    await HandleSocketClosureAsync(ex.Message).ConfigureAwait(false);
                    break;
                }
            }
        }

        private async Task SendRegisterAsync(CancellationToken token)
        {
            var registerPayload = new JObject
            {
                ["type"] = "register",
                // session_id is now server-authoritative; omitted here or sent as null
                ["project_name"] = _projectName,
                ["project_hash"] = _projectHash,
                ["unity_version"] = _unityVersion,
                ["project_path"] = _projectPath
            };

            await SendJsonAsync(registerPayload, token).ConfigureAwait(false);
        }

        private Task SendPongAsync(CancellationToken token)
        {
            var payload = new JObject
            {
                ["type"] = "pong",
                ["session_id"] = _sessionId  // Include session ID for server-side tracking
            };
            return SendJsonAsync(payload, token);
        }

        private async Task SendJsonAsync(JObject payload, CancellationToken token)
        {
            if (_socket == null)
            {
                throw new InvalidOperationException("WebSocket is not initialised");
            }

            string json = payload.ToString(Formatting.None);
            byte[] bytes = Encoding.UTF8.GetBytes(json);
            var buffer = new ArraySegment<byte>(bytes);

            await _sendLock.WaitAsync(token).ConfigureAwait(false);
            try
            {
                if (_socket.State != WebSocketState.Open)
                {
                    throw new InvalidOperationException("WebSocket is not open");
                }

                await _socket.SendAsync(buffer, WebSocketMessageType.Text, true, token).ConfigureAwait(false);
            }
            finally
            {
                _sendLock.Release();
            }
        }

        private async Task HandleSocketClosureAsync(string reason)
        {
            // Capture stack trace for debugging disconnection triggers
            var stackTrace = new System.Diagnostics.StackTrace(true);
            McpLog.Debug($"[WebSocket] HandleSocketClosureAsync called. Reason: {reason}\nStack trace:\n{stackTrace}");

            if (_lifecycleCts == null || _lifecycleCts.IsCancellationRequested)
            {
                return;
            }

            if (Interlocked.CompareExchange(ref _isReconnectingFlag, 1, 0) != 0)
            {
                return;
            }

            _isConnected = false;
            _state = _state.WithError(reason ?? "Connection closed");
            McpLog.Warn($"[WebSocket] Connection closed: {reason}");

            await StopConnectionLoopsAsync(awaitTasks: false).ConfigureAwait(false);

            _ = Task.Run(() => AttemptReconnectAsync(_lifecycleCts.Token), CancellationToken.None);
        }

        private async Task AttemptReconnectAsync(CancellationToken token)
        {
            try
            {
                await StopConnectionLoopsAsync().ConfigureAwait(false);

                foreach (TimeSpan delay in ReconnectSchedule)
                {
                    if (token.IsCancellationRequested)
                    {
                        return;
                    }

                    if (delay > TimeSpan.Zero)
                    {
                        try { await Task.Delay(delay, token).ConfigureAwait(false); }
                        catch (OperationCanceledException) { return; }
                    }

                    if (await EstablishConnectionAsync(token).ConfigureAwait(false))
                    {
                        _state = TransportState.Connected(TransportDisplayName, sessionId: _sessionId, details: _endpointUri.ToString());
                        _isConnected = true;
                        McpLog.Info("[WebSocket] Reconnected to MCP server", false);
                        return;
                    }
                }
            }
            finally
            {
                Interlocked.Exchange(ref _isReconnectingFlag, 0);
            }

            _state = TransportState.Disconnected(TransportDisplayName, "Failed to reconnect");
        }

        private static Uri BuildWebSocketUri(string baseUrl)
        {
            if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var httpUri))
            {
                throw new InvalidOperationException($"Invalid MCP base URL: {baseUrl}");
            }

            // Replace bind-only addresses with localhost for client connections
            // 0.0.0.0 and :: are only valid for server binding, not client connections
            string host = httpUri.Host;
            if (host == "0.0.0.0" || host == "::")
            {
                McpLog.Warn($"[WebSocket] Base URL host '{host}' is bind-only; using 'localhost' for client connection.");
                host = "localhost";
            }

            var builder = new UriBuilder(httpUri)
            {
                Scheme = httpUri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase) ? "wss" : "ws",
                Host = host,
                Path = httpUri.AbsolutePath.TrimEnd('/') + "/hub/plugin"
            };

            return builder.Uri;
        }
    }
}
