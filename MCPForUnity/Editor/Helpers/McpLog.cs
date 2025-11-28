using MCPForUnity.Editor.Constants;
using UnityEditor;
using UnityEngine;

namespace MCPForUnity.Editor.Helpers
{
    internal static class McpLog
    {
        private const string InfoPrefix = "<b><color=#2EA3FF>MCP-FOR-UNITY</color></b>:";
        private const string DebugPrefix = "<b><color=#6AA84F>MCP-FOR-UNITY</color></b>:";
        private const string WarnPrefix = "<b><color=#cc7a00>MCP-FOR-UNITY</color></b>:";
        private const string ErrorPrefix = "<b><color=#cc3333>MCP-FOR-UNITY</color></b>:";

        private static volatile bool _debugEnabled = ReadDebugPreference();

        private static bool IsDebugEnabled() => _debugEnabled;

        private static bool ReadDebugPreference()
        {
            try { return EditorPrefs.GetBool(EditorPrefKeys.DebugLogs, false); }
            catch { return false; }
        }

        public static void SetDebugLoggingEnabled(bool enabled)
        {
            _debugEnabled = enabled;
            try { EditorPrefs.SetBool(EditorPrefKeys.DebugLogs, enabled); }
            catch { }
        }

        public static void Debug(string message)
        {
            if (!IsDebugEnabled()) return;
            UnityEngine.Debug.Log($"{DebugPrefix} {message}");
        }

        public static void Info(string message, bool always = true)
        {
            if (!always && !IsDebugEnabled()) return;
            UnityEngine.Debug.Log($"{InfoPrefix} {message}");
        }

        public static void Warn(string message)
        {
            UnityEngine.Debug.LogWarning($"{WarnPrefix} {message}");
        }

        public static void Error(string message)
        {
            UnityEngine.Debug.LogError($"{ErrorPrefix} {message}");
        }
    }
}
