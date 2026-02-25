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
            EditorApplication.update += OnUpdate;
        }

        static void OnUpdate()
        {
            Queue.ProcessTick(async (tool, @params) =>
                await CommandRegistry.InvokeCommandAsync(tool, @params));
        }
    }
}
