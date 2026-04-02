using Newtonsoft.Json;

namespace MCPForUnity.Editor.Helpers
{
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
}
