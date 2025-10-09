using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace MCPForUnity.Editor.Services
{
    /// <summary>
    /// Implementation of bridge control service
    /// </summary>
    public class BridgeControlService : IBridgeControlService
    {
        public bool IsRunning => MCPForUnityBridge.IsRunning;
        public int CurrentPort => MCPForUnityBridge.GetCurrentPort();
        public bool IsAutoConnectMode => MCPForUnityBridge.IsAutoConnectMode();

        public void Start()
        {
            // If server is installed, use auto-connect mode
            // Otherwise use standard mode
            string serverPath = MCPServiceLocator.Paths.GetPythonServerPath();
            if (!string.IsNullOrEmpty(serverPath) && File.Exists(Path.Combine(serverPath, "server.py")))
            {
                MCPForUnityBridge.StartAutoConnect();
            }
            else
            {
                MCPForUnityBridge.Start();
            }
        }

        public void Stop()
        {
            MCPForUnityBridge.Stop();
        }

        public BridgeVerificationResult Verify(int port)
        {
            var result = new BridgeVerificationResult
            {
                Success = false,
                HandshakeValid = false,
                PingSucceeded = false,
                Message = "Verification not started"
            };

            const int ConnectTimeoutMs = 1000;
            const int FrameTimeoutMs = 30000; // Match bridge frame I/O timeout

            try
            {
                using (var client = new TcpClient())
                {
                    // Attempt connection
                    var connectTask = client.ConnectAsync(IPAddress.Loopback, port);
                    if (!connectTask.Wait(ConnectTimeoutMs))
                    {
                        result.Message = "Connection timeout";
                        return result;
                    }

                    using (var stream = client.GetStream())
                    {
                        try { client.NoDelay = true; } catch { }

                        // 1) Read handshake line (ASCII, newline-terminated)
                        string handshake = ReadLineAscii(stream, 2000);
                        if (string.IsNullOrEmpty(handshake) || handshake.IndexOf("FRAMING=1", StringComparison.OrdinalIgnoreCase) < 0)
                        {
                            result.Message = "Bridge handshake missing FRAMING=1";
                            return result;
                        }

                        result.HandshakeValid = true;

                        // 2) Send framed "ping"
                        byte[] payload = Encoding.UTF8.GetBytes("ping");
                        WriteFrame(stream, payload, FrameTimeoutMs);

                        // 3) Read framed response and check for pong
                        string response = ReadFrameUtf8(stream, FrameTimeoutMs);
                        if (!string.IsNullOrEmpty(response) && response.IndexOf("pong", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            result.PingSucceeded = true;
                            result.Success = true;
                            result.Message = "Bridge verified successfully";
                        }
                        else
                        {
                            result.Message = $"Ping failed; response='{response}'";
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                result.Message = $"Verification error: {ex.Message}";
            }

            return result;
        }

        // Minimal framing helpers (8-byte big-endian length prefix), blocking with timeouts
        private static void WriteFrame(NetworkStream stream, byte[] payload, int timeoutMs)
        {
            if (payload == null) throw new ArgumentNullException(nameof(payload));
            if (payload.LongLength < 1) throw new IOException("Zero-length frames are not allowed");
            
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

            stream.WriteTimeout = timeoutMs;
            stream.Write(header, 0, header.Length);
            stream.Write(payload, 0, payload.Length);
        }

        private static string ReadFrameUtf8(NetworkStream stream, int timeoutMs)
        {
            byte[] header = ReadExact(stream, 8, timeoutMs);
            ulong len = ((ulong)header[0] << 56)
                      | ((ulong)header[1] << 48)
                      | ((ulong)header[2] << 40)
                      | ((ulong)header[3] << 32)
                      | ((ulong)header[4] << 24)
                      | ((ulong)header[5] << 16)
                      | ((ulong)header[6] << 8)
                      | header[7];
            if (len == 0UL) throw new IOException("Zero-length frames are not allowed");
            if (len > int.MaxValue) throw new IOException("Frame too large");
            byte[] payload = ReadExact(stream, (int)len, timeoutMs);
            return Encoding.UTF8.GetString(payload);
        }

        private static byte[] ReadExact(NetworkStream stream, int count, int timeoutMs)
        {
            byte[] buffer = new byte[count];
            int offset = 0;
            stream.ReadTimeout = timeoutMs;
            while (offset < count)
            {
                int read = stream.Read(buffer, offset, count - offset);
                if (read <= 0) throw new IOException("Connection closed before reading expected bytes");
                offset += read;
            }
            return buffer;
        }

        private static string ReadLineAscii(NetworkStream stream, int timeoutMs, int maxLen = 512)
        {
            stream.ReadTimeout = timeoutMs;
            using (var ms = new MemoryStream())
            {
                byte[] one = new byte[1];
                while (ms.Length < maxLen)
                {
                    int n = stream.Read(one, 0, 1);
                    if (n <= 0) break;
                    if (one[0] == (byte)'\n') break;
                    ms.WriteByte(one[0]);
                }
                return Encoding.ASCII.GetString(ms.ToArray());
            }
        }
    }
}
