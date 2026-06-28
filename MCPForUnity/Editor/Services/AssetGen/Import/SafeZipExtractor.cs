using System;
using System.IO;
using System.IO.Compression;

namespace MCPForUnity.Editor.Services.AssetGen.Import
{
    /// <summary>
    /// Extracts a .zip into a destination directory while rejecting Zip-Slip path traversal:
    /// every entry's resolved target must stay inside <c>destDir</c>. Directory entries are
    /// created; file entries are written by copying the entry stream (no reliance on the
    /// ZipFileExtensions helper). Used to unpack marketplace model archives (e.g. Sketchfab).
    /// </summary>
    public static class SafeZipExtractor
    {
        public static void ExtractTo(string zipPath, string destDir)
        {
            if (string.IsNullOrEmpty(zipPath)) throw new ArgumentException("zipPath required", nameof(zipPath));
            if (string.IsNullOrEmpty(destDir)) throw new ArgumentException("destDir required", nameof(destDir));

            Directory.CreateDirectory(destDir);
            string destFull = Path.GetFullPath(destDir);
            string prefix = destFull.EndsWith(Path.DirectorySeparatorChar.ToString())
                ? destFull
                : destFull + Path.DirectorySeparatorChar;

            using (FileStream fs = File.OpenRead(zipPath))
            using (var archive = new ZipArchive(fs, ZipArchiveMode.Read))
            {
                foreach (ZipArchiveEntry entry in archive.Entries)
                {
                    string name = entry.FullName;
                    if (string.IsNullOrEmpty(name)) continue;

                    // Reject traversal / absolute paths up front.
                    if (name.Contains("..") || Path.IsPathRooted(name))
                        throw new IOException($"Unsafe zip entry rejected: {name}");

                    string target = Path.GetFullPath(Path.Combine(destDir, name));
                    if (!target.StartsWith(prefix, StringComparison.Ordinal))
                        throw new IOException($"Unsafe zip entry escapes destination: {name}");

                    // A directory entry has an empty Name (FullName ends with a separator).
                    if (string.IsNullOrEmpty(entry.Name))
                    {
                        Directory.CreateDirectory(target);
                        continue;
                    }

                    string parent = Path.GetDirectoryName(target);
                    if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);

                    using (Stream src = entry.Open())
                    using (FileStream dst = File.Create(target))
                    {
                        src.CopyTo(dst);
                    }
                }
            }
        }
    }
}
