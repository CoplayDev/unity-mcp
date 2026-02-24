using System;
using System.IO;
using System.Reflection;
using MCPForUnity.Editor.Helpers;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

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
                    assetsPath = assetsPath,
                    renderPipeline = RenderPipelineUtility.GetActivePipeline().ToString(),
                    activeInputHandler = GetActiveInputHandler(),
                    packages = new
                    {
                        ugui = IsPackageInstalled("com.unity.ugui"),
                        textmeshpro = IsPackageInstalled("com.unity.textmeshpro"),
                        inputsystem = IsPackageInstalled("com.unity.inputsystem"),
                    }
                };

                return new SuccessResponse("Retrieved project info.", info);
            }
            catch (Exception e)
            {
                return new ErrorResponse($"Error getting project info: {e.Message}");
            }
        }

        /// <summary>
        /// Reads PlayerSettings.activeInputHandler via reflection to avoid
        /// compile-time dependency on the Input System package.
        /// Returns "Old" (0), "New" (1), or "Both" (2).
        /// </summary>
        private static string GetActiveInputHandler()
        {
            try
            {
                var prop = typeof(PlayerSettings).GetProperty(
                    "activeInputHandler",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);

                if (prop == null)
                    return "Old";

                int value = (int)prop.GetValue(null);
                return value switch
                {
                    0 => "Old",
                    1 => "New",
                    2 => "Both",
                    _ => "Old"
                };
            }
            catch
            {
                return "Old";
            }
        }

        private static bool IsPackageInstalled(string packageName)
        {
            try
            {
                return PackageInfo.FindForAssetPath("Packages/" + packageName) != null;
            }
            catch
            {
                return false;
            }
        }
    }
}
