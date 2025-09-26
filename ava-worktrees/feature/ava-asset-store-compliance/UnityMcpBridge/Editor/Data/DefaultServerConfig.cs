using MCPForUnity.Editor.Models;

namespace MCPForUnity.Editor.Data
{
    public class DefaultServerConfig : ServerConfig
    {
        public new string unityHost = "localhost";
        public new int unityPort = 6400;
        public new int mcpPort = 6500;
        public new float connectionTimeout = 15.0f;
        public new int bufferSize = 32768;
        public new string logLevel = "INFO";
        public new string logFormat = "%(asctime)s - %(name)s - %(levelname)s - %(message)s";
        public new int maxRetries = 3;
        public new float retryDelay = 1.0f;
    }
}

