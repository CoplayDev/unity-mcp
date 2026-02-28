using System;
using System.IO;
using System.IO.Compression;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

namespace MCPForUnity.Editor.Setup
{
    public static class RoslynInstaller
    {
        private const string PluginsRelPath = "Plugins/Roslyn";

        private static readonly (string packageId, string version, string dllPath, string dllName)[] NuGetEntries =
        {
            ("microsoft.codeanalysis.common",    "4.12.0", "lib/netstandard2.0/Microsoft.CodeAnalysis.dll",       "Microsoft.CodeAnalysis.dll"),
            ("microsoft.codeanalysis.csharp",    "4.12.0", "lib/netstandard2.0/Microsoft.CodeAnalysis.CSharp.dll","Microsoft.CodeAnalysis.CSharp.dll"),
            ("system.collections.immutable",     "8.0.0",  "lib/netstandard2.0/System.Collections.Immutable.dll", "System.Collections.Immutable.dll"),
            ("system.reflection.metadata",       "8.0.0",  "lib/netstandard2.0/System.Reflection.Metadata.dll",   "System.Reflection.Metadata.dll"),
        };

        [MenuItem("Window/MCP For Unity/Install Roslyn DLLs", priority = 20)]
        public static void InstallViaMenu()
        {
            Install(interactive: true);
        }

        public static bool IsInstalled()
        {
            string folder = Path.Combine(Application.dataPath, PluginsRelPath);
            foreach (var entry in NuGetEntries)
            {
                if (!File.Exists(Path.Combine(folder, entry.dllName)))
                    return false;
            }
            return true;
        }

        public static void Install(bool interactive = true)
        {
            if (IsInstalled() && interactive)
            {
                if (!EditorUtility.DisplayDialog(
                        "Roslyn Already Installed",
                        $"Roslyn DLLs are already present in Assets/{PluginsRelPath}.\nReinstall?",
                        "Reinstall", "Cancel"))
                    return;
            }

            string destFolder = Path.Combine(Application.dataPath, PluginsRelPath);

            try
            {
                Directory.CreateDirectory(destFolder);

                for (int i = 0; i < NuGetEntries.Length; i++)
                {
                    var (packageId, pkgVersion, dllPathInZip, dllName) = NuGetEntries[i];

                    if (interactive)
                    {
                        EditorUtility.DisplayProgressBar(
                            "Installing Roslyn",
                            $"Downloading {packageId} v{pkgVersion}...",
                            (float)i / NuGetEntries.Length);
                    }

                    string url =
                        $"https://api.nuget.org/v3-flatcontainer/{packageId}/{pkgVersion}/{packageId}.{pkgVersion}.nupkg";

                    using (var request = UnityWebRequest.Get(url))
                    {
                        request.timeout = 30;
                        request.SendWebRequest();
                        while (!request.isDone)
                            System.Threading.Thread.Sleep(50);

                        if (request.result != UnityWebRequest.Result.Success)
                            throw new Exception($"Failed to download {packageId}: {request.error}");

                        byte[] nupkgBytes = request.downloadHandler.data;
                        byte[] dllBytes = ExtractFileFromZip(nupkgBytes, dllPathInZip);

                        if (dllBytes == null)
                        {
                            Debug.LogError($"[MCP] Could not find {dllPathInZip} in {packageId}.{pkgVersion}.nupkg");
                            continue;
                        }

                        string destPath = Path.Combine(destFolder, dllName);
                        File.WriteAllBytes(destPath, dllBytes);
                        Debug.Log($"[MCP] Extracted {dllName} ({dllBytes.Length / 1024}KB) → Assets/{PluginsRelPath}/{dllName}");
                    }
                }

                if (interactive)
                    EditorUtility.DisplayProgressBar("Installing Roslyn", "Refreshing assets...", 0.95f);

                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

                if (interactive)
                {
                    EditorUtility.ClearProgressBar();
                    EditorUtility.DisplayDialog(
                        "Roslyn Installed",
                        $"Roslyn DLLs and dependencies installed to Assets/{PluginsRelPath}/.\n\n" +
                        "The execute_code tool is now available via MCP.",
                        "OK");
                }

                Debug.Log($"[MCP] Roslyn installation complete ({NuGetEntries.Length} DLLs). execute_code is now available.");
            }
            catch (Exception e)
            {
                if (interactive) EditorUtility.ClearProgressBar();
                Debug.LogError($"[MCP] Failed to install Roslyn: {e}");

                if (interactive)
                {
                    EditorUtility.DisplayDialog(
                        "Installation Failed",
                        $"Could not download Roslyn DLLs:\n{e.Message}\n\n" +
                        "You can manually download Microsoft.CodeAnalysis.CSharp from NuGet " +
                        "and place the DLLs in Assets/Plugins/Roslyn/.",
                        "OK");
                }
            }
        }

        private static byte[] ExtractFileFromZip(byte[] zipBytes, string entryPath)
        {
            entryPath = entryPath.Replace('\\', '/');

            using (var stream = new MemoryStream(zipBytes))
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Read))
            {
                foreach (var entry in archive.Entries)
                {
                    if (entry.FullName.Replace('\\', '/').Equals(entryPath, StringComparison.OrdinalIgnoreCase))
                    {
                        using (var entryStream = entry.Open())
                        using (var output = new MemoryStream())
                        {
                            entryStream.CopyTo(output);
                            return output.ToArray();
                        }
                    }
                }
            }

            return null;
        }
    }
}
