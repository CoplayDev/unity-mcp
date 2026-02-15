using System;
using System.Threading.Tasks;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Services.Transport.Transports;

namespace MCPForUnity.Editor.Services.Transport
{
    /// <summary>
    /// Coordinates the active transport client and exposes lifecycle helpers.
    /// </summary>
    public class TransportManager
    {
        private IMcpTransportClient _httpClient;
        private IMcpTransportClient _stdioClient;
        private TransportState _httpState = TransportState.Disconnected("http");
        private TransportState _stdioState = TransportState.Disconnected("stdio");
        private Func<IMcpTransportClient> _webSocketFactory;
        private Func<IMcpTransportClient> _stdioFactory;

        public TransportManager()
        {
            Configure(
                () => new WebSocketTransportClient(MCPServiceLocator.ToolDiscovery),
                () => new StdioTransportClient());
        }

        public void Configure(
            Func<IMcpTransportClient> webSocketFactory,
            Func<IMcpTransportClient> stdioFactory)
        {
            _webSocketFactory = webSocketFactory ?? throw new ArgumentNullException(nameof(webSocketFactory));
            _stdioFactory = stdioFactory ?? throw new ArgumentNullException(nameof(stdioFactory));
        }

        private IMcpTransportClient GetOrCreateClient(TransportMode mode)
        {
            return mode switch
            {
                TransportMode.Http => _httpClient ??= _webSocketFactory(),
                TransportMode.Stdio => _stdioClient ??= _stdioFactory(),
                _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unsupported transport mode"),
            };
        }

        public async Task<bool> StartAsync(TransportMode mode)
        {
            IMcpTransportClient client = GetOrCreateClient(mode);

            bool started = await client.StartAsync();
            if (!started)
            {
                try
                {
                    await client.StopAsync();
                }
                catch (Exception ex)
                {
                    McpLog.Warn($"Error while stopping transport {client.TransportName}: {ex.Message}");
                }
                UpdateState(mode, TransportState.Disconnected(client.TransportName, client.State?.Error ?? "Failed to start"));
                return false;
            }

            UpdateState(mode, client.State ?? TransportState.Connected(client.TransportName));
            return true;
        }

        public async Task StopAsync(TransportMode? mode = null)
        {
            async Task StopClient(IMcpTransportClient client, TransportMode clientMode)
            {
                if (client == null) return;
                try { await client.StopAsync(); }
                catch (Exception ex) { McpLog.Warn($"Error while stopping transport {client.TransportName}: {ex.Message}"); }
                finally { UpdateState(clientMode, TransportState.Disconnected(client.TransportName)); }
            }

            if (mode == null)
            {
                await StopClient(_httpClient, TransportMode.Http);
                await StopClient(_stdioClient, TransportMode.Stdio);
                return;
            }

            if (mode == TransportMode.Http)
            {
                await StopClient(_httpClient, TransportMode.Http);
            }
            else
            {
                await StopClient(_stdioClient, TransportMode.Stdio);
            }
        }

        public async Task<bool> VerifyAsync(TransportMode mode)
        {
            IMcpTransportClient client = GetClient(mode);
            if (client == null)
            {
                return false;
            }

            bool ok = await client.VerifyAsync();
            var state = client.State ?? TransportState.Disconnected(client.TransportName, "No state reported");
            UpdateState(mode, state);
            return ok;
        }

        public TransportState GetState(TransportMode mode)
        {
            return mode switch
            {
                TransportMode.Http => _httpState,
                TransportMode.Stdio => _stdioState,
                _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unsupported transport mode"),
            };
        }

        public bool IsRunning(TransportMode mode) => GetState(mode).IsConnected;

        /// <summary>
        /// Synchronous teardown for shutdown/reload hooks where async awaits are not possible.
        /// </summary>
        public void ForceStop(TransportMode mode)
        {
            IMcpTransportClient client = GetClient(mode);
            string transportName = client?.TransportName ?? mode.ToString().ToLowerInvariant();

            if (client == null)
            {
                UpdateState(mode, TransportState.Disconnected(transportName));
                return;
            }

            try
            {
                if (client is WebSocketTransportClient wsClient)
                {
                    wsClient.ForceStop();
                }
                else
                {
                    client.StopAsync().GetAwaiter().GetResult();
                }
            }
            catch (Exception ex)
            {
                McpLog.Warn($"Error while force-stopping transport {transportName}: {ex.Message}");
            }
            finally
            {
                UpdateState(mode, TransportState.Disconnected(transportName));
            }
        }

        /// <summary>
        /// Gets the active transport client for the specified mode.
        /// Returns null if the client hasn't been created yet.
        /// </summary>
        public IMcpTransportClient GetClient(TransportMode mode)
        {
            return mode switch
            {
                TransportMode.Http => _httpClient,
                TransportMode.Stdio => _stdioClient,
                _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unsupported transport mode"),
            };
        }

        private void UpdateState(TransportMode mode, TransportState state)
        {
            switch (mode)
            {
                case TransportMode.Http:
                    _httpState = state;
                    break;
                case TransportMode.Stdio:
                    _stdioState = state;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unsupported transport mode");
            }
        }
    }

    public enum TransportMode
    {
        Http,
        Stdio
    }
}
