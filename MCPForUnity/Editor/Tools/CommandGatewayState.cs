using MCPForUnity.Editor.Services;
using UnityEditor;

namespace MCPForUnity.Editor.Tools
{
    /// <summary>
    /// Singleton state for the command gateway queue.
    /// Hooks into EditorApplication.update for tick processing.
    /// </summary>
    [InitializeOnLoad]
    public static class CommandGatewayState
    {
        public static readonly CommandQueue Queue = new();

        static CommandGatewayState()
        {
            Queue.IsEditorBusy = () =>
                TestJobManager.HasRunningJob
                || EditorApplication.isCompiling;

            EditorApplication.update += OnUpdate;
        }

        static void OnUpdate()
        {
            Queue.ProcessTick(async (tool, @params) =>
                await CommandRegistry.InvokeCommandAsync(tool, @params));
        }
    }
}
