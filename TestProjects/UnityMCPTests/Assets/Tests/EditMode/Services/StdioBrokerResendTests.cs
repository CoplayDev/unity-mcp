using System;
using System.Collections;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using NUnit.Framework;

using UnityEngine.TestTools;
using MCPForUnity.Editor.Services.Transport.Transports;

namespace MCPForUnityTests.Editor.Services
{
    /// <summary>
    /// When a long-running command outlives the broker's patience, the Python side reconnects and
    /// resends it. The bridge used to queue that resend as a brand-new command behind the original,
    /// so the work ran twice — visibly, for commands with side effects (issue #1130).
    /// </summary>
    [TestFixture]
    public class StdioBrokerResendTests
    {
        private const int ConnectTimeoutMs = 5000;
        private const int ReadTimeoutMs = 10000;

        [Test]
        public void IsBrokerResend_SamePayloadFromAnotherConnection_IsAResend()
        {
            object first = new object();
            object second = new object();

            Assert.IsTrue(StdioBridgeHost.IsBrokerResend("{\"type\":\"a\"}", first, "{\"type\":\"a\"}", second),
                "same payload arriving on a different connection is the resend signature");
        }

        [Test]
        public void IsBrokerResend_SameConnection_IsNotAResend()
        {
            object connection = new object();

            Assert.IsFalse(StdioBridgeHost.IsBrokerResend("{\"type\":\"a\"}", connection, "{\"type\":\"a\"}", connection),
                "one connection handles commands sequentially, so identical payloads are distinct requests");
        }

        [Test]
        public void IsBrokerResend_DifferentPayload_IsNotAResend()
        {
            Assert.IsFalse(StdioBridgeHost.IsBrokerResend("{\"type\":\"a\"}", new object(), "{\"type\":\"b\"}", new object()),
                "different work must never be collapsed");
        }

        [Test]
        public void IsBrokerResend_UnknownOwner_IsNotAResend()
        {
            Assert.IsFalse(StdioBridgeHost.IsBrokerResend("{\"type\":\"a\"}", null, "{\"type\":\"a\"}", new object()),
                "a command with no recorded owner cannot be proven to be a resend");
            Assert.IsFalse(StdioBridgeHost.IsBrokerResend("{\"type\":\"a\"}", new object(), "{\"type\":\"a\"}", null),
                "an incoming command with no owner cannot be proven to be a resend");
        }

        /// <summary>
        /// The bridge must not queue a second copy of a command that is already in flight from
        /// another connection. Asserted on the queue itself while the main thread is blocked, so
        /// no command has been dispatched yet and the result does not depend on what the command
        /// would have done. Requires the bridge (batchmode needs UNITY_MCP_ALLOW_BATCH).
        /// </summary>
        [UnityTest]
        public IEnumerator IdenticalCommandResentOnNewConnection_IsQueuedOnce()
        {
            if (!StdioBridgeHost.IsRunning)
            {
                Assert.Ignore("StdioBridgeHost is not running; skipping broker-resend test.");
                yield break;
            }

            int port = StdioBridgeHost.GetCurrentPort();
            byte[] command = Encoding.UTF8.GetBytes(
                "{\"type\":\"read_console\",\"params\":{\"action\":\"get\",\"count\":1}}");

            TcpClient first = null;
            TcpClient second = null;
            int queuedAfterFirst;
            int queuedAfterResend;
            try
            {
                first = Connect(port);
                SendFrame(first.GetStream(), command);

                // Sleeping on the main thread lets the listener task read and queue the frame while
                // ProcessCommands — an editor-update hook — cannot run and drain it. The wait also
                // has to happen before the second connect: a new connection closes stale clients,
                // and if it wins that race the first frame is never read at all.
                Thread.Sleep(1500);
                queuedAfterFirst = StdioBridgeHost.QueuedCommandCount;

                // A second connection is what the broker opens after giving up on the first.
                second = Connect(port);
                SendFrame(second.GetStream(), command);
                Thread.Sleep(1500);
                queuedAfterResend = StdioBridgeHost.QueuedCommandCount;
            }
            finally
            {
                SafeClose(first);
                SafeClose(second);
            }

            // Let the queue drain so we do not leak state into later tests.
            for (int i = 0; i < 120; i++)
                yield return null;

            Assert.AreEqual(1, queuedAfterFirst,
                "precondition: the first command must be sitting in the queue undrained — "
                + $"found {queuedAfterFirst} entries, so this run proves nothing about the resend");
            Assert.AreEqual(1, queuedAfterResend,
                $"the resend should have attached to the in-flight command, but {queuedAfterResend} "
                + "entries were queued — the command would run that many times");
        }

        private static TcpClient Connect(int port)
        {
            var client = new TcpClient();
            Assert.IsTrue(client.ConnectAsync("127.0.0.1", port).Wait(ConnectTimeoutMs), "connect timed out");
            client.ReceiveTimeout = ReadTimeoutMs;
            string handshake = ReadLine(client.GetStream(), ReadTimeoutMs);
            Assert.That(handshake, Does.Contain("FRAMING=1"), "expected the framing handshake");
            return client;
        }

        private static void SafeClose(TcpClient client)
        {
            if (client == null) return;
            try
            {
                client.Client.LingerState = new LingerOption(true, 0);
                client.Close();
            }
            catch { }
        }

        private static string ReadLine(NetworkStream stream, int timeoutMs)
        {
            stream.ReadTimeout = timeoutMs;
            var sb = new StringBuilder();
            var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            while (DateTime.UtcNow <= deadline)
            {
                int b = stream.ReadByte();
                if (b < 0) throw new IOException("Connection closed while reading handshake");
                if (b == '\n') return sb.ToString();
                sb.Append((char)b);
            }
            throw new TimeoutException("Timed out reading handshake line");
        }

        private static void SendFrame(NetworkStream stream, byte[] payload)
        {
            byte[] header = new byte[8];
            ulong len = (ulong)payload.LongLength;
            for (int i = 0; i < 8; i++)
                header[i] = (byte)(len >> (56 - 8 * i));
            stream.Write(header, 0, 8);
            stream.Write(payload, 0, payload.Length);
            stream.Flush();
        }
    }
}
