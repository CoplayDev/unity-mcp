using System;
using System.Threading.Tasks;
using MCPForUnity.Editor.Helpers;

namespace MCPForUnity.Editor.Services.Transport.Transports
{
    /// <summary>
    /// Adapts the existing TCP bridge into the transport abstraction.
    /// </summary>
    public class StdioTransportClient : IMcpTransportClient
    {
        private TransportState _state = TransportState.Disconnected("stdio");

        public bool IsConnected => StdioBridgeHost.IsRunning;
        public string TransportName => "stdio";
        public TransportState State => _state;

        public Task<bool> StartAsync()
        {
            try
            {
                StdioBridgeHost.StartAutoConnect();
                _state = TransportState.Connected("stdio", port: StdioBridgeHost.GetCurrentPort());
                return Task.FromResult(true);
            }
            catch (Exception ex)
            {
                _state = TransportState.Disconnected("stdio", ex.Message);
                return Task.FromResult(false);
            }
        }

        public Task StopAsync()
        {
            StdioBridgeHost.Stop();
            _state = TransportState.Disconnected("stdio");
            return Task.CompletedTask;
        }

        public Task<bool> VerifyAsync()
        {
            bool running = StdioBridgeHost.IsRunning;
            _state = running
                ? TransportState.Connected("stdio", port: StdioBridgeHost.GetCurrentPort())
                : TransportState.Disconnected("stdio", "Bridge not running");
            return Task.FromResult(running);
        }

    }
}
