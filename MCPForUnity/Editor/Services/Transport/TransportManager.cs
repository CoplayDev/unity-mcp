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
        private IMcpTransportClient _active;
        private TransportMode? _activeMode;
        private Func<IMcpTransportClient> _webSocketFactory;
        private Func<IMcpTransportClient> _stdioFactory;

        public TransportManager()
        {
            Configure(
                () => new WebSocketTransportClient(MCPServiceLocator.ToolDiscovery),
                () => new StdioTransportClient());
        }

        public IMcpTransportClient ActiveTransport => _active;
        public TransportMode? ActiveMode => _activeMode;

        public void Configure(
            Func<IMcpTransportClient> webSocketFactory,
            Func<IMcpTransportClient> stdioFactory)
        {
            _webSocketFactory = webSocketFactory ?? throw new ArgumentNullException(nameof(webSocketFactory));
            _stdioFactory = stdioFactory ?? throw new ArgumentNullException(nameof(stdioFactory));
        }

        public async Task<bool> StartAsync(TransportMode mode)
        {
            await StopAsync();

            IMcpTransportClient next = mode switch
            {
                TransportMode.Stdio => _stdioFactory(),
                TransportMode.Http => _webSocketFactory(),
                _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unsupported transport mode")
            } ?? throw new InvalidOperationException($"Factory returned null for transport mode {mode}");

            bool started = await next.StartAsync();
            if (!started)
            {
                await next.StopAsync();
                _active = null;
                _activeMode = null;
                return false;
            }

            _active = next;
            _activeMode = mode;
            return true;
        }

        public async Task StopAsync()
        {
            if (_active != null)
            {
                try
                {
                    await _active.StopAsync();
                }
                catch (Exception ex)
                {
                    McpLog.Warn($"Error while stopping transport {_active.TransportName}: {ex.Message}");
                }
                finally
                {
                    _active = null;
                    _activeMode = null;
                }
            }
        }

        public async Task<bool> VerifyAsync()
        {
            if (_active == null)
            {
                return false;
            }
            return await _active.VerifyAsync();
        }

        public TransportState GetState()
        {
            if (_active == null)
            {
                return TransportState.Disconnected(_activeMode?.ToString()?.ToLowerInvariant() ?? "unknown", "Transport not started");
            }

            return _active.State ?? TransportState.Disconnected(_active.TransportName, "No state reported");
        }
    }

    public enum TransportMode
    {
        Http,
        Stdio
    }
}
