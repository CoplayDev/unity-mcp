using Newtonsoft.Json;

namespace MCPForUnity.Editor.Helpers
{
    public interface IMcpResponse
    {
        [JsonProperty("success")]
        bool Success { get; }
    }
}
