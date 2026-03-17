using UnityEditor;
using UnityEngine;
using System.IO;

namespace MCPForUnity.Editor.Helpers
{
    /// <summary>
    /// Automatically copies UXML and USS files from WSL package directories to a local
    /// <c>Assets/MCPForUnityUI/</c> folder on every domain reload, preserving directory structure.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Problem:</b> Unity's UXML/USS importer on Windows cannot properly parse files
    /// when packages live on a WSL2 filesystem (UNC paths like <c>\\wsl$\...</c>). The
    /// VisualTreeAsset loads but CloneTree produces an empty tree.
    /// </para>
    /// <para>
    /// <b>Solution:</b> On startup, this class copies all UI asset files to
    /// <c>Assets/MCPForUnityUI/</c> and <see cref="AssetPathUtility.GetMcpPackageRootPath"/>
    /// returns this fallback path when WSL is detected.
    /// </para>
    /// </remarks>
    [InitializeOnLoad]
    static class UIAssetSync
    {
        /// <summary>Destination folder under the Unity project for synced UI assets.</summary>
        internal const string SyncedBasePath = "Assets/MCPForUnityUI";

        /// <summary>
        /// Relative paths from package root to UXML and USS files that need syncing.
        /// </summary>
        private static readonly string[] k_UIAssetPaths =
        {
            "Editor/Windows/MCPForUnityEditorWindow.uxml",
            "Editor/Windows/MCPForUnityEditorWindow.uss",
            "Editor/Windows/MCPSetupWindow.uxml",
            "Editor/Windows/MCPSetupWindow.uss",
            "Editor/Windows/EditorPrefs/EditorPrefItem.uxml",
            "Editor/Windows/EditorPrefs/EditorPrefsWindow.uxml",
            "Editor/Windows/EditorPrefs/EditorPrefsWindow.uss",
            "Editor/Windows/Components/Common.uss",
            "Editor/Windows/Components/Connection/McpConnectionSection.uxml",
            "Editor/Windows/Components/ClientConfig/McpClientConfigSection.uxml",
            "Editor/Windows/Components/Validation/McpValidationSection.uxml",
            "Editor/Windows/Components/Advanced/McpAdvancedSection.uxml",
            "Editor/Windows/Components/Tools/McpToolsSection.uxml",
            "Editor/Windows/Components/Resources/McpResourcesSection.uxml",
            "Editor/Windows/Components/Queue/McpQueueSection.uxml",
            "Editor/Windows/Components/Queue/McpQueueSection.uss",
        };

        static UIAssetSync()
        {
            if (!NeedsSync())
                return;

            string packageRoot = GetPackagePhysicalRoot();
            if (string.IsNullOrEmpty(packageRoot))
                return;

            bool anyUpdated = false;

            foreach (string relativePath in k_UIAssetPaths)
            {
                string sourcePath = Path.Combine(packageRoot, relativePath);
                if (!File.Exists(sourcePath))
                    continue;

                string sourceContent = File.ReadAllText(sourcePath);

                string destPath = Path.GetFullPath(Path.Combine(SyncedBasePath, relativePath));
                string destDir = Path.GetDirectoryName(destPath);

                if (!string.IsNullOrEmpty(destDir) && !Directory.Exists(destDir))
                    Directory.CreateDirectory(destDir);

                if (File.Exists(destPath) && File.ReadAllText(destPath) == sourceContent)
                    continue;

                File.WriteAllText(destPath, sourceContent);
                Debug.Log($"[UIAssetSync] Updated {relativePath}");
                anyUpdated = true;
            }

            if (anyUpdated)
                AssetDatabase.Refresh();
        }

        /// <summary>
        /// Returns true when the MCP package lives on a WSL UNC path and Unity runs on Windows.
        /// </summary>
        internal static bool NeedsSync()
        {
            if (Application.platform != RuntimePlatform.WindowsEditor)
                return false;

            string packageRoot = GetPackagePhysicalRoot();
            if (string.IsNullOrEmpty(packageRoot))
                return false;

            return packageRoot.StartsWith(@"\\wsl", System.StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Gets the physical (filesystem) root path of the MCP package.
        /// </summary>
        private static string GetPackagePhysicalRoot()
        {
            var packageInfo = UnityEditor.PackageManager.PackageInfo.FindForAssembly(
                typeof(UIAssetSync).Assembly);
            if (packageInfo != null && !string.IsNullOrEmpty(packageInfo.resolvedPath))
                return packageInfo.resolvedPath;

            // Fallback: resolve the virtual asset path
            if (packageInfo != null && !string.IsNullOrEmpty(packageInfo.assetPath))
                return Path.GetFullPath(packageInfo.assetPath);

            return null;
        }
    }
}
