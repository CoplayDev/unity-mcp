using System;
using System.IO;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace MCPForUnity.Editor.Helpers
{
    /// <summary>
    /// Provides common utility methods for working with Unity asset paths.
    /// </summary>
    public static class AssetPathUtility
    {
        /// <summary>
        /// Normalizes a Unity asset path by ensuring forward slashes are used and that it is rooted under "Assets/".
        /// </summary>
        public static string SanitizeAssetPath(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return path;
            }

            path = path.Replace('\\', '/');
            if (!path.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
            {
                return "Assets/" + path.TrimStart('/');
            }

            return path;
        }

        /// <summary>
        /// Gets the MCP for Unity package root path by finding a known script in the AssetDatabase.
        /// Works for both Package Manager (Packages/com.coplaydev.unity-mcp) and Asset Store (Assets/MCPForUnity) installations.
        /// </summary>
        /// <returns>The package root path, or null if not found</returns>
        public static string GetMcpPackageRootPath()
        {
            try
            {
                // Use a known type from the package to locate it
                string[] guids = AssetDatabase.FindAssets($"t:Script {nameof(AssetPathUtility)}");
                
                if (guids.Length == 0)
                {
                    Debug.LogWarning("Could not find AssetPathUtility script in AssetDatabase");
                    return null;
                }

                string scriptPath = AssetDatabase.GUIDToAssetPath(guids[0]);
                
                // Script is at: {packageRoot}/Editor/Helpers/AssetPathUtility.cs
                // We need to extract {packageRoot}
                int editorIndex = scriptPath.IndexOf("/Editor/", StringComparison.Ordinal);
                
                if (editorIndex >= 0)
                {
                    return scriptPath.Substring(0, editorIndex);
                }

                Debug.LogWarning($"Could not determine package root from script path: {scriptPath}");
                return null;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Failed to get package root path: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Reads and parses the package.json file for MCP for Unity.
        /// </summary>
        /// <returns>JObject containing package.json data, or null if not found or parse failed</returns>
        public static JObject GetPackageJson()
        {
            try
            {
                string packageRoot = GetMcpPackageRootPath();
                if (string.IsNullOrEmpty(packageRoot))
                {
                    return null;
                }

                string packageJsonPath = Path.Combine(packageRoot, "package.json");
                
                if (!File.Exists(packageJsonPath))
                {
                    Debug.LogWarning($"package.json not found at: {packageJsonPath}");
                    return null;
                }

                string json = File.ReadAllText(packageJsonPath);
                return JObject.Parse(json);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Failed to read or parse package.json: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Gets the version string from the package.json file.
        /// </summary>
        /// <returns>Version string, or "unknown" if not found</returns>
        public static string GetPackageVersion()
        {
            try
            {
                var packageJson = GetPackageJson();
                if (packageJson == null)
                {
                    return "unknown";
                }

                string version = packageJson["version"]?.ToString();
                return string.IsNullOrEmpty(version) ? "unknown" : version;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Failed to get package version: {ex.Message}");
                return "unknown";
            }
        }
    }
}
