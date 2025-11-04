using System;
using MCPForUnity.Editor.Helpers;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;

namespace MCPForUnity.Editor.Resources.Editor
{
    /// <summary>
    /// Provides dynamic editor state information that changes frequently.
    /// </summary>
    [McpForUnityResource("get_editor_state")]
    public static class EditorState
    {
        public static object HandleCommand(JObject @params)
        {
            try
            {
                var activeScene = EditorSceneManager.GetActiveScene();
                var state = new
                {
                    isPlaying = EditorApplication.isPlaying,
                    isPaused = EditorApplication.isPaused,
                    isCompiling = EditorApplication.isCompiling,
                    isUpdating = EditorApplication.isUpdating,
                    timeSinceStartup = EditorApplication.timeSinceStartup,
                    activeSceneName = activeScene.name ?? "",
                    selectionCount = UnityEditor.Selection.count,
                    activeObjectName = UnityEditor.Selection.activeObject?.name
                };
                
                return Response.Success("Retrieved editor state.", state);
            }
            catch (Exception e)
            {
                return Response.Error($"Error getting editor state: {e.Message}");
            }
        }
    }
}
