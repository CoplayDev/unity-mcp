using System;
using System.Collections;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using MCPForUnity.Editor.Services.Transport.Transports;

namespace MCPForUnityTests.Editor.Services
{
    /// <summary>
    /// Tests that StdioBridgeHost correctly handles client reconnection scenarios.
    /// After an abrupt client disconnect, a new client must be able to connect and
    /// have its commands processed — this was broken by the zombie state bug (#785).
    /// </summary>
    [TestFixture]
    public class StdioBridgeReconnectTests
    {
        private const int ConnectTimeoutMs = 5000;
        private const int ReadTimeoutMs = 10000;

        [UnityTest]
        public IEnumerator NewClient_AfterAbruptDisconnect_CanSendAndReceiveCommands()
        {
            if (!StdioBridgeHost.IsRunning)
            {
                Assert.Ignore("StdioBridgeHost is not running; skipping reconnect test.");
                yield break;
            }

            int port = StdioBridgeHost.GetCurrentPort();

            // --- First client: connect, verify ping/pong, then abruptly close ---
            using (var client1 = new TcpClient())
            {
                Assert.IsTrue(client1.ConnectAsync("127.0.0.1", port).Wait(ConnectTimeoutMs),
                    "First client connect timed out");
                client1.ReceiveTimeout = ReadTimeoutMs;
                var stream1 = client1.GetStream();

                string handshake1 = ReadLine(stream1, ReadTimeoutMs);
                Assert.That(handshake1, Does.Contain("FRAMING=1"), "First client should receive handshake");

                // Send a framed ping
                SendFrame(stream1, Encoding.UTF8.GetBytes("ping"));
                byte[] pongBytes = ReadFrame(stream1, ReadTimeoutMs);
                string pong1 = Encoding.UTF8.GetString(pongBytes);
                Assert.That(pong1, Does.Contain("pong"), "First client should get pong response");

                // Abrupt close — simulates server crash / domain reload disconnect
                client1.Client.LingerState = new LingerOption(true, 0);
                client1.Close();
            }

            // Wait a few frames for cleanup
            for (int i = 0; i < 10; i++)
                yield return null;

            // --- Second client: connect and verify commands still work ---
            using (var client2 = new TcpClient())
            {
                Assert.IsTrue(client2.ConnectAsync("127.0.0.1", port).Wait(ConnectTimeoutMs),
                    "Second client connect timed out");
                client2.ReceiveTimeout = ReadTimeoutMs;
                var stream2 = client2.GetStream();

                string handshake2 = ReadLine(stream2, ReadTimeoutMs);
                Assert.That(handshake2, Does.Contain("FRAMING=1"), "Second client should receive handshake");

                // Send a framed ping — this is the critical check that would fail
                // if the bridge is in zombie state.
                SendFrame(stream2, Encoding.UTF8.GetBytes("ping"));
                byte[] pongBytes2 = ReadFrame(stream2, ReadTimeoutMs);
                string pong2 = Encoding.UTF8.GetString(pongBytes2);
                Assert.That(pong2, Does.Contain("pong"), "Second client should get pong response after reconnect");

                client2.Close();
            }
        }

        [UnityTest]
        public IEnumerator NewClient_WhileOldClientStillConnected_ClosesStaleClient()
        {
            if (!StdioBridgeHost.IsRunning)
            {
                Assert.Ignore("StdioBridgeHost is not running; skipping reconnect test.");
                yield break;
            }

            int port = StdioBridgeHost.GetCurrentPort();

            // --- First client: connect and verify handshake (but don't close) ---
            var client1 = new TcpClient();
            try
            {
                Assert.IsTrue(client1.ConnectAsync("127.0.0.1", port).Wait(ConnectTimeoutMs),
                    "First client connect timed out");
                client1.ReceiveTimeout = ReadTimeoutMs;
                var stream1 = client1.GetStream();

                string handshake1 = ReadLine(stream1, ReadTimeoutMs);
                Assert.That(handshake1, Does.Contain("FRAMING=1"), "First client should receive handshake");

                // Verify ping works on first client
                SendFrame(stream1, Encoding.UTF8.GetBytes("ping"));
                byte[] pong1Bytes = ReadFrame(stream1, ReadTimeoutMs);
                Assert.That(Encoding.UTF8.GetString(pong1Bytes), Does.Contain("pong"));

                // --- Second client: connect while first is still open ---
                using (var client2 = new TcpClient())
                {
                    Assert.IsTrue(client2.ConnectAsync("127.0.0.1", port).Wait(ConnectTimeoutMs),
                        "Second client connect timed out");
                    client2.ReceiveTimeout = ReadTimeoutMs;
                    var stream2 = client2.GetStream();

                    string handshake2 = ReadLine(stream2, ReadTimeoutMs);
                    Assert.That(handshake2, Does.Contain("FRAMING=1"), "Second client should receive handshake");

                    // Stale-client cleanup runs synchronously in HandleClientAsync before
                    // the read loop, so by the time we read the handshake it's already done.
                    // No yield needed — yielding here creates a window for the MCP Python
                    // server to reconnect and close our test client as stale.
                    SendFrame(stream2, Encoding.UTF8.GetBytes("ping"));
                    byte[] pong2Bytes = ReadFrame(stream2, ReadTimeoutMs);
                    Assert.That(Encoding.UTF8.GetString(pong2Bytes), Does.Contain("pong"),
                        "Second client should get pong after stale client cleanup");

                    client2.Close();
                }

                // First client should now be disconnected by the bridge.
                // A read attempt should throw or return 0 bytes.
                yield return null;
                bool firstClientDisconnected = false;
                try
                {
                    SendFrame(stream1, Encoding.UTF8.GetBytes("ping"));
                    ReadFrame(stream1, 2000);
                }
                catch
                {
                    firstClientDisconnected = true;
                }

                Assert.IsTrue(firstClientDisconnected, "First client should be disconnected after second client connects");
            }
            finally
            {
                try { client1.Close(); } catch { }
            }
        }

