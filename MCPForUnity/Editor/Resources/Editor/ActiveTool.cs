using System;
using MCPForUnity.Editor.Helpers;
using Newtonsoft.Json.Linq;
using UnityEditor;

namespace MCPForUnity.Editor.Resources.Editor
{
    /// <summary>
    /// Provides information about the currently active editor tool.
    /// </summary>
    [McpForUnityResource("get_active_tool")]
    public static class ActiveTool
    {
        public static object HandleCommand(JObject @params)
        {
            try
            {
                Tool currentTool = UnityEditor.Tools.current;
                string toolName = currentTool.ToString();
                bool customToolActive = UnityEditor.Tools.current == Tool.Custom;
                string activeToolName = customToolActive ? EditorTools.GetActiveToolName() : toolName;

                var toolInfo = new
                {
                    activeTool = activeToolName,
                    isCustom = customToolActive,
                    pivotMode = UnityEditor.Tools.pivotMode.ToString(),
                    pivotRotation = UnityEditor.Tools.pivotRotation.ToString(),
                    handleRotation = new
                    {
                        x = UnityEditor.Tools.handleRotation.eulerAngles.x,
                        y = UnityEditor.Tools.handleRotation.eulerAngles.y,
                        z = UnityEditor.Tools.handleRotation.eulerAngles.z
                    },
                    handlePosition = new
                    {
                        x = UnityEditor.Tools.handlePosition.x,
                        y = UnityEditor.Tools.handlePosition.y,
                        z = UnityEditor.Tools.handlePosition.z
                    }
                };

                return Response.Success("Retrieved active tool information.", toolInfo);
            }
            catch (Exception e)
            {
                return Response.Error($"Error getting active tool: {e.Message}");
            }
        }
    }

    // Helper class for custom tool names
    internal static class EditorTools
    {
        public static string GetActiveToolName()
        {
            if (UnityEditor.Tools.current == Tool.Custom)
            {
                return "Unknown Custom Tool";
            }
            return UnityEditor.Tools.current.ToString();
        }
    }
}
