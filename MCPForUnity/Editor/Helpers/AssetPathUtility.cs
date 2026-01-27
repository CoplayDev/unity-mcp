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
        /// Normalizes path separators to forward slashes without modifying the path structure.
        /// Use this for non-asset paths (e.g., file system paths, relative directories).
        /// </summary>
        public static string NormalizeSeparators(string path)
        {
            if (string.IsNullOrEmpty(path))
                return path;
            return path.Replace('\\', '/');
        }

        /// <summary>
        /// Normalizes a Unity asset path by ensuring forward slashes are used and that it is rooted under "Assets/".
        /// Also protects against path traversal attacks using "../" sequences.
        /// </summary>
        public static string SanitizeAssetPath(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return path;
            }

            path = NormalizeSeparators(path);

            // Check for path traversal sequences
            if (path.Contains(".."))
            {
                McpLog.Warn($"[AssetPathUtility] Path contains potential traversal sequence: '{path}'");
                return null;
            }

            // Ensure path starts with Assets/
            if (!path.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
            {
                return "Assets/" + path.TrimStart('/');
            }

            return path;
        }

        /// <summary>
        /// Checks if a given asset path is valid and safe (no traversal, within Assets folder).
        /// </summary>
        /// <returns>True if the path is valid, false otherwise.</returns>
        public static bool IsValidAssetPath(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return false;
            }

            // Normalize for comparison
            string normalized = NormalizeSeparators(path);

            // Must start with Assets/
            if (!normalized.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            // Must not contain traversal sequences
            if (normalized.Contains(".."))
            {
                return false;
            }

            // Must not contain invalid path characters
            char[] invalidChars = { ':', '*', '?', '"', '<', '>', '|' };
            foreach (char c in invalidChars)
            {
                if (normalized.IndexOf(c) >= 0)
                {
                    return false;
                }
            }

            return true;
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
        /// Gets the package source for the MCP server (used with uvx --from).
        /// Checks for EditorPrefs override first (supports git URLs, file:// paths, etc.),
        /// then falls back to PyPI package reference.
        /// </summary>
        /// <returns>Package source string for uvx --from argument</returns>
        public static string GetMcpServerPackageSource()
        {
            // Check for override first (supports git URLs, file:// paths, local paths)
            string sourceOverride = EditorPrefs.GetString(EditorPrefKeys.GitUrlOverride, "");
            if (!string.IsNullOrEmpty(sourceOverride))
            {
                return sourceOverride;
            }

            // Default to PyPI package (avoids Windows long path issues with git clone)
            string version = GetPackageVersion();
            if (version == "unknown")
            {
                // Fall back to latest PyPI version so configs remain valid in test scenarios
                return "mcpforunityserver";
            }

            return $"mcpforunityserver=={version}";
        }

        /// <summary>
        /// Deprecated: Use GetMcpServerPackageSource() instead.
        /// Kept for backwards compatibility.
        /// </summary>
        [System.Obsolete("Use GetMcpServerPackageSource() instead")]
        public static string GetMcpServerGitUrl() => GetMcpServerPackageSource();

        /// <summary>
        /// Gets structured uvx command parts for different client configurations
        /// </summary>
        /// <returns>Tuple containing (uvxPath, fromUrl, packageName)</returns>
        public static (string uvxPath, string fromUrl, string packageName) GetUvxCommandParts()
        {
            string uvxPath = MCPServiceLocator.Paths.GetUvxPath();
            string fromUrl = GetMcpServerPackageSource();
            string packageName = "mcp-for-unity";

            return (uvxPath, fromUrl, packageName);
        }

        /// <summary>
        /// Builds the uvx package source arguments for the MCP server.
        /// Handles beta server mode (prerelease from PyPI) vs standard mode (pinned version or override).
        /// Centralizes the prerelease logic to avoid duplication between HTTP and stdio transports.
        /// Priority: explicit fromUrl override > beta server mode > default package.
        /// </summary>
        /// <param name="quoteFromPath">Whether to quote the --from path (needed for command-line strings, not for arg lists)</param>
        /// <returns>The package source arguments (e.g., "--prerelease explicit --from mcpforunityserver>=0.0.0a0")</returns>
        public static string GetBetaServerFromArgs(bool quoteFromPath = false)
        {
            // Explicit override (local path, git URL, etc.) always wins
            string fromUrl = GetMcpServerPackageSource();
            string overrideUrl = EditorPrefs.GetString(EditorPrefKeys.GitUrlOverride, "");
            if (!string.IsNullOrEmpty(overrideUrl))
            {
                return $"--from {fromUrl}";
            }

            // Beta server mode: use prerelease from PyPI
            bool useBetaServer = EditorPrefs.GetBool(EditorPrefKeys.UseBetaServer, true);
            if (useBetaServer)
            {
                // Use --prerelease explicit with version specifier to only get prereleases of our package,
                // not of dependencies (which can be broken on PyPI).
                string fromValue = quoteFromPath ? "\"mcpforunityserver>=0.0.0a0\"" : "mcpforunityserver>=0.0.0a0";
                return $"--prerelease explicit --from {fromValue}";
            }

            // Standard mode: use pinned version from package.json
            if (!string.IsNullOrEmpty(fromUrl))
            {
                return $"--from {fromUrl}";
            }

            return string.Empty;
        }

        /// <summary>
        /// Builds the uvx package source arguments as a list (for JSON config builders).
        /// Priority: explicit fromUrl override > beta server mode > default package.
        /// </summary>
        /// <returns>List of arguments to add to uvx command</returns>
        public static System.Collections.Generic.IList<string> GetBetaServerFromArgsList()
        {
            var args = new System.Collections.Generic.List<string>();

            // Explicit override (local path, git URL, etc.) always wins
            string fromUrl = GetMcpServerPackageSource();
            string overrideUrl = EditorPrefs.GetString(EditorPrefKeys.GitUrlOverride, "");
            if (!string.IsNullOrEmpty(overrideUrl))
            {
                args.Add("--from");
                args.Add(fromUrl);
                return args;
            }

            // Beta server mode: use prerelease from PyPI
            bool useBetaServer = EditorPrefs.GetBool(EditorPrefKeys.UseBetaServer, true);
            if (useBetaServer)
            {
                args.Add("--prerelease");
                args.Add("explicit");
                args.Add("--from");
                args.Add("mcpforunityserver>=0.0.0a0");
                return args;
            }

            // Standard mode: use pinned version from package.json
            if (!string.IsNullOrEmpty(fromUrl))
            {
                args.Add("--from");
                args.Add(fromUrl);
            }

            return args;
        }

        /// <summary>
        /// Determines whether uvx should use --no-cache --refresh flags.
        /// Returns true if DevModeForceServerRefresh is enabled OR if the server URL is a local path.
        /// Local paths (file:// or absolute) always need fresh builds to avoid stale uvx cache.
        /// Note: --reinstall is not supported by uvx and will cause a warning.
        /// </summary>
        public static bool ShouldForceUvxRefresh()
        {
            bool devForceRefresh = false;
            try { devForceRefresh = EditorPrefs.GetBool(EditorPrefKeys.DevModeForceServerRefresh, false); } catch { }

            if (devForceRefresh)
                return true;

            // Auto-enable force refresh when using a local path override.
            return IsLocalServerPath();
        }

        /// <summary>
        /// Returns true if the server URL is a local path (file:// or absolute path).
        /// </summary>
        public static bool IsLocalServerPath()
        {
            string fromUrl = GetMcpServerPackageSource();
            if (string.IsNullOrEmpty(fromUrl))
                return false;

            // Check for file:// protocol or absolute local path
            return fromUrl.StartsWith("file://", StringComparison.OrdinalIgnoreCase) ||
                   System.IO.Path.IsPathRooted(fromUrl);
        }

        /// <summary>
        /// Gets the local server path if GitUrlOverride points to a local directory.
        /// Returns null if not using a local path.
        /// </summary>
        public static string GetLocalServerPath()
        {
            if (!IsLocalServerPath())
                return null;

            string fromUrl = GetMcpServerPackageSource();
            if (fromUrl.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
            {
                // Strip file:// prefix
                fromUrl = fromUrl.Substring(7);
            }

            return fromUrl;
        }

        /// <summary>
        /// Cleans stale Python build artifacts from the local server path.
        /// This is necessary because Python's build system doesn't remove deleted files from build/,
        /// and the auto-discovery mechanism will pick up old .py files causing ghost resources/tools.
        /// </summary>
        /// <returns>True if cleaning was performed, false if not applicable or failed.</returns>
        public static bool CleanLocalServerBuildArtifacts()
        {
            string localPath = GetLocalServerPath();
            if (string.IsNullOrEmpty(localPath))
                return false;

            // Clean the build/ directory which can contain stale .py files
            string buildPath = System.IO.Path.Combine(localPath, "build");
            if (System.IO.Directory.Exists(buildPath))
            {
                try
                {
                    System.IO.Directory.Delete(buildPath, recursive: true);
                    McpLog.Info($"Cleaned stale build artifacts from: {buildPath}");
                    return true;
                }
                catch (Exception ex)
                {
                    McpLog.Warn($"Failed to clean build artifacts: {ex.Message}");
                    return false;
                }
            }

            return false;
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
