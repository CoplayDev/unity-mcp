using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using MCPForUnity.Editor.Dependencies;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Models;

namespace MCPForUnity.Editor.Helpers
{
    /// <summary>
    /// Shared helper for MCP client configuration management with sophisticated
    /// logic for preserving existing configs and handling different client types
    /// </summary>
    public static class McpConfigurationHelper
    {
        private const string LOCK_CONFIG_KEY = "MCPForUnity.LockCursorConfig";

        /// <summary>
        /// Writes MCP configuration to the specified path using sophisticated logic
        /// that preserves existing configuration and only writes when necessary
        /// </summary>
        public static string WriteMcpConfiguration(string pythonDir, string configPath, McpClient mcpClient = null)
        {
            // 0) Respect explicit lock (hidden pref or UI toggle)
            try
            {
                if (EditorPrefs.GetBool(LOCK_CONFIG_KEY, false))
                    return "Skipped (locked)";
            }
            catch { }

            JsonSerializerSettings jsonSettings = new() { Formatting = Formatting.Indented };

            // Read existing config if it exists
            string existingJson = "{}";
            if (File.Exists(configPath))
            {
                try
                {
                    existingJson = File.ReadAllText(configPath);
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"Error reading existing config: {e.Message}.");
                }
            }

            // Parse the existing JSON while preserving all properties
            dynamic existingConfig;
            try
            {
                if (string.IsNullOrWhiteSpace(existingJson))
                {
                    existingConfig = new JObject();
                }
                else
                {
                    existingConfig = JsonConvert.DeserializeObject(existingJson) ?? new JObject();
                }
            }
            catch
            {
                // If user has partial/invalid JSON (e.g., mid-edit), start from a fresh object
                if (!string.IsNullOrWhiteSpace(existingJson))
                {
                    Debug.LogWarning("UnityMCP: Configuration file could not be parsed; rewriting server block.");
                }
                existingConfig = new JObject();
            }

            // Determine existing entry references (command/args)
            string existingCommand = null;
            string[] existingArgs = null;
            bool isVSCode = (mcpClient?.mcpType == McpTypes.VSCode);
            try
            {
                if (isVSCode)
                {
                    existingCommand = existingConfig?.servers?.unityMCP?.command?.ToString();
                    existingArgs = existingConfig?.servers?.unityMCP?.args?.ToObject<string[]>();
                }
                else
                {
                    existingCommand = existingConfig?.mcpServers?.unityMCP?.command?.ToString();
                    existingArgs = existingConfig?.mcpServers?.unityMCP?.args?.ToObject<string[]>();
                }
            }
            catch { }

            // 1) Start from existing, only fill gaps (prefer trusted resolver)
            string uvPath = ServerInstaller.FindUvPath();
            // Optionally trust existingCommand if it looks like uv/uv.exe
            try
            {
                var name = Path.GetFileName((existingCommand ?? string.Empty).Trim()).ToLowerInvariant();
                if ((name == "uv" || name == "uv.exe") && IsValidUvBinary(existingCommand))
                {
                    uvPath = existingCommand;
                }
            }
            catch { }
            if (uvPath == null) return "UV package manager not found. Please install UV first.";
            string serverSrc = McpConfigFileHelper.ResolveServerDirectory(pythonDir, existingArgs);

            // Ensure containers exist and write back configuration
            JObject existingRoot;
            if (existingConfig is JObject eo)
                existingRoot = eo;
            else
                existingRoot = JObject.FromObject(existingConfig);

            existingRoot = ConfigJsonBuilder.ApplyUnityServerToExistingConfig(existingRoot, uvPath, serverSrc, mcpClient);

            string mergedJson = JsonConvert.SerializeObject(existingRoot, jsonSettings);

            McpConfigFileHelper.EnsureConfigDirectoryExists(configPath);
            McpConfigFileHelper.WriteAtomicFile(configPath, mergedJson);

