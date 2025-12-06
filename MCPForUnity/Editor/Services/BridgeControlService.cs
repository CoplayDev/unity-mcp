
using System;
using System.Threading.Tasks;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Services.Transport;
using MCPForUnity.Editor.Services.Transport.Transports;

namespace MCPForUnity.Editor.Services
{
    /// <summary>
    /// Bridges the editor UI to the stdio transport.
    /// </summary>
    public class BridgeControlService : IBridgeControlService
    {
        private readonly TransportManager _transportManager;

        public BridgeControlService()
        {
            _transportManager = MCPServiceLocator.TransportManager;
        }

        private static BridgeVerificationResult BuildVerificationResult(TransportState state, bool pingSucceeded, string messageOverride = null, bool? handshakeOverride = null)
        {
            bool handshakeValid = handshakeOverride ?? state.IsConnected;
            string transportLabel = string.IsNullOrWhiteSpace(state.TransportName)
                ? "stdio"
                : state.TransportName;
            string detailSuffix = string.IsNullOrWhiteSpace(state.Details) ? string.Empty : $" [{state.Details}]";
            string message = messageOverride
                ?? state.Error
                ?? (state.IsConnected ? $"Transport '{transportLabel}' connected{detailSuffix}" : $"Transport '{transportLabel}' disconnected{detailSuffix}");

            return new BridgeVerificationResult
            {
                Success = pingSucceeded && handshakeValid,
                HandshakeValid = handshakeValid,
                PingSucceeded = pingSucceeded,
                Message = message
            };
        }

        public bool IsRunning
        {
            get
            {
                return _transportManager.IsRunning();
            }
        }

        public int CurrentPort
        {
            get
            {
                var state = _transportManager.GetState();
                if (state.Port.HasValue)
                {
                    return state.Port.Value;
                }

                return StdioBridgeHost.GetCurrentPort();
            }
        }

        public bool IsAutoConnectMode => StdioBridgeHost.IsAutoConnectMode();

        public async Task<bool> StartAsync()
        {
            try
            {
                bool started = await _transportManager.StartAsync();
                if (!started)
                {
                    McpLog.Warn("Failed to start MCP stdio transport");
                }
                return started;
            }
            catch (Exception ex)
            {
                McpLog.Error($"Error starting MCP stdio transport: {ex.Message}");
                return false;
            }
        }

        public async Task StopAsync()
        {
            try
            {
                await _transportManager.StopAsync();
            }
            catch (Exception ex)
            {
                McpLog.Warn($"Error stopping MCP transport: {ex.Message}");
            }
        }

        public async Task<BridgeVerificationResult> VerifyAsync()
        {
            bool pingSucceeded = await _transportManager.VerifyAsync();
            var state = _transportManager.GetState();
            return BuildVerificationResult(state, pingSucceeded);
        }

        public BridgeVerificationResult Verify(int port)
        {
            bool pingSucceeded = _transportManager.VerifyAsync().GetAwaiter().GetResult();
            var state = _transportManager.GetState();

            bool handshakeValid = state.IsConnected && port == CurrentPort;
            string message = handshakeValid
                ? $"STDIO transport listening on port {CurrentPort}"
                : $"STDIO transport port mismatch (expected {CurrentPort}, got {port})";
            return BuildVerificationResult(state, pingSucceeded && handshakeValid, message, handshakeValid);
        }

    }
}
