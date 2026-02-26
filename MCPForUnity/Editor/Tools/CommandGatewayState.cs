using MCPForUnity.Editor.Services;
using UnityEditor;

namespace MCPForUnity.Editor.Tools
{
    /// <summary>
    /// Singleton state for the command gateway queue.
    /// Hooks into EditorApplication.update for tick processing.
    /// Persists queue state across domain reloads via SessionState.
    /// </summary>
    [InitializeOnLoad]
    public static class CommandGatewayState
    {
        const string SessionKey = "MCPForUnity.GatewayQueueV1";

        public static readonly CommandQueue Queue = new();

        static CommandGatewayState()
        {
            // Restore queue state from previous domain reload
            string json = SessionState.GetString(SessionKey, "");
            if (!string.IsNullOrEmpty(json))
                Queue.RestoreFromJson(json);

            Queue.IsEditorBusy = () =>
                TestJobManager.HasRunningJob
                || TestRunStatus.IsRunning
                || EditorApplication.isCompiling;

            // Persist before next domain reload
            AssemblyReloadEvents.beforeAssemblyReload += () =>
                SessionState.SetString(SessionKey, Queue.PersistToJson());

            EditorApplication.update += OnUpdate;
        }

        static void OnUpdate()
        {
            Queue.ProcessTick(async (tool, @params) =>
                await CommandRegistry.InvokeCommandAsync(tool, @params));
        }
    }
}
