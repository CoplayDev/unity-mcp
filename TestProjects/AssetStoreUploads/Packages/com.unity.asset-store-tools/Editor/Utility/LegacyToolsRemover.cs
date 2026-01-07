using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace AssetStoreTools.Utility
{
    [InitializeOnLoad]
    internal class LegacyToolsRemover
    {
        private const string MessagePart1 = "A legacy version of Asset Store Tools " +
            "was detected at the following path:\n";
        private const string MessagePart2 = "\n\nHaving both the legacy and the latest version installed at the same time is not supported " +
            "and might prevent the latest version from functioning properly.\n\nWould you like the legacy version to be removed automatically?";

        static LegacyToolsRemover()
        {
            try
            {
                if (Application.isBatchMode)
                    return;

                CheckAndRemoveLegacyTools();
            }
            catch { }
        }

        private static void CheckAndRemoveLegacyTools()
        {
            if (!ASToolsPreferences.Instance.LegacyVersionCheck || !ProjectContainsLegacyTools(out string path))
                return;

            var relativePath = path.Substring(Application.dataPath.Length - "Assets".Length).Replace("\\", "/");
            var result = EditorUtility.DisplayDialog("Asset Store Tools", MessagePart1 + relativePath + MessagePart2, "Yes", "No");

            // If "No" - do nothing
            if (!result)
                return;

            // If "Yes" - remove legacy tools
            File.Delete(path);
            File.Delete(path + ".meta");
            RemoveEmptyFolders(Path.GetDirectoryName(path)?.Replace("\\", "/"));
            AssetDatabase.Refresh();

            // We could also optionally prevent future execution here
            // but the ProjectContainsLegacyTools() function runs in less
            // than a milisecond on an empty project
        }

        private static bool ProjectContainsLegacyTools(out string path)
        {
            path = null;

            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (assembly.ManifestModule.Name == "AssetStoreTools.dll")
                {
                    path = assembly.Location;
                    break;
                }
            }

            if (string.IsNullOrEmpty(path))
                return false;
            return true;
        }

        private static void RemoveEmptyFolders(string directory)
        {
            if (directory.EndsWith(Application.dataPath))
                return;

            if (Directory.GetFiles(directory).Length == 0 && Directory.GetDirectories(directory).Length == 0)
            {
                var parentPath = Path.GetDirectoryName(directory).Replace("\\", "/");

                Directory.Delete(directory);
                File.Delete(directory + ".meta");

                RemoveEmptyFolders(parentPath);
            }
        }
    }
}