using System;
using System.IO;
using UnityEditor;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Newtonsoft.Json;
using UnityEngine;

namespace MCPForUnity.Editor.Helpers
{
    /// <summary>
    /// Manages dynamic port allocation and persistent storage for MCP for Unity
    /// </summary>
    public static class PortManager
    {
        private static bool IsDebugEnabled()
        {
            try { return EditorPrefs.GetBool("MCPForUnity.DebugLogs", false); }
            catch { return false; }
        }

        private const int DefaultPort = 6400;
        private const int MaxPortAttempts = 100;
        private const string RegistryFileName = "unity-mcp-port.json";

        [Serializable]
        public class PortConfig
        {
            public int unity_port;
            public string created_date;
            public string project_path;
        }

        /// <summary>
        /// Get the port to use - either from storage or discover a new one
        /// Will try stored port first, then fallback to discovering new port
        /// </summary>
        /// <returns>Port number to use</returns>
        public static int GetPortWithFallback()
        {
            // Try to load stored port first, but only if it's from the current project
            var storedConfig = GetStoredPortConfig();
            if (storedConfig != null && 
                storedConfig.unity_port > 0 && 
                string.Equals(storedConfig.project_path ?? string.Empty, Application.dataPath ?? string.Empty, StringComparison.OrdinalIgnoreCase) &&
                IsPortAvailable(storedConfig.unity_port))
            {
                if (IsDebugEnabled()) Debug.Log($"<b><color=#2EA3FF>MCP-FOR-UNITY</color></b>: Using stored port {storedConfig.unity_port} for current project");
                return storedConfig.unity_port;
            }

            // If stored port exists but is currently busy, wait briefly for release
            if (storedConfig != null && storedConfig.unity_port > 0)
            {
                if (WaitForPortRelease(storedConfig.unity_port, 1500))
                {
                    if (IsDebugEnabled()) Debug.Log($"<b><color=#2EA3FF>MCP-FOR-UNITY</color></b>: Stored port {storedConfig.unity_port} became available after short wait");
                    return storedConfig.unity_port;
                }
                // Prefer sticking to the same port; let the caller handle bind retries/fallbacks
                return storedConfig.unity_port;
            }

            // If no valid stored port, find a new one and save it
            int newPort = FindAvailablePort();
            SavePort(newPort);
            return newPort;
        }

        /// <summary>
        /// Discover and save a new available port (used by Auto-Connect button)
        /// </summary>
        /// <returns>New available port</returns>
        public static int DiscoverNewPort()
        {
            int newPort = FindAvailablePort();
            SavePort(newPort);
            if (IsDebugEnabled()) Debug.Log($"<b><color=#2EA3FF>MCP-FOR-UNITY</color></b>: Discovered and saved new port: {newPort}");
            return newPort;
        }

        /// <summary>
        /// Find an available port starting from the default port
        /// </summary>
        /// <returns>Available port number</returns>
        private static int FindAvailablePort()
        {
            // Always try default port first
            if (IsPortAvailable(DefaultPort))
            {
                if (IsDebugEnabled()) Debug.Log($"<b><color=#2EA3FF>MCP-FOR-UNITY</color></b>: Using default port {DefaultPort}");
                return DefaultPort;
            }

            if (IsDebugEnabled()) Debug.Log($"<b><color=#2EA3FF>MCP-FOR-UNITY</color></b>: Default port {DefaultPort} is in use, searching for alternative...");

            // Search for alternatives
            for (int port = DefaultPort + 1; port < DefaultPort + MaxPortAttempts; port++)
            {
                if (IsPortAvailable(port))
                {
                    if (IsDebugEnabled()) Debug.Log($"<b><color=#2EA3FF>MCP-FOR-UNITY</color></b>: Found available port {port}");
                    return port;
                }
            }

            throw new Exception($"No available ports found in range {DefaultPort}-{DefaultPort + MaxPortAttempts}");
        }

        /// <summary>
        /// Check if a specific port is available for binding
        /// </summary>
        /// <param name="port">Port to check</param>
        /// <returns>True if port is available</returns>
        public static bool IsPortAvailable(int port)
        {
            try
            {
                var testListener = new TcpListener(IPAddress.Loopback, port);
                testListener.Start();
                testListener.Stop();
                return true;
            }
            catch (SocketException)
            {
                return false;
            }
        }

