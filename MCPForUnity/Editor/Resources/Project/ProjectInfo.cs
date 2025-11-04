using System;
using System.IO;
using MCPForUnity.Editor.Helpers;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace MCPForUnity.Editor.Resources.Project
{
    /// <summary>
    /// Provides static project configuration information.
    /// </summary>
    [McpForUnityResource("get_project_info")]
    public static class ProjectInfo
    {
        public static object HandleCommand(JObject @params)
        {
            try
            {
                string assetsPath = Application.dataPath.Replace('\\', '/');
                string projectRoot = Directory.GetParent(assetsPath)?.FullName.Replace('\\', '/');
                string projectName = Path.GetFileName(projectRoot);
                
                var info = new
                {
                    projectRoot = projectRoot ?? "",
                    projectName = projectName ?? "",
                    unityVersion = Application.unityVersion,
                    platform = EditorUserBuildSettings.activeBuildTarget.ToString(),
                    assetsPath = assetsPath
                };
                
                return Response.Success("Retrieved project info.", info);
            }
            catch (Exception e)
            {
                return Response.Error($"Error getting project info: {e.Message}");
            }
        }
    }
}
