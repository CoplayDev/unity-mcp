using UnityEditor;
using UnityEngine;

namespace AssetStoreTools.Utility
{
    internal static class ASDebug
    {
        private enum LogType
        {
            Info,
            Warning,
            Error
        }

        private static string FormatInfo(object message) => $"<b>[AST Info]</b> {message}";
        private static string FormatWarning(object message) => $"<color=yellow><b>[AST Warning]</b></color> {message}";
        private static string FormatError(object message) => $"<color=red><b>[AST Error]</b></color> {message}";


        private static bool s_debugModeEnabled = EditorPrefs.GetBool(Constants.Debug.DebugModeKey);

        public static bool DebugModeEnabled
        {
            get => s_debugModeEnabled;
            set { s_debugModeEnabled = value; EditorPrefs.SetBool(Constants.Debug.DebugModeKey, value); }
        }

        public static void Log(object message)
        {
            LogMessage(message, LogType.Info);
        }

        public static void LogWarning(object message)
        {
            LogMessage(message, LogType.Warning);
        }

        public static void LogError(object message)
        {
            LogMessage(message, LogType.Error);
        }

        private static void LogMessage(object message, LogType type)
        {
            if (!DebugModeEnabled)
                return;

            switch (type)
            {
                case LogType.Info:
                    Debug.Log(FormatInfo(message));
                    break;
                case LogType.Warning:
                    Debug.LogWarning(FormatWarning(message));
                    break;
                case LogType.Error:
                    Debug.LogError(FormatError(message));
                    break;
                default:
                    Debug.Log(message);
                    break;
            }
        }
    }
}
