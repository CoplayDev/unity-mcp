using UnityEditor;
using UnityEngine;

namespace MCPForUnity.Editor.Helpers
{
    /// <summary>
    /// Handles automatic installation of the Python server when the package is first installed.
    /// </summary>
    [InitializeOnLoad]
    public static class PackageInstaller
    {
        private const string InstallationFlagKey = "MCPForUnity.ServerInstalled";

        static PackageInstaller()
        {
            // Check if this is the first time the package is loaded
            if (!EditorPrefs.GetBool(InstallationFlagKey, false))
            {
                // Schedule the installation for after Unity is fully loaded
                EditorApplication.delayCall += InstallServerOnFirstLoad;
            }
        }

        private static void InstallServerOnFirstLoad()
        {
            try
            {
                ServerInstaller.EnsureServerInstalled();

                // Mark as installed/checked
                EditorPrefs.SetBool(InstallationFlagKey, true);

                // Only log success if server was actually embedded and copied
                if (ServerInstaller.HasEmbeddedServer())
                {
                    McpLog.Info("Python server installation completed successfully.");
                }
            }
            catch (System.Exception)
            {
                EditorPrefs.SetBool(InstallationFlagKey, true); // Mark as handled
                McpLog.Info("Server installation pending. Open Window > MCP For Unity to download the server.");
            }
        }
    }
}