        /// <summary>
        /// Check if a port is currently being used by MCP for Unity
        /// This helps avoid unnecessary port changes when Unity itself is using the port
        /// </summary>
        /// <param name="port">Port to check</param>
        /// <returns>True if port appears to be used by MCP for Unity</returns>
        public static bool IsPortUsedByMCPForUnity(int port)
        {
            try
            {
                // Try to make a quick connection to see if it's an MCP for Unity server
                using var client = new TcpClient();
                var connectTask = client.ConnectAsync(IPAddress.Loopback, port);
                if (connectTask.Wait(100)) // 100ms timeout
                {
                    // If connection succeeded, it's likely the MCP for Unity server
                    return client.Connected;
                }
                return false;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Wait for a port to become available for a limited amount of time.
        /// Used to bridge the gap during domain reload when the old listener
        /// hasn't released the socket yet.
        /// </summary>
        private static bool WaitForPortRelease(int port, int timeoutMs)
        {
            int waited = 0;
            const int step = 100;
            while (waited < timeoutMs)
            {
                if (IsPortAvailable(port))
                {
                    return true;
                }

                // If the port is in use by an MCP instance, continue waiting briefly
                if (!IsPortUsedByMCPForUnity(port))
                {
                    // In use by something else; don't keep waiting
                    return false;
                }

                Thread.Sleep(step);
                waited += step;
            }
            return IsPortAvailable(port);
        }

        /// <summary>
        /// Save port to persistent storage
        /// </summary>
        /// <param name="port">Port to save</param>
        private static void SavePort(int port)
        {
            try
            {
                var portConfig = new PortConfig
                {
                    unity_port = port,
                    created_date = DateTime.UtcNow.ToString("O"),
                    project_path = Application.dataPath
                };

                string registryDir = GetRegistryDirectory();
                Directory.CreateDirectory(registryDir);

                string registryFile = GetRegistryFilePath();
                string json = JsonConvert.SerializeObject(portConfig, Formatting.Indented);
                // Write to hashed, project-scoped file
                File.WriteAllText(registryFile, json, new System.Text.UTF8Encoding(false));
                // Also write to legacy stable filename to avoid hash/case drift across reloads
                string legacy = Path.Combine(GetRegistryDirectory(), RegistryFileName);
                File.WriteAllText(legacy, json, new System.Text.UTF8Encoding(false));

                if (IsDebugEnabled()) Debug.Log($"<b><color=#2EA3FF>MCP-FOR-UNITY</color></b>: Saved port {port} to storage");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Could not save port to storage: {ex.Message}");
            }
        }

        /// <summary>
        /// Load port from persistent storage
        /// </summary>
        /// <returns>Stored port number, or 0 if not found</returns>
        private static int LoadStoredPort()
        {
            try
            {
                string registryFile = GetRegistryFilePath();
                
                if (!File.Exists(registryFile))
                {
                    // Backwards compatibility: try the legacy file name
                    string legacy = Path.Combine(GetRegistryDirectory(), RegistryFileName);
                    if (!File.Exists(legacy))
                    {
                        return 0;
                    }
                    registryFile = legacy;
                }

                string json = File.ReadAllText(registryFile);
                var portConfig = JsonConvert.DeserializeObject<PortConfig>(json);

                return portConfig?.unity_port ?? 0;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Could not load port from storage: {ex.Message}");
                return 0;
            }
        }

        /// <summary>
        /// Get the current stored port configuration
        /// </summary>
        /// <returns>Port configuration if exists, null otherwise</returns>
        public static PortConfig GetStoredPortConfig()
        {
            try
            {
                string registryFile = GetRegistryFilePath();
                
                if (!File.Exists(registryFile))
                {
                    // Backwards compatibility: try the legacy file
                    string legacy = Path.Combine(GetRegistryDirectory(), RegistryFileName);
                    if (!File.Exists(legacy))
                    {
                        return null;
                    }
                    registryFile = legacy;
                }

                string json = File.ReadAllText(registryFile);
                return JsonConvert.DeserializeObject<PortConfig>(json);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Could not load port config: {ex.Message}");
                return null;
            }
        }

        private static string GetRegistryDirectory()
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".unity-mcp");
        }

        private static string GetRegistryFilePath()
        {
            string dir = GetRegistryDirectory();
            string hash = ComputeProjectHash(Application.dataPath);
            string fileName = $"unity-mcp-port-{hash}.json";
            return Path.Combine(dir, fileName);
        }

        private static string ComputeProjectHash(string input)
        {
            try
            {
                using SHA1 sha1 = SHA1.Create();
                byte[] bytes = Encoding.UTF8.GetBytes(input ?? string.Empty);
                byte[] hashBytes = sha1.ComputeHash(bytes);
                var sb = new StringBuilder();
                foreach (byte b in hashBytes)
                {
                    sb.Append(b.ToString("x2"));
                }
                return sb.ToString()[..8]; // short, sufficient for filenames
            }
            catch
            {
                return "default";
            }
        }
    }
}