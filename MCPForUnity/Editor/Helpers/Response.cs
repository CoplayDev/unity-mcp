using Newtonsoft.Json;

namespace MCPForUnity.Editor.Helpers
{
    public interface IMcpResponse
    {
        [JsonProperty("success")]
        bool Success { get; }
    }

    public sealed class SuccessResponse : IMcpResponse
    {
        [JsonProperty("success")]
        public bool Success => true;

        [JsonIgnore]
        public bool success => Success; // Backward-compatible casing for reflection-based tests

        [JsonProperty("message")]
        public string Message { get; }

        [JsonProperty("data", NullValueHandling = NullValueHandling.Ignore)]
        public object Data { get; }

        [JsonIgnore]
        public object data => Data;

        public SuccessResponse(string message, object data = null)
        {
            Message = message;
            Data = data;
        }
    }

    public sealed class ErrorResponse : IMcpResponse
    {
        [JsonProperty("success")]
        public bool Success => false;

        [JsonIgnore]
        public bool success => Success; // Backward-compatible casing for reflection-based tests

        [JsonProperty("code", NullValueHandling = NullValueHandling.Ignore)]
        public string Code { get; }

        [JsonIgnore]
        public string code => Code;

        [JsonProperty("error")]
        public string Error { get; }

        [JsonIgnore]
        public string error => Error;

        [JsonProperty("data", NullValueHandling = NullValueHandling.Ignore)]
        public object Data { get; }

        [JsonIgnore]
        public object data => Data;

        public ErrorResponse(string messageOrCode, object data = null)
        {
            Code = messageOrCode;
            Error = messageOrCode;
            Data = data;
        }
    }

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
