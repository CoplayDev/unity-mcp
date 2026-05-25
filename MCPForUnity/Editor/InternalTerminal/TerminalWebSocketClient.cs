using System;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace WTL.InternalTerminal.Editor
{
    internal sealed class TerminalWebSocketClient : IDisposable
    {
        private readonly ConcurrentQueue<string> pendingScreens = new ConcurrentQueue<string>();
        private readonly ConcurrentQueue<string> pendingStatus = new ConcurrentQueue<string>();
        private ClientWebSocket socket;
        private CancellationTokenSource cancellation;
        private int pendingCols;
        private int pendingRows;

        public bool IsConnected => socket != null && socket.State == WebSocketState.Open;

        public async void Connect(string url)
        {
            DisposeSocket();
            cancellation = new CancellationTokenSource();
            socket = new ClientWebSocket();

            try
            {
                var builder = new UriBuilder(url)
                {
                    Scheme = "ws",
                    Query = string.Empty,
                    Path = "terminal"
                };
                await socket.ConnectAsync(builder.Uri, cancellation.Token);
                EnqueueStatus("WS connected");
                if (pendingCols > 0 && pendingRows > 0)
                {
                    Resize(pendingCols, pendingRows);
                }
                _ = Task.Run(ReceiveLoop);
            }
            catch (Exception exception)
            {
                EnqueueStatus(exception.Message);
                DisposeSocket();
            }
        }

        public bool TryDequeueScreen(out string json)
        {
            return pendingScreens.TryDequeue(out json);
        }

        public bool TryDequeueStatus(out string message)
        {
            return pendingStatus.TryDequeue(out message);
        }

        public async void SendKey(string key, string text, bool shift, bool control, bool alt)
        {
            await SendJson(
                "{\"type\":\"key\",\"key\":" + JsonString(key) +
                ",\"text\":" + JsonString(text ?? string.Empty) +
                ",\"shift\":" + Bool(shift) +
                ",\"ctrl\":" + Bool(control) +
                ",\"alt\":" + Bool(alt) +
                "}");
        }

        public async void SendPaste(string text)
        {
            await SendJson("{\"type\":\"paste\",\"data\":" + JsonString(text ?? string.Empty) + "}");
        }

        public async void SendText(string text)
        {
            await SendJson("{\"type\":\"text\",\"data\":" + JsonString(text ?? string.Empty) + "}");
        }

        public async void SendScroll(int lines)
        {
            await SendJson("{\"type\":\"scroll\",\"lines\":" + lines + "}");
        }

        public async void SendMouseWheel(int col, int row, int lines, bool shift, bool control, bool alt)
        {
            await SendJson(
                "{\"type\":\"mouseWheel\",\"col\":" + col +
                ",\"row\":" + row +
                ",\"lines\":" + lines +
                ",\"shift\":" + Bool(shift) +
                ",\"ctrl\":" + Bool(control) +
                ",\"alt\":" + Bool(alt) +
                "}");
        }

        public async void SendScrollTo(int viewportY)
        {
            await SendJson("{\"type\":\"scrollTo\",\"viewportY\":" + viewportY + "}");
        }

        public async void Resize(int cols, int rows)
        {
            pendingCols = cols;
            pendingRows = rows;
            await SendJson("{\"type\":\"resize\",\"cols\":" + cols + ",\"rows\":" + rows + "}");
        }

        public void Dispose()
        {
            DisposeSocket();
        }

        private async Task ReceiveLoop()
        {
            var buffer = new byte[8192];
            var builder = new StringBuilder();

            try
            {
                while (socket != null && socket.State == WebSocketState.Open && !cancellation.IsCancellationRequested)
                {
                    builder.Length = 0;
                    WebSocketReceiveResult result;
                    do
                    {
                        result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellation.Token);
                        if (result.MessageType == WebSocketMessageType.Close)
                        {
                            EnqueueStatus("Disconnected");
                            return;
                        }

                        builder.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                    }
                    while (!result.EndOfMessage);

                    HandleMessage(builder.ToString());
                }
            }
            catch (Exception exception)
            {
                if (!cancellation.IsCancellationRequested)
                {
                    EnqueueStatus(exception.Message);
                }
            }
        }

        private void HandleMessage(string json)
        {
            var type = ExtractJsonString(json, "type");
            if (type == "screen")
            {
                while (pendingScreens.TryDequeue(out _))
                {
                    // Keep only the newest full-frame snapshot. Older frames are stale.
                }

                pendingScreens.Enqueue(json);
            }
            else if (type == "ready")
            {
                EnqueueStatus("Connected: " + ExtractJsonString(json, "shell"));
            }
            else if (type == "exit")
            {
                EnqueueStatus("Exited");
            }
        }

        private void EnqueueStatus(string message)
        {
            pendingStatus.Enqueue(message);
        }

        private async Task SendJson(string json)
        {
            if (!IsConnected)
            {
                return;
            }

            var bytes = Encoding.UTF8.GetBytes(json);
            await socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, cancellation.Token);
        }

        private void DisposeSocket()
        {
            try
            {
                cancellation?.Cancel();
                socket?.Dispose();
            }
            catch
            {
                // Best effort cleanup.
            }
            finally
            {
                socket = null;
                cancellation?.Dispose();
                cancellation = null;
            }
        }

        private static string JsonString(string value)
        {
            var builder = new StringBuilder(value.Length + 2);
            builder.Append('"');
            foreach (var ch in value)
            {
                switch (ch)
                {
                    case '\\': builder.Append("\\\\"); break;
                    case '"': builder.Append("\\\""); break;
                    case '\b': builder.Append("\\b"); break;
                    case '\f': builder.Append("\\f"); break;
                    case '\n': builder.Append("\\n"); break;
                    case '\r': builder.Append("\\r"); break;
                    case '\t': builder.Append("\\t"); break;
                    default:
                        if (ch < 32)
                        {
                            builder.Append("\\u");
                            builder.Append(((int)ch).ToString("x4"));
                        }
                        else
                        {
                            builder.Append(ch);
                        }
                        break;
                }
            }
            builder.Append('"');
            return builder.ToString();
        }

        private static string Bool(bool value)
        {
            return value ? "true" : "false";
        }

        private static string ExtractJsonString(string json, string key)
        {
            var marker = "\"" + key + "\":";
            var index = json.IndexOf(marker, StringComparison.Ordinal);
            if (index < 0)
            {
                return string.Empty;
            }

            index += marker.Length;
            while (index < json.Length && char.IsWhiteSpace(json[index]))
            {
                index++;
            }

            if (index >= json.Length || json[index] != '"')
            {
                return string.Empty;
            }

            index++;
            var builder = new StringBuilder();
            while (index < json.Length)
            {
                var ch = json[index++];
                if (ch == '"')
                {
                    break;
                }

                if (ch == '\\' && index < json.Length)
                {
                    var escaped = json[index++];
                    switch (escaped)
                    {
                        case '"': builder.Append('"'); break;
                        case '\\': builder.Append('\\'); break;
                        case '/': builder.Append('/'); break;
                        case 'b': builder.Append('\b'); break;
                        case 'f': builder.Append('\f'); break;
                        case 'n': builder.Append('\n'); break;
                        case 'r': builder.Append('\r'); break;
                        case 't': builder.Append('\t'); break;
                        case 'u':
                            if (index + 4 <= json.Length)
                            {
                                var hex = json.Substring(index, 4);
                                if (int.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out var value))
                                {
                                    builder.Append((char)value);
                                }
                                index += 4;
                            }
                            break;
                        default:
                            builder.Append(escaped);
                            break;
                    }
                }
                else
                {
                    builder.Append(ch);
                }
            }

            return builder.ToString();
        }
    }
}
