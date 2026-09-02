using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using MCPForUnity.Editor.Helpers;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MCPForUnity.Editor.Services.Blender
{
    /// <summary>
    /// Minimal client for the BlenderMCP addon socket. Protocol: one JSON object
    /// {"type": ..., "params": {...}} per request and one JSON object
    /// {"status": "success"|"error", "result"|"message": ...} back, with no length framing —
    /// the addon parses whenever the accumulated bytes form valid JSON, so this does the same.
    /// A fresh connection is used per command so it never interleaves with the AI client's
    /// own BlenderMCP server talking to the same addon.
    /// </summary>
    public static class BlenderSocketClient
    {
        private const int ConnectTimeoutSeconds = 3;

        public static JToken Send(string type, JObject @params = null, int timeoutSeconds = 60)
        {
            string host = BlenderBridgePrefs.Host;
            int port = BlenderBridgePrefs.Port;

            var request = new JObject { ["type"] = type, ["params"] = @params ?? new JObject() };
            byte[] payload = Encoding.UTF8.GetBytes(request.ToString(Formatting.None));

            using var client = new TcpClient();
            IAsyncResult connect = client.BeginConnect(host, port, null, null);
            if (!connect.AsyncWaitHandle.WaitOne(TimeSpan.FromSeconds(ConnectTimeoutSeconds)) || !client.Connected)
            {
                throw new BlenderUnavailableException(
                    $"Blender addon not reachable at {host}:{port}. Start Blender and press " +
                    "'Connect to MCP server' in the BlenderMCP sidebar (N panel).");
            }
            client.EndConnect(connect);

            using NetworkStream stream = client.GetStream();
            stream.ReadTimeout = Math.Max(1, timeoutSeconds) * 1000;
            stream.Write(payload, 0, payload.Length);
            stream.Flush();

            var buffer = new MemoryStream();
            var chunk = new byte[65536];
            DateTime deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);

            while (true)
            {
                int n;
                try
                {
                    n = stream.Read(chunk, 0, chunk.Length);
                }
                catch (IOException e)
                {
                    throw new TimeoutException(
                        $"Timed out after {timeoutSeconds}s waiting for Blender to answer '{type}'.", e);
                }

                if (n <= 0) break;
                buffer.Write(chunk, 0, n);

                if (TryParseResponse(buffer.GetBuffer(), (int)buffer.Length, out JObject parsed)) return Unwrap(parsed, type);
                if (DateTime.UtcNow > deadline)
                    throw new TimeoutException($"Timed out after {timeoutSeconds}s waiting for Blender to answer '{type}'.");
            }

            if (TryParseResponse(buffer.GetBuffer(), (int)buffer.Length, out JObject final)) return Unwrap(final, type);
            throw new IOException($"Blender closed the connection before a complete response to '{type}' arrived.");
        }

        /// <summary>Runs Python inside Blender and returns its captured stdout.</summary>
        public static string RunPython(string code, int timeoutSeconds = 120)
        {
            JToken result = Send("execute_code", new JObject { ["code"] = code }, timeoutSeconds);
            return result?["result"]?.ToString() ?? string.Empty;
        }

        public static bool IsReachable(out string error)
        {
            try
            {
                Send("get_scene_info", null, 10);
                error = null;
                return true;
            }
            catch (Exception e)
            {
                error = e.Message;
                return false;
            }
        }

        /// <summary>True once the bytes received so far form one complete JSON object.</summary>
        internal static bool TryParseResponse(byte[] buffer, int length, out JObject obj)
        {
            try
            {
                obj = JObject.Parse(Encoding.UTF8.GetString(buffer, 0, length));
                return true;
            }
            catch
            {
                obj = null;
                return false;
            }
        }

        /// <summary>Returns the addon's "result" payload, or throws when it reported an error.</summary>
        internal static JToken Unwrap(JObject response, string type)
        {
            if (string.Equals((string)response["status"], "error", StringComparison.OrdinalIgnoreCase))
                throw new BlenderCommandException($"Blender '{type}' failed: {(string)response["message"] ?? "unknown error"}");
            return response["result"];
        }
    }

    public class BlenderUnavailableException : Exception
    {
        public BlenderUnavailableException(string message) : base(message) { }
    }

    public class BlenderCommandException : Exception
    {
        public BlenderCommandException(string message) : base(message) { }
    }
}
