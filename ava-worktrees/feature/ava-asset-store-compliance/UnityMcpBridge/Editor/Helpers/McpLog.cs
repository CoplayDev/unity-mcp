using UnityEditor;
using UnityEngine;

namespace MCPForUnity.Editor.Helpers
{
    internal static class McpLog
    {
        private const string Prefix = "<b><color=#2EA3FF>MCP-FOR-UNITY</color></b>:";

        private static bool IsDebugEnabled()
        {
            try { return EditorPrefs.GetBool("MCPForUnity.DebugLogs", false); } catch { return false; }
        }

        public static void Info(string message, bool always = true)
        {
            if (!always && !IsDebugEnabled()) return;
            Debug.Log($"{Prefix} {message}");
        }

        public static void Warn(string message)
        {
            Debug.LogWarning($"<color=#cc7a00>{Prefix} {message}</color>");
        }

        public static void Error(string message)
        {
            Debug.LogError($"<color=#cc3333>{Prefix} {message}</color>");
        }
    }
}


