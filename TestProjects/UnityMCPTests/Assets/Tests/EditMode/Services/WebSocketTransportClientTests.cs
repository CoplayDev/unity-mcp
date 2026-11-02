using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.WebSockets;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MCPForUnity.Editor.Services.Transport.Transports;
using NUnit.Framework;

namespace MCPForUnityTests.Editor.Services
{
    [TestFixture]
    public class WebSocketTransportClientTests
    {
        private const string CandidateBuilderMethodName = "BuildConnectionCandidateUris";
        private const string WebSocketTransportClientTypeName = "MCPForUnity.Editor.Services.Transport.Transports.WebSocketTransportClient";
        private static readonly MethodInfo BuildConnectionCandidateUrisMethod = ResolveCandidateBuilderMethod();

        [Test]
        public void BuildConnectionCandidateUris_NullEndpoint_ReturnsEmptyList()
        {
            // Act
            List<Uri> candidates = InvokeBuildConnectionCandidateUris(null);

            // Assert
            Assert.IsNotNull(candidates);
            Assert.AreEqual(0, candidates.Count);
        }

        [Test]
        public void BuildConnectionCandidateUris_NonLocalhost_ReturnsOriginalOnly()
        {
            // Arrange
            var endpoint = new Uri("ws://127.0.0.1:8080/hub/plugin");

            // Act
            List<Uri> candidates = InvokeBuildConnectionCandidateUris(endpoint);

            // Assert
            Assert.AreEqual(1, candidates.Count);
            Assert.AreEqual(endpoint, candidates[0]);
        }

        [Test]
        public void BuildConnectionCandidateUris_Localhost_AddsIPv4AndIPv6Fallbacks()
        {
            // Arrange
            var endpoint = new Uri("ws://localhost:8080/hub/plugin");

            // Act
            List<Uri> candidates = InvokeBuildConnectionCandidateUris(endpoint);

            // Assert
            Assert.AreEqual(3, candidates.Count);
            CollectionAssert.AreEqual(
                new[] { "localhost", "127.0.0.1", "::1" },
                candidates.Select(uri => NormalizeHostForComparison(uri.Host)).ToArray());

            int uniqueCount = candidates
                .Select(uri => uri.AbsoluteUri)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count();
            Assert.AreEqual(candidates.Count, uniqueCount, "Fallback list should not contain duplicate endpoints.");
        }

        [Test]
        public void BuildConnectionCandidateUris_LocalhostFallbacks_PreserveSchemePortPathAndQuery()
        {
            // Arrange
            var endpoint = new Uri("wss://localhost:9443/custom/path?mode=test");

            // Act
            List<Uri> candidates = InvokeBuildConnectionCandidateUris(endpoint);

            // Assert
            Assert.AreEqual(3, candidates.Count);
            foreach (Uri candidate in candidates)
            {
                Assert.AreEqual("wss", candidate.Scheme);
                Assert.AreEqual(9443, candidate.Port);
                Assert.AreEqual("/custom/path", candidate.AbsolutePath);
                Assert.AreEqual("?mode=test", candidate.Query);
            }
        }

        [Test]
        public void CloseForDomainReload_OpenSocket_SendsBoundedCloseOutput()
        {
            var socket = new RecordingWebSocket();

            WebSocketTransportClient.CloseForDomainReload(socket, TimeSpan.FromSeconds(1));

            Assert.IsTrue(socket.CloseOutputCalled);
            Assert.AreEqual(WebSocketCloseStatus.EndpointUnavailable, socket.RequestedCloseStatus);
            Assert.AreEqual("Domain reload", socket.RequestedCloseDescription);
            Assert.IsFalse(socket.AbortCalled);
        }

        [Test]
        public void CloseForDomainReload_CloseFailure_AbortsSocket()
        {
            var socket = new RecordingWebSocket { ThrowOnCloseOutput = true };

            WebSocketTransportClient.CloseForDomainReload(socket, TimeSpan.FromSeconds(1));

            Assert.IsTrue(socket.CloseOutputCalled);
            Assert.IsTrue(socket.AbortCalled);
        }

        [Test]
        public void CloseForDomainReload_CloseReceived_CompletesCloseOutput()
        {
            var socket = new RecordingWebSocket { SocketState = WebSocketState.CloseReceived };

            WebSocketTransportClient.CloseForDomainReload(socket, TimeSpan.FromSeconds(1));

            Assert.IsTrue(socket.CloseOutputCalled);
            Assert.IsFalse(socket.AbortCalled);
        }

        [Test]
        public void CloseForDomainReload_CloseTimeout_AbortsSocket()
        {
            var socket = new RecordingWebSocket { WaitForCancellation = true };

            WebSocketTransportClient.CloseForDomainReload(socket, TimeSpan.FromMilliseconds(10));

            Assert.IsTrue(socket.CloseOutputCalled);
            Assert.IsTrue(socket.AbortCalled);
        }

        [Test]
        public void CloseForDomainReload_ConnectingSocket_AbortsImmediately()
        {
            var socket = new RecordingWebSocket { SocketState = WebSocketState.Connecting };

            WebSocketTransportClient.CloseForDomainReload(socket, TimeSpan.FromSeconds(1));

            Assert.IsFalse(socket.CloseOutputCalled);
            Assert.IsTrue(socket.AbortCalled);
        }