            try
            {
                if (File.Exists(uvPath)) EditorPrefs.SetString("MCPForUnity.UvPath", uvPath);
                EditorPrefs.SetString("MCPForUnity.ServerSrc", serverSrc);
            }
            catch { }

            return "Configured successfully";
        }

        /// <summary>
        /// Configures a Codex client with sophisticated TOML handling
        /// </summary>
        public static string ConfigureCodexClient(string pythonDir, string configPath, McpClient mcpClient)
        {
            try
            {
                if (EditorPrefs.GetBool(LOCK_CONFIG_KEY, false))
                    return "Skipped (locked)";
            }
            catch { }

            string existingToml = string.Empty;
            if (File.Exists(configPath))
            {
                try
                {
                    existingToml = File.ReadAllText(configPath);
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"UnityMCP: Failed to read Codex config '{configPath}': {e.Message}");
                    existingToml = string.Empty;
                }
            }

            string existingCommand = null;
            string[] existingArgs = null;
            if (!string.IsNullOrWhiteSpace(existingToml))
            {
                CodexConfigHelper.TryParseCodexServer(existingToml, out existingCommand, out existingArgs);
            }

            string uvPath = ServerInstaller.FindUvPath();
            try
            {
                var name = Path.GetFileName((existingCommand ?? string.Empty).Trim()).ToLowerInvariant();
                if ((name == "uv" || name == "uv.exe") && IsValidUvBinary(existingCommand))
                {
                    uvPath = existingCommand;
                }
            }
            catch { }

            if (uvPath == null)
            {
                return "UV package manager not found. Please install UV first.";
            }

            string serverSrc = McpConfigFileHelper.ResolveServerDirectory(pythonDir, existingArgs);

            string updatedToml = CodexConfigHelper.UpsertCodexServerBlock(existingToml, uvPath, serverSrc);

            McpConfigFileHelper.EnsureConfigDirectoryExists(configPath);
            McpConfigFileHelper.WriteAtomicFile(configPath, updatedToml);

            try
            {
                if (File.Exists(uvPath)) EditorPrefs.SetString("MCPForUnity.UvPath", uvPath);
                EditorPrefs.SetString("MCPForUnity.ServerSrc", serverSrc);
            }
            catch { }

            return "Configured successfully";
        }

        /// <summary>
        /// Validates UV binary by running --version command
        /// </summary>
        private static bool IsValidUvBinary(string path)
        {
            try
            {
                if (!File.Exists(path)) return false;
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = path,
                    Arguments = "--version",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                using var p = System.Diagnostics.Process.Start(psi);
                if (p == null) return false;
                if (!p.WaitForExit(3000)) { try { p.Kill(); } catch { } return false; }
                if (p.ExitCode != 0) return false;
                string output = p.StandardOutput.ReadToEnd().Trim();
                return output.StartsWith("uv ");
            }
            catch { return false; }
        }

        /// <summary>
        /// Compares two string arrays for equality
        /// </summary>
        private static bool ArgsEqual(string[] a, string[] b)
        {
            if (a == null || b == null) return a == b;
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
            {
                if (!string.Equals(a[i], b[i], StringComparison.Ordinal)) return false;
            }
            return true;
        }

        /// <summary>
        /// Gets the appropriate config file path for the given MCP client based on OS
        /// </summary>
        public static string GetClientConfigPath(McpClient mcpClient)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return mcpClient.windowsConfigPath;
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                return string.IsNullOrEmpty(mcpClient.macConfigPath)
                    ? mcpClient.linuxConfigPath
                    : mcpClient.macConfigPath;
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                return mcpClient.linuxConfigPath;
            }
            else
            {
                return mcpClient.linuxConfigPath; // fallback
            }
        }

        /// <summary>
        /// Creates the directory for the config file if it doesn't exist
        /// </summary>
        public static void EnsureConfigDirectoryExists(string configPath)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(configPath));
        }
    }
}
