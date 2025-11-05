using System;
using MCPForUnity.Editor.Helpers;
using Newtonsoft.Json.Linq;
using UnityEditor.SceneManagement;

namespace MCPForUnity.Editor.Resources.Editor
{
    /// <summary>
    /// Provides information about the current prefab editing context.
    /// </summary>
    [McpForUnityResource("get_prefab_stage")]
    public static class PrefabStage
    {
        public static object HandleCommand(JObject @params)
        {
            try
            {
                var stage = PrefabStageUtility.GetCurrentPrefabStage();
                
                if (stage == null)
                {
                    return Response.Success("No prefab stage is currently open.", new { isOpen = false });
                }

                var stageInfo = new
                {
                    isOpen = true,
                    assetPath = stage.assetPath,
                    prefabRootName = stage.prefabContentsRoot?.name,
                    mode = stage.mode.ToString(),
                    isDirty = stage.scene.isDirty
                };

                return Response.Success("Prefab stage info retrieved.", stageInfo);
            }
            catch (Exception e)
            {
                return Response.Error($"Error getting prefab stage info: {e.Message}");
            }
        }
    }
}
