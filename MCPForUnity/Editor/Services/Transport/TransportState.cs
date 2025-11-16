namespace MCPForUnity.Editor.Services.Transport
{
    /// <summary>
    /// Lightweight snapshot of a transport's runtime status for editor UI and diagnostics.
    /// </summary>
    public sealed class TransportState
    {
        public bool IsConnected { get; }
        public string TransportName { get; }
        public int? Port { get; }
        public string SessionId { get; }
        public string Details { get; }
        public string Error { get; }

        private TransportState(
            bool isConnected,
            string transportName,
            int? port,
            string sessionId,
            string details,
            string error)
        {
            IsConnected = isConnected;
            TransportName = transportName;
            Port = port;
            SessionId = sessionId;
            Details = details;
            Error = error;
        }

        public static TransportState Connected(
            string transportName,
            int? port = null,
            string sessionId = null,
            string details = null)
            => new TransportState(true, transportName, port, sessionId, details, null);

        public static TransportState Disconnected(
            string transportName,
            string error = null,
            int? port = null)
            => new TransportState(false, transportName, port, null, null, error);

        public TransportState WithError(string error) => new TransportState(
            IsConnected,
            TransportName,
            Port,
            SessionId,
            Details,
            error);
    }
}