        [UnityTest]
        public IEnumerator InternalClient_WhilePrimaryConnected_DoesNotClosePrimary()
        {
            if (!StdioBridgeHost.IsRunning)
            {
                Assert.Ignore("StdioBridgeHost is not running; skipping reconnect test.");
                yield break;
            }

            int port = StdioBridgeHost.GetCurrentPort();

            using (var primary = new TcpClient())
            using (var internalClient = new TcpClient())
            {
                Assert.IsTrue(primary.ConnectAsync("127.0.0.1", port).Wait(ConnectTimeoutMs),
                    "Primary client connect timed out");
                primary.ReceiveTimeout = ReadTimeoutMs;
                var primaryStream = primary.GetStream();
                string primaryHandshake = ReadLine(primaryStream, ReadTimeoutMs);
                Assert.That(primaryHandshake, Does.Contain("FRAMING=1"), "Primary client should receive handshake");

                SendFrame(primaryStream, Encoding.UTF8.GetBytes("ping"));
                byte[] primaryPong = ReadFrame(primaryStream, ReadTimeoutMs);
                Assert.That(Encoding.UTF8.GetString(primaryPong), Does.Contain("pong"));

                Assert.IsTrue(internalClient.ConnectAsync("127.0.0.1", port).Wait(ConnectTimeoutMs),
                    "Internal client connect timed out");
                internalClient.ReceiveTimeout = ReadTimeoutMs;
                var internalStream = internalClient.GetStream();
                string internalHandshake = ReadLine(internalStream, ReadTimeoutMs);
                Assert.That(internalHandshake, Does.Contain("FRAMING=1"), "Internal client should receive handshake");

                SendFrame(internalStream, Encoding.UTF8.GetBytes(
                    "{\"type\":\"hello\",\"role\":\"internal\",\"client_id\":\"test-terminal\"}"));
                byte[] helloBytes = ReadFrame(internalStream, ReadTimeoutMs);
                Assert.That(Encoding.UTF8.GetString(helloBytes), Does.Contain("ready"));

                SendFrame(internalStream, Encoding.UTF8.GetBytes("ping"));
                byte[] internalPong = ReadFrame(internalStream, ReadTimeoutMs);
                Assert.That(Encoding.UTF8.GetString(internalPong), Does.Contain("pong"));

                yield return null;

                SendFrame(primaryStream, Encoding.UTF8.GetBytes("ping"));
                byte[] secondPrimaryPong = ReadFrame(primaryStream, ReadTimeoutMs);
                Assert.That(Encoding.UTF8.GetString(secondPrimaryPong), Does.Contain("pong"),
                    "Primary client should remain connected after internal client connects");
            }
        }

