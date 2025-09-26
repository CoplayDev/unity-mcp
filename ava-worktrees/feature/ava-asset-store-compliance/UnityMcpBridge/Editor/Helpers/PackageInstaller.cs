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
                Debug.Log("<b><color=#2EA3FF>MCP-FOR-UNITY</color></b>: Installing Python server...");
                ServerInstaller.EnsureServerInstalled();
                
                // Mark as installed
                EditorPrefs.SetBool(InstallationFlagKey, true);
                
                Debug.Log("<b><color=#2EA3FF>MCP-FOR-UNITY</color></b>: Python server installation completed successfully.");
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"<b><color=#2EA3FF>MCP-FOR-UNITY</color></b>: Failed to install Python server: {ex.Message}");
                Debug.LogWarning("<b><color=#2EA3FF>MCP-FOR-UNITY</color></b>: You may need to manually install the Python server. Check the MCP for Unity Editor Window for instructions.");
            }
        }
    }
}
