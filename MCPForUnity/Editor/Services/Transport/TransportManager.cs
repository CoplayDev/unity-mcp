using System;
using System.Threading.Tasks;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Services.Transport.Transports;

namespace MCPForUnity.Editor.Services.Transport
{
    /// <summary>
    /// Coordinates the stdio transport client and exposes lifecycle helpers.
    /// </summary>
    public class TransportManager
    {
        private IMcpTransportClient _stdioClient;
        private TransportState _stdioState = TransportState.Disconnected("stdio");

        public TransportManager()
        {
            _stdioClient = new StdioTransportClient();
        }

        public IMcpTransportClient ActiveTransport => _stdioClient;

        public async Task<bool> StartAsync()
        {
            if (_stdioClient == null)
            {
                _stdioClient = new StdioTransportClient();
            }

            bool started = await _stdioClient.StartAsync();
            if (!started)
            {
                try
                {
                    await _stdioClient.StopAsync();
                }
                catch (Exception ex)
                {
                    McpLog.Warn($"Error while stopping transport {_stdioClient.TransportName}: {ex.Message}");
                }
                _stdioState = TransportState.Disconnected(_stdioClient.TransportName, "Failed to start");
                return false;
            }

            _stdioState = _stdioClient.State ?? TransportState.Connected(_stdioClient.TransportName);
            return true;
        }

        public async Task StopAsync()
        {
            if (_stdioClient == null) return;
            try 
            { 
                await _stdioClient.StopAsync(); 
            }
            catch (Exception ex) 
            { 
                McpLog.Warn($"Error while stopping transport {_stdioClient.TransportName}: {ex.Message}"); 
            }
            finally 
            { 
                _stdioState = TransportState.Disconnected(_stdioClient.TransportName); 
            }
        }

        public async Task<bool> VerifyAsync()
        {
            if (_stdioClient == null)
            {
                return false;
            }

            bool ok = await _stdioClient.VerifyAsync();
            var state = _stdioClient.State ?? TransportState.Disconnected(_stdioClient.TransportName, "No state reported");
            _stdioState = state;
            return ok;
        }

        public TransportState GetState()
        {
            return _stdioState;
        }

        public bool IsRunning() => _stdioState.IsConnected;
    }
}
