using System;
using System.IO;
using System.Linq;
using MCPForUnity.Editor.Helpers;
using UnityEditor;
using UnityEngine;

namespace MCPForUnity.Editor.Services
{
    /// <summary>
    /// Service for managing cache operations
    /// </summary>
    public class CacheManagementService : ICacheManagementService
    {
        /// <summary>
        /// Clear the local uvx cache for the MCP server package
        /// </summary>
        /// <returns>True if successful, false otherwise</returns>
        public bool ClearUvxCache()
        {
            try
            {
                var pathService = MCPServiceLocator.Paths;
                bool hasOverride = pathService.HasUvxPathOverride;
                string uvCommand = "uv";

                if (hasOverride)
                {
                    string overridePath = pathService.GetUvxPath();

                    if (!string.IsNullOrEmpty(overridePath) && File.Exists(overridePath))
                    {
                        string overrideDirectory = Path.GetDirectoryName(overridePath);
                        string overrideExtension = Path.GetExtension(overridePath);
                        string overrideName = Path.GetFileNameWithoutExtension(overridePath);

                        if (!string.IsNullOrEmpty(overrideDirectory) && overrideName.Equals("uvx", StringComparison.OrdinalIgnoreCase))
                        {
                            string uvSibling = Path.Combine(overrideDirectory, string.IsNullOrEmpty(overrideExtension) ? "uv" : $"uv{overrideExtension}");
                            if (File.Exists(uvSibling))
                            {
                                uvCommand = uvSibling;
                                McpLog.Debug($"Using UV executable inferred from override: {uvSibling}");
                            }
                            else
                            {
                                uvCommand = overridePath;
                                McpLog.Debug($"Using override executable: {overridePath}");
                            }
                        }
                        else
                        {
                            uvCommand = overridePath;
                            McpLog.Debug($"Using override executable: {overridePath}");
                        }
                    }
                    else
                    {
                        McpLog.Debug("UV override was not found at specified location, falling back to system PATH.");
                    }
                }
                else if (string.Equals(uvCommand, "uv", StringComparison.OrdinalIgnoreCase))
                {
                    McpLog.Debug("No UV override configured; using 'uv' from system PATH.");
                }

                // Get the package name
                string packageName = "mcp-for-unity";

                // Run uvx cache clean command
                string args = $"cache clean {packageName}";

                bool success;
                string stdout;
                string stderr;

                if (!string.Equals(uvCommand, "uv", StringComparison.OrdinalIgnoreCase))
                {
                    success = ExecPath.TryRun(uvCommand, args, Application.dataPath, out stdout, out stderr, 30000);
                }
                else
                {
                    string command = $"uv {args}";
                    string extraPathPrepend = null;

                    if (Application.platform == RuntimePlatform.OSXEditor)
                    {
                        extraPathPrepend = string.Join(Path.PathSeparator.ToString(), new[]
                        {
                            "/opt/homebrew/bin",
                            "/usr/local/bin",
                            "/usr/bin",
                            "/bin"
                        });
                    }
                    else if (Application.platform == RuntimePlatform.LinuxEditor)
                    {
                        extraPathPrepend = string.Join(Path.PathSeparator.ToString(), new[]
                        {
                            "/usr/local/bin",
                            "/usr/bin",
                            "/bin"
                        });
                    }
                    else if (Application.platform == RuntimePlatform.WindowsEditor)
                    {
                        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                        string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);

                        extraPathPrepend = string.Join(Path.PathSeparator.ToString(), new[]
                        {
                            !string.IsNullOrEmpty(localAppData) ? Path.Combine(localAppData, "Programs", "uv") : null,
                            !string.IsNullOrEmpty(programFiles) ? Path.Combine(programFiles, "uv") : null
                        }.Where(p => !string.IsNullOrEmpty(p)).ToArray());
                    }

                    if (Application.platform == RuntimePlatform.WindowsEditor)
                    {
                        success = ExecPath.TryRun("cmd.exe", $"/c {command}", Application.dataPath, out stdout, out stderr, 30000, extraPathPrepend);
                    }
                    else
                    {
                        string shell = File.Exists("/bin/bash") ? "/bin/bash" : "/bin/sh";

                        if (!string.IsNullOrEmpty(shell) && File.Exists(shell))
                        {
                            string escaped = command.Replace("\"", "\\\"");
                            success = ExecPath.TryRun(shell, $"-lc \"{escaped}\"", Application.dataPath, out stdout, out stderr, 30000, extraPathPrepend);
                        }
                        else
                        {
                            success = ExecPath.TryRun("uv", args, Application.dataPath, out stdout, out stderr, 30000, extraPathPrepend);
                        }
                    }
                }

                if (success)
                {
                    McpLog.Debug($"uv cache cleared successfully: {stdout}");
                    return true;
                }
                else
                {
                    string errorMessage = string.IsNullOrEmpty(stderr)
                        ? "Unknown error"
                        : stderr;

                    McpLog.Error($"Failed to clear uv cache using '{uvCommand} {args}': {errorMessage}. Ensure UV/UVX is installed, available on PATH, or set an override in Advanced Settings.");
                    return false;
                }
            }
            catch (Exception ex)
            {
                McpLog.Error($"Error clearing uv cache: {ex.Message}");
                return false;
            }
        }
    }
}
