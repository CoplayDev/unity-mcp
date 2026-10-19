using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using MCPForUnity.Editor.Constants;
using MCPForUnity.Editor.Dependencies;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Models;
using MCPForUnity.Editor.Services;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace MCPForUnity.Editor.Helpers
{
    /// <summary>
    /// Shared helper for MCP client configuration management with sophisticated
    /// logic for preserving existing configs and handling different client types
    /// </summary>
    public static class McpConfigurationHelper
    {
        private const string LOCK_CONFIG_KEY = EditorPrefKeys.LockCursorConfig;

        /// <summary>
        /// Writes MCP configuration to the specified path using sophisticated logic
        /// that preserves existing configuration and only writes when necessary
        /// </summary>
        public static string WriteMcpConfiguration(string configPath, McpClient mcpClient = null)
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
                    McpLog.Warn($"Error reading existing config: {e.Message}.");
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
                    McpLog.Warn("UnityMCP: Configuration file could not be parsed; rewriting server block.");
                }
                existingConfig = new JObject();
            }

            // Determine existing entry references (command/args)
            string existingCommand = null;
            string[] existingArgs = null;
            bool isVSCode = (mcpClient?.IsVsCodeLayout == true);
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
            string uvxPath = MCPServiceLocator.Paths.GetUvxPath();
            if (uvxPath == null) return "uv package manager not found. Please install uv first.";

            // Ensure containers exist and write back configuration
            JObject existingRoot;
            if (existingConfig is JObject eo)
                existingRoot = eo;
            else
                existingRoot = JObject.FromObject(existingConfig);

            existingRoot = ConfigJsonBuilder.ApplyUnityServerToExistingConfig(existingRoot, uvxPath, mcpClient);

            string mergedJson = JsonConvert.SerializeObject(existingRoot, jsonSettings);

            EnsureConfigDirectoryExists(configPath);
            WriteAtomicFile(configPath, mergedJson);

            return "Configured successfully";
        }

        /// <summary>
        /// Configures a Codex client with sophisticated TOML handling
        /// </summary>
        public static string ConfigureCodexClient(string configPath, McpClient mcpClient)
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
                    McpLog.Warn($"UnityMCP: Failed to read Codex config '{configPath}': {e.Message}");
                    existingToml = string.Empty;
                }
            }

            string existingCommand = null;
            string[] existingArgs = null;
            if (!string.IsNullOrWhiteSpace(existingToml))
            {
                CodexConfigHelper.TryParseCodexServer(existingToml, out existingCommand, out existingArgs);
            }

            string uvxPath = MCPServiceLocator.Paths.GetUvxPath();
            if (uvxPath == null)
            {
                return "uv package manager not found. Please install uv first.";
            }

            string updatedToml = CodexConfigHelper.UpsertCodexServerBlock(existingToml, uvxPath);

            EnsureConfigDirectoryExists(configPath);
            WriteAtomicFile(configPath, updatedToml);

            return "Configured successfully";
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

        public static string ExtractUvxUrl(string[] args)
        {
            if (args == null) return null;
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], "--from", StringComparison.OrdinalIgnoreCase))
                {
                    return args[i + 1];
                }
            }
            return null;
        }

        public static bool PathsEqual(string a, string b)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return false;
            try
            {
                string na = Path.GetFullPath(a.Trim());
                string nb = Path.GetFullPath(b.Trim());
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    return string.Equals(na, nb, StringComparison.OrdinalIgnoreCase);
                }
                return string.Equals(na, nb, StringComparison.Ordinal);
            }
            catch
            {
                return false;
            }
        }

        public static void WriteAtomicFile(string path, string contents)
        {
            string tmp = path + ".tmp";
            string backup = path + ".backup";
            bool writeDone = false;
            try
            {
                File.WriteAllText(tmp, contents, new UTF8Encoding(false));
                try
                {
                    File.Replace(tmp, path, backup);
                    writeDone = true;
                }
                catch (FileNotFoundException)
                {
                    File.Move(tmp, path);
                    writeDone = true;
                }
                catch (PlatformNotSupportedException)
                {
                    if (File.Exists(path))
                    {
                        try
                        {
                            if (File.Exists(backup)) File.Delete(backup);
                        }
                        catch { }
                        File.Move(path, backup);
                    }
                    File.Move(tmp, path);
                    writeDone = true;
                }
            }
            catch (Exception ex)
            {
                try
                {
                    if (!writeDone && File.Exists(backup))
                    {
                        try { File.Copy(backup, path, true); } catch { }
                    }
                }
                catch { }
                throw new Exception($"Failed to write config file '{path}': {ex.Message}", ex);
            }
            finally
            {
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
                try { if (writeDone && File.Exists(backup)) File.Delete(backup); } catch { }
            }
        }
    }
}
