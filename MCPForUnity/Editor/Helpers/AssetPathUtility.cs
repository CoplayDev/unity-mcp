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
        /// When the override is a local path, auto-corrects to the "Server" subdirectory
        /// if the path doesn't contain pyproject.toml but Server/pyproject.toml exists.
        /// </summary>
        /// <returns>Package source string for uvx --from argument</returns>
        public static string GetMcpServerPackageSource()
        {
            // Check for override first (supports git URLs, file:// paths, local paths)
            string sourceOverride = EditorPrefs.GetString(EditorPrefKeys.GitUrlOverride, "");
            if (!string.IsNullOrEmpty(sourceOverride))
            {
                string resolved = ResolveLocalServerPath(sourceOverride);
                // Persist the corrected path so future reads are consistent
                if (resolved != sourceOverride)
                {
                    EditorPrefs.SetString(EditorPrefKeys.GitUrlOverride, resolved);
                    McpLog.Info($"Auto-corrected server source override from '{sourceOverride}' to '{resolved}'");
                }
                return resolved;
            }

            // Default to PyPI package (avoids Windows long path issues with git clone)
            string version = GetPackageVersion();
            if (version == "unknown")
            {
                // Fall back to latest PyPI version so configs remain valid in test scenarios
                return "mcpforunityserver";
            }

            // Package.json uses semver prerelease tags (e.g., 9.4.5-beta.1) that are not valid
            // PEP 440 pins for uvx. Use the beta prerelease range instead of a pinned prerelease.
            if (IsSemVerPreRelease(version))
            {
                return "mcpforunityserver>=0.0.0a0";
            }

            return $"mcpforunityserver=={version}";
        }

        /// <summary>
        /// Validates and auto-corrects a local server source path to ensure it points to the
        /// directory containing pyproject.toml. If the path points to a parent directory
        /// (e.g. the repo root "unity-mcp") instead of the Python package directory ("Server"),
        /// this checks for a "Server" subdirectory with pyproject.toml and returns that path.
        /// Non-local paths (URLs, PyPI references) are returned unchanged.
        /// </summary>
        internal static string ResolveLocalServerPath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return path;

            // Skip non-local paths (git URLs, PyPI package names, etc.)
            if (path.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("git+", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("ssh://", StringComparison.OrdinalIgnoreCase))
            {
                return path;
            }

            // If it looks like a PyPI package reference (no path separators), skip
            if (!path.Contains('/') && !path.Contains('\\') && !path.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
            {
                return path;
            }

            // Strip file:// prefix for filesystem checks, preserve for return value
            string checkPath = path;
            string prefix = string.Empty;
            if (checkPath.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
            {
                prefix = checkPath.Substring(0, 7); // preserve original casing
                checkPath = checkPath.Substring(7);
            }

            // Already correct — pyproject.toml exists at this path
            if (System.IO.File.Exists(System.IO.Path.Combine(checkPath, "pyproject.toml")))
            {
                return path;
            }

            // Check if "Server" subdirectory contains pyproject.toml
            string serverSubDir = System.IO.Path.Combine(checkPath, "Server");
            if (System.IO.File.Exists(System.IO.Path.Combine(serverSubDir, "pyproject.toml")))
            {
                return prefix + serverSubDir;
            }

            // Return as-is; uvx will report the error if the path is truly invalid
            return path;
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
        /// Handles prerelease package mode (prerelease from PyPI) vs stable mode (pinned version or override).
        /// Centralizes the prerelease logic to avoid duplication between HTTP and stdio transports.
        /// Priority: explicit fromUrl override > package-version-driven prerelease mode > stable pinned package.
        /// NOTE: This overload reads from EditorPrefs/cache and MUST be called from the main thread.
        /// For background threads, use the overload that accepts pre-captured parameters.
        /// </summary>
        /// <param name="quoteFromPath">Whether to quote the --from path (needed for command-line strings, not for arg lists)</param>
        /// <returns>The package source arguments (e.g., "--prerelease explicit --from mcpforunityserver>=0.0.0a0")</returns>
        public static string GetBetaServerFromArgs(bool quoteFromPath = false)
        {
            string gitUrlOverride = EditorPrefs.GetString(EditorPrefKeys.GitUrlOverride, "");
            string packageSource = GetMcpServerPackageSource();
            return GetBetaServerFromArgs(gitUrlOverride, packageSource, quoteFromPath);
        }

        /// <summary>
        /// Thread-safe overload that accepts pre-captured values.
        /// Use this when calling from background threads.
        /// </summary>
        /// <param name="gitUrlOverride">Pre-captured value from EditorPrefs GitUrlOverride</param>
        /// <param name="packageSource">Pre-captured value from GetMcpServerPackageSource()</param>
        /// <param name="quoteFromPath">Whether to quote the --from path</param>
        public static string GetBetaServerFromArgs(string gitUrlOverride, string packageSource, bool quoteFromPath = false)
        {
            // Explicit override (local path, git URL, etc.) always wins
            if (!string.IsNullOrEmpty(gitUrlOverride))
            {
                string fromValue = quoteFromPath ? $"\"{gitUrlOverride}\"" : gitUrlOverride;
                return $"--from {fromValue}";
            }

            bool usePrereleaseRange = string.Equals(packageSource, "mcpforunityserver>=0.0.0a0", StringComparison.OrdinalIgnoreCase);

            // Prerelease package mode: use prerelease from PyPI.
            if (usePrereleaseRange)
            {
                // Use --prerelease explicit with version specifier to only get prereleases of our package,
                // not of dependencies (which can be broken on PyPI).
                string fromValue = quoteFromPath ? "\"mcpforunityserver>=0.0.0a0\"" : "mcpforunityserver>=0.0.0a0";
                return $"--prerelease explicit --from {fromValue}";
            }

            // Standard mode: use pinned version from package.json
            if (!string.IsNullOrEmpty(packageSource))
            {
                string fromValue = quoteFromPath ? $"\"{packageSource}\"" : packageSource;
                return $"--from {fromValue}";
            }

            return string.Empty;
        }

        /// <summary>
        /// Builds the uvx package source arguments as a list (for JSON config builders).
        /// Priority: explicit fromUrl override > package-version-driven prerelease mode > stable pinned package.
        /// NOTE: This overload reads from EditorPrefs/cache and MUST be called from the main thread.
        /// For background threads, use the overload that accepts pre-captured parameters.
        /// </summary>
        /// <returns>List of arguments to add to uvx command</returns>
        public static System.Collections.Generic.IList<string> GetBetaServerFromArgsList()
        {
            string gitUrlOverride = EditorPrefs.GetString(EditorPrefKeys.GitUrlOverride, "");
            string packageSource = GetMcpServerPackageSource();
            return GetBetaServerFromArgsList(gitUrlOverride, packageSource);
        }

        /// <summary>
        /// Thread-safe overload that accepts pre-captured values.
        /// Use this when calling from background threads.
        /// </summary>
        /// <param name="gitUrlOverride">Pre-captured value from EditorPrefs GitUrlOverride</param>
        /// <param name="packageSource">Pre-captured value from GetMcpServerPackageSource()</param>
        public static System.Collections.Generic.IList<string> GetBetaServerFromArgsList(string gitUrlOverride, string packageSource)
        {
            var args = new System.Collections.Generic.List<string>();

            // Explicit override (local path, git URL, etc.) always wins
            if (!string.IsNullOrEmpty(gitUrlOverride))
            {
                args.Add("--from");
                args.Add(gitUrlOverride);
                return args;
            }

            bool usePrereleaseRange = string.Equals(packageSource, "mcpforunityserver>=0.0.0a0", StringComparison.OrdinalIgnoreCase);

            // Prerelease package mode: use prerelease from PyPI.
            if (usePrereleaseRange)
            {
                args.Add("--prerelease");
                args.Add("explicit");
                args.Add("--from");
                args.Add("mcpforunityserver>=0.0.0a0");
                return args;
            }

            // Standard mode: use pinned version from package.json
            if (!string.IsNullOrEmpty(packageSource))
            {
                args.Add("--from");
                args.Add(packageSource);
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
            if (fromUrl.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
                return true;

            try
            {
                return System.IO.Path.IsPathRooted(fromUrl);
            }
            catch (System.ArgumentException)
            {
                // fromUrl contains characters illegal in paths (e.g. a remote URL)
                return false;
            }
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

        /// <summary>
        /// Returns true if the installed package version is a prerelease (beta, alpha, rc, etc.).
        /// Used to auto-enable beta server mode for beta package users.
        /// </summary>
        public static bool IsPreReleaseVersion()
        {
            try
            {
                string version = GetPackageVersion();
                if (string.IsNullOrEmpty(version) || version == "unknown")
                    return false;

                return IsSemVerPreRelease(version);
            }
            catch
            {
                return false;
            }
        }

        private static bool IsSemVerPreRelease(string version)
        {
            if (string.IsNullOrEmpty(version))
                return false;

            // Common semver prerelease indicators:
            // e.g., "9.3.0-beta.1", "9.3.0-alpha", "9.3.0-rc.2", "9.3.0-preview"
            return version.Contains("-beta", StringComparison.OrdinalIgnoreCase) ||
                   version.Contains("-alpha", StringComparison.OrdinalIgnoreCase) ||
                   version.Contains("-rc", StringComparison.OrdinalIgnoreCase) ||
                   version.Contains("-preview", StringComparison.OrdinalIgnoreCase) ||
                   version.Contains("-pre", StringComparison.OrdinalIgnoreCase);
        }
    }
}