        [TestCase(WebSocketState.Closed)]
        [TestCase(WebSocketState.Aborted)]
        public void CloseForDomainReload_TerminalSocket_DoesNothing(WebSocketState state)
        {
            var socket = new RecordingWebSocket { SocketState = state };

            WebSocketTransportClient.CloseForDomainReload(socket, TimeSpan.FromSeconds(1));

            Assert.IsFalse(socket.CloseOutputCalled);
            Assert.IsFalse(socket.AbortCalled);
        }

        private static List<Uri> InvokeBuildConnectionCandidateUris(Uri endpoint)
        {
            if (BuildConnectionCandidateUrisMethod == null)
            {
                Assert.Fail(BuildMissingMethodDiagnostic());
            }
            var result = BuildConnectionCandidateUrisMethod.Invoke(null, new object[] { endpoint });
            Assert.IsNotNull(result);
            Assert.IsInstanceOf<List<Uri>>(result);
            return (List<Uri>)result;
        }

        private static MethodInfo ResolveCandidateBuilderMethod()
        {
            MethodInfo direct = GetCandidateBuilderMethod(typeof(WebSocketTransportClient));
            if (direct != null)
            {
                return direct;
            }

            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type candidateType = assembly.GetType(WebSocketTransportClientTypeName);
                if (candidateType == null)
                {
                    continue;
                }

                MethodInfo method = GetCandidateBuilderMethod(candidateType);
                if (method != null)
                {
                    return method;
                }
            }

            return null;
        }

        private static MethodInfo GetCandidateBuilderMethod(Type type)
        {
            const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static;
            MethodInfo direct = type.GetMethod(
                CandidateBuilderMethodName,
                flags,
                binder: null,
                types: new[] { typeof(Uri) },
                modifiers: null);
            if (direct != null)
            {
                return direct;
            }

            // Fallback for environments where signature binding can differ between loaded copies.
            return type.GetMethods(flags).FirstOrDefault(method =>
            {
                if (!string.Equals(method.Name, CandidateBuilderMethodName, StringComparison.Ordinal))
                {
                    return false;
                }

                ParameterInfo[] parameters = method.GetParameters();
                return parameters.Length == 1 && parameters[0].ParameterType == typeof(Uri);
            });
        }

        private static string BuildMissingMethodDiagnostic()
        {
            var sb = new StringBuilder();
            sb.Append("Expected private candidate builder method to exist. Searched loaded assemblies for ")
              .Append(WebSocketTransportClientTypeName)
              .Append('.')
              .Append(CandidateBuilderMethodName)
              .Append(". Loaded candidate types:");

            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type candidateType = assembly.GetType(WebSocketTransportClientTypeName);
                if (candidateType == null)
                {
                    continue;
                }

                sb.Append("\n- ")
                  .Append(assembly.FullName)
                  .Append(" @ ")
                  .Append(string.IsNullOrEmpty(assembly.Location) ? "<dynamic>" : assembly.Location);
            }

            return sb.ToString();
        }

        private static string NormalizeHostForComparison(string host)
        {
            if (string.IsNullOrEmpty(host))
            {
                return host;
            }

            return host.Trim('[', ']');
        }

        private sealed class RecordingWebSocket : WebSocket
        {
            public bool ThrowOnCloseOutput { get; set; }
            public bool WaitForCancellation { get; set; }
            public WebSocketState SocketState { get; set; } = WebSocketState.Open;
            public bool CloseOutputCalled { get; private set; }
            public bool AbortCalled { get; private set; }
            public WebSocketCloseStatus RequestedCloseStatus { get; private set; }
            public string RequestedCloseDescription { get; private set; }

            public override WebSocketCloseStatus? CloseStatus => null;
            public override string CloseStatusDescription => null;
            public override WebSocketState State => SocketState;
            public override string SubProtocol => null;

            public override void Abort()
            {
                AbortCalled = true;
            }

            public override Task CloseAsync(
                WebSocketCloseStatus closeStatus,
                string statusDescription,
                CancellationToken cancellationToken)
            {
                throw new NotSupportedException();
            }

            public override Task CloseOutputAsync(
                WebSocketCloseStatus closeStatus,
                string statusDescription,
                CancellationToken cancellationToken)
            {
                CloseOutputCalled = true;
                RequestedCloseStatus = closeStatus;
                RequestedCloseDescription = statusDescription;
                if (ThrowOnCloseOutput)
                {
                    throw new InvalidOperationException("close failed");
                }
                if (WaitForCancellation)
                {
                    return Task.Delay(Timeout.Infinite, cancellationToken);
                }

                return Task.CompletedTask;
            }

            public override void Dispose()
            {
            }

            public override Task<WebSocketReceiveResult> ReceiveAsync(
                ArraySegment<byte> buffer,
                CancellationToken cancellationToken)
            {
                throw new NotSupportedException();
            }

            public override Task SendAsync(
                ArraySegment<byte> buffer,
                WebSocketMessageType messageType,
                bool endOfMessage,
                CancellationToken cancellationToken)
            {
                throw new NotSupportedException();
            }
        }
    }
}
