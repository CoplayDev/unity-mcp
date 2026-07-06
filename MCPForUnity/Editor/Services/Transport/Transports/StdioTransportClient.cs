using System;
using System.Threading.Tasks;
using MCPForUnity.Editor.Helpers;
using UnityEditor;

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

        // Bounded window to wait for the bridge to actually bind after StartAutoConnect. Covers the
        // OS port-release delay after a domain reload (the same port can stay held for a few hundred
        // ms, longer on Windows/macOS), during which Start() defers binding to an editor-idle retry
        // or falls back to a new port once BusyPortFallbackWindowSeconds elapses.
        internal const double ReadyWaitTimeoutSeconds = 5.0;
        private const int ReadyPollIntervalMs = 100;

        // Pure predicate (unit-testable): keep polling while the bridge is not yet ready and the
        // bounded window has not elapsed.
        internal static bool ShouldKeepWaitingForReady(bool bridgeReady, double secondsWaited)
            => !bridgeReady && secondsWaited < ReadyWaitTimeoutSeconds;

        public async Task<bool> StartAsync()
        {
            try
            {
                StdioBridgeHost.StartAutoConnect();

                // StartAutoConnect triggers the bind, but when the previous port is still held after a
                // domain reload it defers binding to an editor-idle retry — so IsRunning can still be
                // false right here. Wait (bounded) for the bridge to actually become ready before
                // reporting success; otherwise callers immediately verify a bool that was never
                // awaited and get a spurious "Bridge not running" (the Start Session race).
                bool ready = await WaitForBridgeReadyAsync();
                _state = ready
                    ? TransportState.Connected("stdio", port: StdioBridgeHost.GetCurrentPort())
                    : TransportState.Disconnected("stdio", "Bridge not ready yet (port still releasing after reload).");
                return ready;
            }
            catch (Exception ex)
            {
                _state = TransportState.Disconnected("stdio", ex.Message);
                return false;
            }
        }

        private static async Task<bool> WaitForBridgeReadyAsync()
        {
            double start = EditorApplication.timeSinceStartup;
            while (ShouldKeepWaitingForReady(StdioBridgeHost.IsRunning, EditorApplication.timeSinceStartup - start))
            {
                await Task.Delay(ReadyPollIntervalMs);
            }
            return StdioBridgeHost.IsRunning;
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

        public Task ReregisterToolsAsync()
        {
            // In stdio mode, Python re-syncs tools automatically on reconnection
            // after domain reload. No proactive push mechanism exists over TCP.
            return Task.CompletedTask;
        }

    }
}
