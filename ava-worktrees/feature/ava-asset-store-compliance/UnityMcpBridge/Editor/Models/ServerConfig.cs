using System;
using Newtonsoft.Json;

namespace MCPForUnity.Editor.Models
{
    [Serializable]
    public class ServerConfig
    {
        [JsonProperty("unity_host")]
        public string unityHost = "localhost";

        [JsonProperty("unity_port")]
        public int unityPort;

        [JsonProperty("mcp_port")]
        public int mcpPort;

        [JsonProperty("connection_timeout")]
        public float connectionTimeout;

        [JsonProperty("buffer_size")]
        public int bufferSize;

        [JsonProperty("log_level")]
        public string logLevel;

        [JsonProperty("log_format")]
        public string logFormat;

        [JsonProperty("max_retries")]
        public int maxRetries;

        [JsonProperty("retry_delay")]
        public float retryDelay;
    }
}
