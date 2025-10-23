using System;
using System.IO;
using UnityEngine;
using UnityEditor;
using MCPForUnity.Editor.Helpers;

namespace MCPForUnity.Editor.Helpers
{
    /// <summary>
    /// Shared helper for resolving MCP server directory paths with support for
    /// development mode, embedded servers, and installed packages
    /// </summary>
    public static class McpPathResolver
    {
        private const string USE_EMBEDDED_SERVER_KEY = "MCPForUnity.UseEmbeddedServer";

        /// <summary>
        /// Resolves the MCP server directory path with comprehensive logic
        /// including development mode support and fallback mechanisms
        /// </summary>
        public static string FindPackagePythonDirectory(bool debugLogsEnabled = false)
        {
            string pythonDir = McpConfigurationHelper.ResolveServerSource();

            try
            {
                // Only check dev paths if we're using a file-based package (development mode)
                bool isDevelopmentMode = IsDevelopmentMode();
                if (isDevelopmentMode)
                {
                    string currentPackagePath = Path.GetDirectoryName(Application.dataPath);
                    string[] devPaths = {
                        Path.Combine(currentPackagePath, "unity-mcp", "UnityMcpServer", "src"),
                        Path.Combine(Path.GetDirectoryName(currentPackagePath), "unity-mcp", "UnityMcpServer", "src"),
                    };

                    foreach (string devPath in devPaths)
                    {
                        if (Directory.Exists(devPath) && File.Exists(Path.Combine(devPath, "server.py")))
                        {
                            if (debugLogsEnabled)
                            {
                                Debug.Log($"Currently in development mode. Package: {devPath}");
                            }
                            return devPath;
                        }
                    }
                }

                // Resolve via shared helper (handles local registry and older fallback) only if dev override on
                if (EditorPrefs.GetBool(USE_EMBEDDED_SERVER_KEY, false))
                {
                    if (ServerPathResolver.TryFindEmbeddedServerSource(out string embedded))
                    {
                        return embedded;
                    }
                }

                // Log only if the resolved path does not actually contain server.py
                if (debugLogsEnabled)
                {
                    bool hasServer = false;
                    try { hasServer = File.Exists(Path.Combine(pythonDir, "server.py")); } catch { }
                    if (!hasServer)
                    {
                        Debug.LogWarning("Could not find Python directory with server.py; falling back to installed path");
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"Error finding package path: {e.Message}");
            }

            return pythonDir;
        }

        /// <summary>
        /// Checks if the current Unity project is in development mode
        /// (i.e., the package is referenced as a local file path in manifest.json)
        /// </summary>
        private static bool IsDevelopmentMode()
        {
            try
            {
                // Only treat as development if manifest explicitly references a local file path for the package
                string manifestPath = Path.Combine(Application.dataPath, "..", "Packages", "manifest.json");
                if (!File.Exists(manifestPath)) return false;

                string manifestContent = File.ReadAllText(manifestPath);
                // Look specifically for our package dependency set to a file: URL
                // This avoids auto-enabling dev mode just because a repo exists elsewhere on disk
                if (manifestContent.IndexOf("\"com.coplaydev.unity-mcp\"", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    int idx = manifestContent.IndexOf("com.coplaydev.unity-mcp", StringComparison.OrdinalIgnoreCase);
                    // Crude but effective: check for "file:" in the same line/value
                    if (manifestContent.IndexOf("file:", idx, StringComparison.OrdinalIgnoreCase) >= 0
                        && manifestContent.IndexOf("\n", idx, StringComparison.OrdinalIgnoreCase) > manifestContent.IndexOf("file:", idx, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
                return false;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Gets the appropriate PATH prepend for the current platform when running external processes
        /// </summary>
        public static string GetPathPrepend()
        {
            if (Application.platform == RuntimePlatform.OSXEditor)
                return "/opt/homebrew/bin:/usr/local/bin:/usr/bin:/bin";
            else if (Application.platform == RuntimePlatform.LinuxEditor)
                return "/usr/local/bin:/usr/bin:/bin";
            return null;
        }
    }
}
