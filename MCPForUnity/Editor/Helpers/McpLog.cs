using UnityEditor;
using UnityEngine;

namespace MCPForUnity.Editor.Helpers
{
    internal static class McpLog
    {
        private const string LogPrefix = "<b><color=#2EA3FF>MCP-FOR-UNITY</color></b>:";
        private const string WarnPrefix = "<b><color=#cc7a00>MCP-FOR-UNITY</color></b>:";
        private const string ErrorPrefix = "<b><color=#cc3333>MCP-FOR-UNITY</color></b>:";

        private static bool IsDebugEnabled()
        {
            try { return EditorPrefs.GetBool("MCPForUnity.DebugLogs", false); } catch { return false; }
        }

        public static void Info(string message, bool always = true)
        {
            if (!always && !IsDebugEnabled()) return;
            Debug.Log($"{LogPrefix} {message}");
        }

        public static void Warn(string message)
        {
            Debug.LogWarning($"{WarnPrefix} {message}");
        }

        public static void Error(string message)
        {
            Debug.LogError($"{ErrorPrefix} {message}");
        }
    }
}
