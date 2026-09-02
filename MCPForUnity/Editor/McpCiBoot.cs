using MCPForUnity.Editor.Services;
using MCPForUnity.Editor.Services.Transport.Transports;

namespace MCPForUnity.Editor
{
    public static class McpCiBoot
    {
        public static void StartStdioForCi()
        {
            // Session-scoped, not EditorPrefs: this must not rewrite the developer's real
            // transport preference, and it has to beat the value EditorConfigurationCache
            // already read at domain load, which HttpAutoStartHandler consults on its first tick.
            try
            {
                EditorConfigurationCache.Instance.PinStdioForSession();
            }
            catch { /* ignore */ }

            StdioBridgeHost.StartAutoConnect();
        }
    }
}
