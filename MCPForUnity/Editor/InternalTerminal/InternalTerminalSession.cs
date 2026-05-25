using UnityEditor;

namespace WTL.InternalTerminal.Editor
{
    [InitializeOnLoad]
    internal static class InternalTerminalSession
    {
        private const string PortKey = "WTL.InternalTerminal.Port";
        private static readonly InternalTerminalBackend Backend = new InternalTerminalBackend();

        static InternalTerminalSession()
        {
            EditorApplication.quitting += Stop;
        }

        public static InternalTerminalBackend SharedBackend => Backend;

        public static bool IsRunning => Backend.IsRunning;
        public static string Url => Backend.Url;

        public static void Start()
        {
            Backend.Start();
            if (Backend.Port > 0)
            {
                SessionState.SetInt(PortKey, Backend.Port);
            }
        }

        public static bool TryReconnectKnownBackend()
        {
            if (Backend.IsRunning)
            {
                return true;
            }

            var port = SessionState.GetInt(PortKey, 0);
            return port > 0 && Backend.Attach(port);
        }

        public static void Stop()
        {
            SessionState.SetInt(PortKey, 0);
            Backend.Stop();
        }
    }
}
