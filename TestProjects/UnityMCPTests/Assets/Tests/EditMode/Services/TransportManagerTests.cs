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
