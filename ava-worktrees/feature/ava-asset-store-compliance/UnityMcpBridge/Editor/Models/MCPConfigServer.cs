using System;
using Newtonsoft.Json;

namespace MCPForUnity.Editor.Models
{
    [Serializable]
    public class McpConfigServer
    {
        [JsonProperty("command")]
        public string command;

        [JsonProperty("args")]
        public string[] args;

        // VSCode expects a transport type; include only when explicitly set
        [JsonProperty("type", NullValueHandling = NullValueHandling.Ignore)]
        public string type;
    }
}
