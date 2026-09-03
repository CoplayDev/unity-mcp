using System;
using MCPForUnity.Editor.Services;
using MCPForUnity.Editor.Services.Transport.Transports;

namespace MCPForUnity.Editor
{
    public static class McpCiBoot
    {
        public static void StartStdioForCi()
        {
            try 
            { 
                EditorConfigurationCache.Instance.SetUseHttpTransport(false);
            }
            catch { /* ignore */ }

            StdioBridgeHost.StartAutoConnect();
        }
    }
}
