using Newtonsoft.Json;

namespace MCPForUnity.Editor.Helpers
{
    public sealed class PendingResponse : IMcpResponse
    {
        [JsonProperty("success")]
        public bool Success => true;

        [JsonIgnore]
        public bool success => Success; // Backward-compatible casing for reflection-based tests

        [JsonProperty("_mcp_status")]
        public string Status => "pending";

        [JsonIgnore]
        public string _mcp_status => Status;

        [JsonProperty("_mcp_poll_interval")]
        public double PollIntervalSeconds { get; }

        [JsonIgnore]
        public double _mcp_poll_interval => PollIntervalSeconds;

        [JsonProperty("message", NullValueHandling = NullValueHandling.Ignore)]
        public string Message { get; }

        [JsonIgnore]
        public string message => Message;

        [JsonProperty("data", NullValueHandling = NullValueHandling.Ignore)]
        public object Data { get; }

        [JsonIgnore]
        public object data => Data;

        public PendingResponse(string message = "", double pollIntervalSeconds = 1.0, object data = null)
        {
            Message = string.IsNullOrEmpty(message) ? null : message;
            PollIntervalSeconds = pollIntervalSeconds;
            Data = data;
        }
    }
}