        [UnityTest]
        public IEnumerator Stop_WithInternalLease_ClosesPrimaryButKeepsInternalClient()
        {
            if (!StdioBridgeHost.IsRunning)
            {
                Assert.Ignore("StdioBridgeHost is not running; skipping reconnect test.");
                yield break;
            }

            int port = StdioBridgeHost.GetCurrentPort();

            bool restoreBridge = false;
            try
            {
                using (var lease = StdioBridgeHost.AcquireInternalLease())
                using (var primary = new TcpClient())
                using (var internalClient = new TcpClient())
                {
                    Assert.IsTrue(primary.ConnectAsync("127.0.0.1", port).Wait(ConnectTimeoutMs),
                        "Primary client connect timed out");
                    primary.ReceiveTimeout = ReadTimeoutMs;
                    var primaryStream = primary.GetStream();
                    Assert.That(ReadLine(primaryStream, ReadTimeoutMs), Does.Contain("FRAMING=1"));
                    SendFrame(primaryStream, Encoding.UTF8.GetBytes("ping"));
                    Assert.That(Encoding.UTF8.GetString(ReadFrame(primaryStream, ReadTimeoutMs)), Does.Contain("pong"));

                    Assert.IsTrue(internalClient.ConnectAsync("127.0.0.1", port).Wait(ConnectTimeoutMs),
                        "Internal client connect timed out");
                    internalClient.ReceiveTimeout = ReadTimeoutMs;
                    var internalStream = internalClient.GetStream();
                    Assert.That(ReadLine(internalStream, ReadTimeoutMs), Does.Contain("FRAMING=1"));
                    SendFrame(internalStream, Encoding.UTF8.GetBytes(
                        "{\"type\":\"hello\",\"role\":\"internal\",\"client_id\":\"lease-test\"}"));
                    Assert.That(Encoding.UTF8.GetString(ReadFrame(internalStream, ReadTimeoutMs)), Does.Contain("ready"));

                    StdioBridgeHost.Stop();
                    restoreBridge = true;
                    yield return null;

                    bool primaryDisconnected = false;
                    try
                    {
                        SendFrame(primaryStream, Encoding.UTF8.GetBytes("ping"));
                        ReadFrame(primaryStream, 2000);
                    }
                    catch
                    {
                        primaryDisconnected = true;
                    }

                    Assert.IsTrue(primaryDisconnected, "Primary client should be closed when stdio transport stops");

                    SendFrame(internalStream, Encoding.UTF8.GetBytes("ping"));
                    Assert.That(Encoding.UTF8.GetString(ReadFrame(internalStream, ReadTimeoutMs)), Does.Contain("pong"),
                        "Internal client should remain connected while lease is active");
                }
            }
            finally
            {
                if (restoreBridge)
                {
                    StdioBridgeHost.Start();
                }
            }
        }

        [UnityTest]
        public IEnumerator MultipleInternalClients_WithSameClientId_RemainConnected()
        {
            if (!StdioBridgeHost.IsRunning)
            {
                Assert.Ignore("StdioBridgeHost is not running; skipping reconnect test.");
                yield break;
            }

            int port = StdioBridgeHost.GetCurrentPort();

            using (var first = new TcpClient())
            using (var second = new TcpClient())
            {
                Assert.IsTrue(first.ConnectAsync("127.0.0.1", port).Wait(ConnectTimeoutMs),
                    "First internal client connect timed out");
                first.ReceiveTimeout = ReadTimeoutMs;
                var firstStream = first.GetStream();
                Assert.That(ReadLine(firstStream, ReadTimeoutMs), Does.Contain("FRAMING=1"));
                SendFrame(firstStream, Encoding.UTF8.GetBytes(
                    "{\"type\":\"hello\",\"role\":\"internal\",\"client_id\":\"shared-terminal\"}"));
                Assert.That(Encoding.UTF8.GetString(ReadFrame(firstStream, ReadTimeoutMs)), Does.Contain("ready"));

                Assert.IsTrue(second.ConnectAsync("127.0.0.1", port).Wait(ConnectTimeoutMs),
                    "Second internal client connect timed out");
                second.ReceiveTimeout = ReadTimeoutMs;
                var secondStream = second.GetStream();
                Assert.That(ReadLine(secondStream, ReadTimeoutMs), Does.Contain("FRAMING=1"));
                SendFrame(secondStream, Encoding.UTF8.GetBytes(
                    "{\"type\":\"hello\",\"role\":\"internal\",\"client_id\":\"shared-terminal\"}"));
                Assert.That(Encoding.UTF8.GetString(ReadFrame(secondStream, ReadTimeoutMs)), Does.Contain("ready"));

                yield return null;

                SendFrame(firstStream, Encoding.UTF8.GetBytes("ping"));
                Assert.That(Encoding.UTF8.GetString(ReadFrame(firstStream, ReadTimeoutMs)), Does.Contain("pong"),
                    "First internal client should remain connected after a second internal client with the same id connects");

                SendFrame(secondStream, Encoding.UTF8.GetBytes("ping"));
                Assert.That(Encoding.UTF8.GetString(ReadFrame(secondStream, ReadTimeoutMs)), Does.Contain("pong"),
                    "Second internal client should remain connected");
            }
        }

