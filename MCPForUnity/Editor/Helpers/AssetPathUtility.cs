using System;
using System.IO;
using MCPForUnity.Editor.Constants;
using MCPForUnity.Editor.Services;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

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
        /// Gets the absolute file system path to the package root.
        /// Uses StackTrace/ScriptableObject trick to resolve real path (bypassing PackageCache if symlinked).
        /// </summary>
        public static string GetPackageAbsolutePath()
        {
            // 1. Try to find path relative to this script file (Most reliable for local dev & mono scripts)
            try 
            {
                // Get the path of THIS source file during execution
                string scriptFilePath = GetCallerFilePath();
                if (!string.IsNullOrEmpty(scriptFilePath) && File.Exists(scriptFilePath))
                {
                    // Current file: .../MCPForUnity/Editor/Helpers/AssetPathUtility.cs
                    // We need: .../MCPForUnity
                    return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(scriptFilePath), "..", ".."));
                }
            }
            catch (Exception ex)
            {
                McpLog.Warn($"Failed to resolve path from stack trace: {ex.Message}");
            }

            // 2. Fallback to standard Unity PackageInfo (Reliable for Registry/Git packages)
            string packageRoot = GetMcpPackageRootPath();
            if (string.IsNullOrEmpty(packageRoot)) return null;

            // If it's already an absolute path, we're good
            if (Path.IsPathRooted(packageRoot))
            {
                return packageRoot;
            }

            // If it's a virtual path (Packages/...), resolve it to physical path
            if (packageRoot.StartsWith("Packages/", StringComparison.OrdinalIgnoreCase))
            {
                var packageInfo = PackageInfo.FindForAssembly(typeof(AssetPathUtility).Assembly);
                if (packageInfo != null && !string.IsNullOrEmpty(packageInfo.resolvedPath))
                {
                    return packageInfo.resolvedPath;
                }
                
                // If resolvedPath is failing but we have assetPath, try Path.GetFullPath
                // Note: This rarely works for Library/PackageCache, but worth a shot for local tarballs
                return Path.GetFullPath(packageRoot);
            }
            else if (packageRoot.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
            {
                string relativePath = packageRoot.Substring("Assets/".Length);
                return Path.Combine(Application.dataPath, relativePath);
            }
            
            return Path.GetFullPath(packageRoot);
        }

        private static string GetCallerFilePath([System.Runtime.CompilerServices.CallerFilePath] string sourceFilePath = "")
        {
            return sourceFilePath;
        }

        /// <summary>
        /// Gets the absolute path to the wrapper.js file in the package.
        /// </summary>
        /// <returns>Absolute path to wrapper.js, or null if not found</returns>
        public static string GetWrapperJsPath()
        {
            string packageRoot = GetMcpPackageRootPath();
            if (string.IsNullOrEmpty(packageRoot))
            {
                return null;
            }

            // wrapper.js is expected to be in {packageRoot}/Server~/wrapper.js
            // But we need to handle virtual paths if consistent with GetPackageJson logic

            string wrapperPath;

            // Convert virtual asset path to file system path (similar logic to GetPackageJson)
            if (packageRoot.StartsWith("Packages/", StringComparison.OrdinalIgnoreCase))
            {
                var packageInfo = PackageInfo.FindForAssembly(typeof(AssetPathUtility).Assembly);
                if (packageInfo != null && !string.IsNullOrEmpty(packageInfo.resolvedPath))
                {
                    wrapperPath = Path.Combine(packageInfo.resolvedPath, "Server~", "wrapper.js");
                }
                else
                {
                    return null;
                }
            }
            else if (packageRoot.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
            {
                string relativePath = packageRoot.Substring("Assets/".Length);
                wrapperPath = Path.Combine(Application.dataPath, relativePath, "Server~", "wrapper.js");
            }
            else
            {
                // Already absolute or unknown
                wrapperPath = Path.Combine(packageRoot, "Server~", "wrapper.js");
            }

            if (File.Exists(wrapperPath))
            {
                return wrapperPath;
            }
            
            // Also check without the ~ just in case
            string wrapperPathNoTilde = Path.Combine(Path.GetDirectoryName(wrapperPath), "..", "Server", "wrapper.js");
            if (File.Exists(wrapperPathNoTilde))
            {
                return Path.GetFullPath(wrapperPathNoTilde);
            }

            return null;
        }

        /// <summary>
        /// Gets the MCP for Unity package root path.
        /// Works for registry Package Manager, local Package Manager, and Asset Store installations.
        /// </summary>
        /// <returns>The package root path (virtual for PM, absolute for Asset Store), or null if not found</returns>
        public static string GetMcpPackageRootPath()
        {
            try
            {
                // Try Package Manager first (registry and local installs)
                var packageInfo = PackageInfo.FindForAssembly(typeof(AssetPathUtility).Assembly);
                if (packageInfo != null && !string.IsNullOrEmpty(packageInfo.assetPath))
                {
                    return packageInfo.assetPath;
                }

                // Fallback to AssetDatabase for Asset Store installs (Assets/MCPForUnity)
                string[] guids = AssetDatabase.FindAssets($"t:Script {nameof(AssetPathUtility)}");

                if (guids.Length == 0)
                {
                    McpLog.Warn("Could not find AssetPathUtility script in AssetDatabase");
                    return null;
                }

                string scriptPath = AssetDatabase.GUIDToAssetPath(guids[0]);

                // Script is at: {packageRoot}/Editor/Helpers/AssetPathUtility.cs
                // Extract {packageRoot}
                int editorIndex = scriptPath.IndexOf("/Editor/", StringComparison.Ordinal);

                if (editorIndex >= 0)
                {
                    return scriptPath.Substring(0, editorIndex);
                }

                McpLog.Warn($"Could not determine package root from script path: {scriptPath}");
                return null;
            }
            catch (Exception ex)
            {
                McpLog.Error($"Failed to get package root path: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Reads and parses the package.json file for MCP for Unity.
        /// Handles both Package Manager (registry/local) and Asset Store installations.
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

                // Convert virtual asset path to file system path
                if (packageRoot.StartsWith("Packages/", StringComparison.OrdinalIgnoreCase))
                {
                    // Package Manager install - must use PackageInfo.resolvedPath
                    // Virtual paths like "Packages/..." don't work with File.Exists()
                    // Registry packages live in Library/PackageCache/package@version/
                    var packageInfo = PackageInfo.FindForAssembly(typeof(AssetPathUtility).Assembly);
                    if (packageInfo != null && !string.IsNullOrEmpty(packageInfo.resolvedPath))
                    {
                        packageJsonPath = Path.Combine(packageInfo.resolvedPath, "package.json");
                    }
                    else
                    {
                        McpLog.Warn("Could not resolve Package Manager path for package.json");
                        return null;
                    }
                }
                else if (packageRoot.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
                {
                    // Asset Store install - convert to absolute file system path
                    // Application.dataPath is the absolute path to the Assets folder
                    string relativePath = packageRoot.Substring("Assets/".Length);
                    packageJsonPath = Path.Combine(Application.dataPath, relativePath, "package.json");
                }

                if (!File.Exists(packageJsonPath))
                {
                    McpLog.Warn($"package.json not found at: {packageJsonPath}");
                    return null;
                }

                string json = File.ReadAllText(packageJsonPath);
                return JObject.Parse(json);
            }
            catch (Exception ex)
            {
                McpLog.Warn($"Failed to read or parse package.json: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Gets just the git URL part for the MCP server package
        /// Checks for EditorPrefs override first, then falls back to package version
        /// </summary>
        /// <returns>Git URL string, or empty string if version is unknown and no override</returns>
        public static string GetMcpServerGitUrl()
        {
            // Check for Git URL override first (still useful for forcing a specific repo)
            string gitUrlOverride = EditorPrefs.GetString(EditorPrefKeys.GitUrlOverride, "");
            if (!string.IsNullOrEmpty(gitUrlOverride))
            {
                return gitUrlOverride;
            }

            // Return official repository URL as default
            // Users can override via EditorPrefs.GitUrlOverride for development/testing
            return "git+https://github.com/CoplayDev/unity-mcp.git@main#subdirectory=MCPForUnity/Server~";
            
            /* 
            // Local path logic disabled for clean config aesthetics
            string packageRoot = GetPackageAbsolutePath();
            if (!string.IsNullOrEmpty(packageRoot))
            {
                // The server code is in {packageRoot}/Server~ or {packageRoot}/Server
                // Try Server~ first (Unity hidden folder)
                string serverPathTilde = Path.Combine(packageRoot, "Server~");
                if (Directory.Exists(serverPathTilde))
                {
                    return serverPathTilde;
                }

                string serverPath = Path.Combine(packageRoot, "Server");
                return serverPath;
            }

            // Fallback to git URL if local path cannot be determined
            // Note: pip/uv uses '#subdirectory=' syntax, not '?path='
            return "git+https://github.com/CoplayDev/unity-mcp.git@v{version}#subdirectory=MCPForUnity/Server~";
            */
        }

        /// <summary>
        /// Gets structured uvx command parts for different client configurations
        /// </summary>
        /// <returns>Tuple containing (uvxPath, fromUrl, packageName)</returns>
        public static (string uvxPath, string fromUrl, string packageName) GetUvxCommandParts()
        {
            string uvxPath = MCPServiceLocator.Paths.GetUvxPath();
            string fromUrl = GetMcpServerGitUrl();
            string packageName = "mcp-for-unity";

            return (uvxPath, fromUrl, packageName);
        }

        /// <summary>
        /// Gets the package version from package.json
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
                McpLog.Warn($"Failed to get package version: {ex.Message}");
                return "unknown";
            }
        }
    }
}
