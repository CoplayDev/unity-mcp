using System;
using System.IO;
using System.Net;
using System.Text;
using UnityEditor;
using UnityEngine;

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

#pragma warning disable SYSLIB0014
                using (var client = new WebClient())
#pragma warning restore SYSLIB0014
                {
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

                        byte[] nupkgBytes = client.DownloadData(url);
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

        /// <summary>
        /// Extracts a single file from a ZIP archive byte array without System.IO.Compression.
        /// Only supports Deflate (method 8) and Store (method 0) — sufficient for nupkg files.
        /// </summary>
        private static byte[] ExtractFileFromZip(byte[] zipBytes, string entryPath)
        {
            // Normalize path separators: ZIP spec uses forward slashes
            entryPath = entryPath.Replace('\\', '/');
            int pos = 0;

            while (pos + 30 <= zipBytes.Length)
            {
                // Local file header signature = 0x04034b50
                uint sig = ReadUInt32LE(zipBytes, pos);
                if (sig != 0x04034b50)
                    break;

                ushort method = ReadUInt16LE(zipBytes, pos + 8);
                uint compressedSize = ReadUInt32LE(zipBytes, pos + 18);
                uint uncompressedSize = ReadUInt32LE(zipBytes, pos + 22);
                ushort nameLen = ReadUInt16LE(zipBytes, pos + 26);
                ushort extraLen = ReadUInt16LE(zipBytes, pos + 28);

                string name = Encoding.UTF8.GetString(zipBytes, pos + 30, nameLen);
                int dataStart = pos + 30 + nameLen + extraLen;

                if (name.Replace('\\', '/').Equals(entryPath, StringComparison.OrdinalIgnoreCase))
                {
                    if (method == 0) // Store
                    {
                        byte[] result = new byte[uncompressedSize];
                        Buffer.BlockCopy(zipBytes, dataStart, result, 0, (int)uncompressedSize);
                        return result;
                    }
                    if (method == 8) // Deflate
                    {
                        using (var compressed = new MemoryStream(zipBytes, dataStart, (int)compressedSize))
                        using (var deflate = new System.IO.Compression.DeflateStream(compressed, System.IO.Compression.CompressionMode.Decompress))
                        using (var output = new MemoryStream((int)uncompressedSize))
                        {
                            deflate.CopyTo(output);
                            return output.ToArray();
                        }
                    }

                    Debug.LogWarning($"[MCP] Unsupported ZIP method {method} for {name}");
                    return null;
                }

                pos = dataStart + (int)compressedSize;
            }

            return null;
        }

        private static ushort ReadUInt16LE(byte[] buf, int offset)
        {
            return (ushort)(buf[offset] | (buf[offset + 1] << 8));
        }

        private static uint ReadUInt32LE(byte[] buf, int offset)
        {
            return (uint)(buf[offset] | (buf[offset + 1] << 8) | (buf[offset + 2] << 16) | (buf[offset + 3] << 24));
        }
    }
}