        [UnityTest]
        public IEnumerator InternalLease_WhenItStartsBridge_StopsBridgeOnDispose()
        {
            bool wasRunning = StdioBridgeHost.IsRunning;
            try
            {
                if (wasRunning)
                {
                    StdioBridgeHost.Stop(true);
                    yield return null;
                }

                Assert.IsFalse(StdioBridgeHost.IsRunning, "Bridge should be stopped before testing lease-owned startup");

                int leasedPort;
                using (StdioBridgeHost.AcquireInternalLease())
                {
                    Assert.IsTrue(StdioBridgeHost.IsRunning, "Acquiring an internal lease should start the bridge when it is stopped");
                    leasedPort = StdioBridgeHost.GetCurrentPort();

                    using (var internalClient = new TcpClient())
                    {
                        Assert.IsTrue(internalClient.ConnectAsync("127.0.0.1", leasedPort).Wait(ConnectTimeoutMs),
                            "Internal client connect timed out");
                        internalClient.ReceiveTimeout = ReadTimeoutMs;
                        var stream = internalClient.GetStream();
                        Assert.That(ReadLine(stream, ReadTimeoutMs), Does.Contain("FRAMING=1"));
                        SendFrame(stream, Encoding.UTF8.GetBytes(
                            "{\"type\":\"hello\",\"role\":\"internal\",\"client_id\":\"lease-owner\"}"));
                        Assert.That(Encoding.UTF8.GetString(ReadFrame(stream, ReadTimeoutMs)), Does.Contain("ready"));
                    }
                }

                yield return null;
                Assert.IsFalse(StdioBridgeHost.IsRunning,
                    "Releasing the final lease should stop a bridge that was started only for the internal terminal");

                Assert.IsFalse(CanConnect(leasedPort, 500),
                    "Lease-owned bridge should no longer accept connections after the lease is disposed");
            }
            finally
            {
                if (wasRunning && !StdioBridgeHost.IsRunning)
                {
                    StdioBridgeHost.Start();
                }
            }
        }

        #region Frame protocol helpers

        private static bool CanConnect(int port, int timeoutMs)
        {
            using (var probe = new TcpClient())
            {
                try
                {
                    return probe.ConnectAsync("127.0.0.1", port).Wait(timeoutMs);
                }
                catch
                {
                    return false;
                }
            }
        }

        private static string ReadLine(NetworkStream stream, int timeoutMs)
        {
            var sb = new StringBuilder();
            var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            stream.ReadTimeout = timeoutMs;

            while (DateTime.UtcNow < deadline)
            {
                int b = stream.ReadByte();
                if (b < 0)
                    throw new IOException("Connection closed while reading line");
                if (b == '\n')
                    return sb.ToString();
                sb.Append((char)b);
            }
            throw new TimeoutException("Timed out reading line from stream");
        }

        private static void SendFrame(NetworkStream stream, byte[] payload)
        {
            byte[] header = new byte[8];
            ulong len = (ulong)payload.LongLength;
            header[0] = (byte)(len >> 56);
            header[1] = (byte)(len >> 48);
            header[2] = (byte)(len >> 40);
            header[3] = (byte)(len >> 32);
            header[4] = (byte)(len >> 24);
            header[5] = (byte)(len >> 16);
            header[6] = (byte)(len >> 8);
            header[7] = (byte)(len);
            stream.Write(header, 0, 8);
            stream.Write(payload, 0, payload.Length);
            stream.Flush();
        }

        private static byte[] ReadFrame(NetworkStream stream, int timeoutMs)
        {
            stream.ReadTimeout = timeoutMs;

            byte[] header = ReadExact(stream, 8, timeoutMs);
            ulong payloadLen =
                ((ulong)header[0] << 56) | ((ulong)header[1] << 48) |
                ((ulong)header[2] << 40) | ((ulong)header[3] << 32) |
                ((ulong)header[4] << 24) | ((ulong)header[5] << 16) |
                ((ulong)header[6] << 8)  | header[7];

            if (payloadLen == 0 || payloadLen > 16 * 1024 * 1024)
                throw new IOException($"Invalid frame length: {payloadLen}");

            return ReadExact(stream, (int)payloadLen, timeoutMs);
        }

        private static byte[] ReadExact(NetworkStream stream, int count, int timeoutMs)
        {
            byte[] buffer = new byte[count];
            int offset = 0;
            var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);

            while (offset < count)
            {
                if (DateTime.UtcNow > deadline)
                    throw new TimeoutException($"Timed out reading {count} bytes (got {offset})");

                int remaining = count - offset;
                int read = stream.Read(buffer, offset, remaining);
                if (read == 0)
                    throw new IOException("Connection closed before reading expected bytes");
                offset += read;
            }

            return buffer;
        }

        #endregion
    }
}
