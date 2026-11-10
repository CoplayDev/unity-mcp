using System.Threading.Tasks;
using NUnit.Framework;
using MCPForUnity.Editor.Services.Transport;

namespace MCPForUnityTests.Editor.Services
{
    /// <summary>
    /// Pins TransportManager.StartAsync's coalescing contract: concurrent starts for the
    /// same mode share one in-flight attempt instead of racing — a second StartAsync would
    /// otherwise tear down the first connection mid-handshake (manual Connect vs the
    /// reload-resume/auto-start loops).
    /// </summary>
    public class TransportManagerTests
    {
        private sealed class PendingTransportClient : IMcpTransportClient
        {
            public readonly TaskCompletionSource<bool> Pending = new TaskCompletionSource<bool>();
            public int StartCalls;

            public bool IsConnected => false;
            public string TransportName => "http";
            public TransportState State { get; } = TransportState.Disconnected("http");

            public Task<bool> StartAsync()
            {
                StartCalls++;
                return Pending.Task;
            }

            public Task StopAsync() => Task.CompletedTask;
            public Task<bool> VerifyAsync() => Task.FromResult(false);
            public Task ReregisterToolsAsync() => Task.CompletedTask;
        }

        [Test]
        public void StartAsync_ConcurrentCallsSameMode_CoalesceIntoOneAttempt()
        {
            var client = new PendingTransportClient();
            var manager = new TransportManager();
            manager.Configure(() => client, () => client);

            Task<bool> first = manager.StartAsync(TransportMode.Http);
            Task<bool> second = manager.StartAsync(TransportMode.Http);

            Assert.AreEqual(1, client.StartCalls, "concurrent starts must share one client attempt");
            Assert.AreSame(first, second, "the in-flight task is returned to concurrent callers");

            client.Pending.SetResult(true); // let the shared attempt finish
        }

        /// <summary>
        /// Stdio fake whose connectivity is flipped by the test without going through
        /// StartAsync/StopAsync — mirrors StdioBridgeHost binding via its editor-idle
        /// retry after a busy-port domain reload, which bypasses TransportManager.
        /// </summary>
        private sealed class ExternallyControlledStdioClient : IMcpTransportClient
        {
            public bool Connected;
            public int Port = 6400;

            public bool IsConnected => Connected;
            public string TransportName => "stdio";
            public TransportState State => Connected
                ? TransportState.Connected("stdio", port: Port)
                : TransportState.Disconnected("stdio", "Bridge not running");

            public Task<bool> StartAsync()
            {
                Connected = true;
                return Task.FromResult(true);
            }

            public Task StopAsync()
            {
                Connected = false;
                return Task.CompletedTask;
            }

            public Task<bool> VerifyAsync() => Task.FromResult(Connected);
            public Task ReregisterToolsAsync() => Task.CompletedTask;
        }

        /// <summary>
        /// The bridge binding via its editor-idle retry (no StartAsync) must surface as
        /// connected through GetState/IsRunning, port included.
        /// </summary>
        [Test]
        public void GetState_Stdio_ReconcilesWhenBridgeStartsOutsideManager()
        {
            var client = new ExternallyControlledStdioClient();
            var manager = new TransportManager();
            manager.Configure(() => client, () => client);

            Assert.IsFalse(manager.IsRunning(TransportMode.Stdio), "sanity: starts disconnected");

            client.Connected = true; // bridge bound via editor-idle retry, no StartAsync involved

            Assert.IsTrue(manager.IsRunning(TransportMode.Stdio),
                "manager must report the live bridge even when it started outside StartAsync");
            TransportState state = manager.GetState(TransportMode.Stdio);
            Assert.IsTrue(state.IsConnected);
            Assert.AreEqual(6400, state.Port, "reconciled state comes from the client, port included");
        }

        /// <summary>
        /// A listener that died without StopAsync (e.g. socket teardown on reload) must stop
        /// being reported as connected.
        /// </summary>
        [Test]
        public void GetState_Stdio_ReconcilesWhenBridgeStopsOutsideManager()
        {
            var client = new ExternallyControlledStdioClient();
            var manager = new TransportManager();
            manager.Configure(() => client, () => client);

            Task<bool> started = manager.StartAsync(TransportMode.Stdio);
            Assert.IsTrue(started.IsCompleted && started.Result, "fake start completes synchronously");
            Assert.IsTrue(manager.IsRunning(TransportMode.Stdio));

            client.Connected = false; // listener died without StopAsync (e.g. socket teardown on reload)

            Assert.IsFalse(manager.IsRunning(TransportMode.Stdio),
                "manager must not report a bridge that is no longer listening");
        }

        /// <summary>
        /// A stop/start cycle between two reads can rebind to a different port while both
        /// snapshots stay "connected" (the 6400↔6401 busy-port fallback); GetState must
        /// surface the new port, not the stale one.
        /// </summary>
        [Test]
        public void GetState_Stdio_ReconcilesWhenBridgePortChangesWhileConnected()
        {
            var client = new ExternallyControlledStdioClient();
            var manager = new TransportManager();
            manager.Configure(() => client, () => client);

            client.Connected = true;
            Assert.AreEqual(6400, manager.GetState(TransportMode.Stdio).Port, "sanity: initial port");

            client.Port = 6401; // bridge restarted onto the fallback port between reads

            TransportState state = manager.GetState(TransportMode.Stdio);
            Assert.IsTrue(state.IsConnected);
            Assert.AreEqual(6401, state.Port, "a rebind while connected must refresh the reported port");
        }

        [Test]
        public void StartAsync_AfterCompletedStart_StartsFresh()
        {
            var client = new FakeTransportClient();
            var manager = new TransportManager();
            manager.Configure(() => client, () => client);

            Task<bool> first = manager.StartAsync(TransportMode.Http);
            Assert.IsTrue(first.IsCompleted && first.Result, "fake start should complete synchronously");

            Task<bool> second = manager.StartAsync(TransportMode.Http);
            Assert.AreEqual(2, client.StartCalls, "a completed start must not block later restarts");
            Assert.IsTrue(second.IsCompleted && second.Result);
        }
    }
}
