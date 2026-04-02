using Newtonsoft.Json;

namespace MCPForUnity.Editor.Helpers
{
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
}
