
using System;
using System.Threading.Tasks;
using MCPForUnity.Editor.Constants;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Services.Transport;
using MCPForUnity.Editor.Services.Transport.Transports;
using UnityEditor;

namespace MCPForUnity.Editor.Services
{
    /// <summary>
    /// Bridges the editor UI to the active transport (HTTP with WebSocket push, or stdio).
    /// </summary>
    public class BridgeControlService : IBridgeControlService
    {
        private readonly TransportManager _transportManager;
        private TransportMode _preferredMode = TransportMode.Http;

        public BridgeControlService()
        {
            _transportManager = MCPServiceLocator.TransportManager;
        }

        private TransportMode ResolvePreferredMode()
        {
            bool useHttp = EditorConfigurationCache.Instance.UseHttpTransport;
            _preferredMode = useHttp ? TransportMode.Http : TransportMode.Stdio;
            return _preferredMode;
        }

        private static BridgeVerificationResult BuildVerificationResult(TransportState state, TransportMode mode, bool pingSucceeded, string messageOverride = null, bool? handshakeOverride = null)
        {
            bool handshakeValid = handshakeOverride ?? (mode == TransportMode.Stdio ? state.IsConnected : true);
            string transportLabel = string.IsNullOrWhiteSpace(state.TransportName)
                ? mode.ToString().ToLowerInvariant()
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
                var mode = ResolvePreferredMode();
                return _transportManager.IsRunning(mode);
            }
        }

        public int CurrentPort
        {
            get
            {
                var mode = ResolvePreferredMode();
                var state = _transportManager.GetState(mode);
                if (state.Port.HasValue)
                {
                    return state.Port.Value;
                }

                // Legacy fallback while the stdio bridge is still in play
                return StdioBridgeHost.GetCurrentPort();
            }
        }

        public bool IsAutoConnectMode => StdioBridgeHost.IsAutoConnectMode();
        public TransportMode? ActiveMode => ResolvePreferredMode();

        public async Task<bool> StartAsync()
        {
            var mode = ResolvePreferredMode();
            try
            {
                // Treat transports as mutually exclusive for user-driven session starts:
                // stop the *other* transport first to avoid duplicated sessions (e.g. stdio lingering when switching to HTTP).
                var otherMode = mode == TransportMode.Http ? TransportMode.Stdio : TransportMode.Http;
                try
                {
                    await _transportManager.StopAsync(otherMode);
                }
                catch (Exception ex)
                {
                    McpLog.Warn($"Error stopping other transport ({otherMode}) before start: {ex.Message}");
                }

                // Legacy safety: stdio may have been started outside TransportManager state.
                if (otherMode == TransportMode.Stdio)
                {
                    try { StdioBridgeHost.Stop(); } catch { }
                }

                bool started = await _transportManager.StartAsync(mode);
                if (!started)
                {
                    McpLog.Warn($"Failed to start MCP transport: {mode}");
                }
                return started;
            }
            catch (Exception ex)
            {
                McpLog.Error($"Error starting MCP transport {mode}: {ex.Message}");
                return false;
            }
        }

        public async Task StopAsync()
        {
            try
            {
                var mode = ResolvePreferredMode();
                await _transportManager.StopAsync(mode);
            }
            catch (Exception ex)
            {
                McpLog.Warn($"Error stopping MCP transport: {ex.Message}");
            }
        }

        public async Task<BridgeVerificationResult> VerifyAsync()
        {
            var mode = ResolvePreferredMode();
            bool pingSucceeded = await _transportManager.VerifyAsync(mode);
            var state = _transportManager.GetState(mode);
            return BuildVerificationResult(state, mode, pingSucceeded);
        }

        public BridgeVerificationResult Verify(int port)
        {
            var mode = ResolvePreferredMode();
            bool pingSucceeded = _transportManager.VerifyAsync(mode).GetAwaiter().GetResult();
            var state = _transportManager.GetState(mode);

            if (mode == TransportMode.Stdio)
            {
                bool handshakeValid = state.IsConnected && port == CurrentPort;
                string message = handshakeValid
                    ? $"STDIO transport listening on port {CurrentPort}"
                    : $"STDIO transport port mismatch (expected {CurrentPort}, got {port})";
                return BuildVerificationResult(state, mode, pingSucceeded && handshakeValid, message, handshakeValid);
            }

            return BuildVerificationResult(state, mode, pingSucceeded);
        }

    }
}
