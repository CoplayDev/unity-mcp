using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace MCPForUnity.Editor.Helpers
{
    /// <summary>Read-only exploration of built bundles; no static dependency on AssetBundleModule.</summary>
    internal static class AssetBundleExplorer
    {
        internal static object Handle(JObject parameters, bool inspect)
        {
            try
            {
                string root = Path.GetFullPath(Path.GetDirectoryName(Application.dataPath));
                string requested = parameters["path"]?.ToString();
                if (string.IsNullOrWhiteSpace(requested)) return new ErrorResponse("'path' is required for AssetBundle exploration.");
                string path = Path.GetFullPath(Path.IsPathRooted(requested) ? requested : Path.Combine(root, requested));
                var comparison = Path.DirectorySeparatorChar == '\\' ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
                if (!path.Equals(root, comparison) && !path.StartsWith(root + Path.DirectorySeparatorChar, comparison))
                    return new ErrorResponse("AssetBundle paths must be inside the Unity project.");
                if (HasLink(root, path)) return new ErrorResponse("AssetBundle paths cannot traverse symbolic links or reparse points.");
                int pageSize = ParamCoercion.CoerceInt(parameters["pageSize"], 25);
                int pageNumber = ParamCoercion.CoerceInt(parameters["pageNumber"], 1);
                if (pageSize < 1 || pageSize > 200 || pageNumber < 1)
                    return new ErrorResponse("Use pageSize from 1 to 200 and a pageNumber of at least 1.");
                return inspect ? Inspect(root, path, pageSize, pageNumber) : List(root, path, pageSize, pageNumber);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is ArgumentException || e is NotSupportedException)
            {
                return new ErrorResponse("Could not read the selected AssetBundle path. Check that it exists and is accessible.");
            }
        }

        private static bool HasLink(string root, string path)
        {
            string current = root;
            foreach (string segment in path.Substring(root.Length).Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries))
            {
                current = Path.Combine(current, segment);
                try
                {
                    if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) return true;
                }
                catch (FileNotFoundException) { break; }
                catch (DirectoryNotFoundException) { break; }
            }
            return false;
        }

        private static string Relative(string root, string path) => path.Substring(root.Length).TrimStart(Path.DirectorySeparatorChar).Replace('\\', '/');

        private static bool HasBundleHeader(string path)
        {
            // Stat before opening. Empty/short files and Unix FIFOs/sockets have no
            // bundle header; opening a FIFO would wait for a writer on the Editor thread.
            if (new FileInfo(path).Length < 8) return false;
            var bytes = new byte[9];
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                if (stream.Read(bytes, 0, bytes.Length) < 8) return false;
            }
            string header = Encoding.ASCII.GetString(bytes);
            return header.StartsWith("UnityFS\0", StringComparison.Ordinal) ||
                header.StartsWith("UnityRaw\0", StringComparison.Ordinal) ||
                header.StartsWith("UnityWeb\0", StringComparison.Ordinal);
        }

        private static object List(string root, string path, int pageSize, int pageNumber)
        {
            if (!Directory.Exists(path)) return new ErrorResponse("'path' must identify an existing project directory for list_asset_bundles.");
            var entries = new List<object>();
            foreach (string entry in Directory.EnumerateFileSystemEntries(path).OrderBy(p => p, StringComparer.Ordinal))
            {
                var attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.ReparsePoint) != 0) continue;
                bool directory = (attributes & FileAttributes.Directory) != 0;
                if (directory) entries.Add(new { type = "directory", path = Relative(root, entry), sizeBytes = (long?)null });
                else if (HasBundleHeader(entry)) entries.Add(new { type = "bundle", path = Relative(root, entry), sizeBytes = (long?)new FileInfo(entry).Length });
            }
            return new SuccessResponse("AssetBundle directory read successfully.", new
            {
                path = Relative(root, path), entries = Page(entries, pageSize, pageNumber),
                totalEntries = entries.Count, pageSize, pageNumber,
                nextPageNumber = NextPage(entries.Count, pageSize, pageNumber)
            });
        }

        private static object Inspect(string root, string path, int pageSize, int pageNumber)
        {
            if (!File.Exists(path)) return new ErrorResponse("'path' must identify an existing bundle file for get_asset_bundle_info.");
            if (!HasBundleHeader(path)) return new ErrorResponse("The selected file does not have a Unity AssetBundle header.");
            Type type = Type.GetType("UnityEngine.AssetBundle, UnityEngine.AssetBundleModule", false);
            if (type == null) return new ErrorResponse("AssetBundle inspection requires the built-in com.unity.modules.assetbundle package.");
            object bundle = null;
            try
            {
                bundle = type.GetMethod("LoadFromFile", new[] { typeof(string) }).Invoke(null, new object[] { path });
                if (bundle == null) return new ErrorResponse("Unity could not load this AssetBundle. Check its build target, file integrity, and whether it is already loaded.");
                string[] assets = (string[])type.GetMethod("GetAllAssetNames").Invoke(bundle, null);
                string[] scenes = (string[])type.GetMethod("GetAllScenePaths").Invoke(bundle, null);
                var entries = assets.OrderBy(s => s, StringComparer.Ordinal).Select(s => (object)new { type = "asset", name = s })
                    .Concat(scenes.OrderBy(s => s, StringComparer.Ordinal).Select(s => (object)new { type = "scene", name = s })).ToList();
                return new SuccessResponse("AssetBundle contents read successfully.", new
                {
                    path = Relative(root, path), bundleName = type.GetProperty("name").GetValue(bundle),
                    sizeBytes = new FileInfo(path).Length,
                    isStreamedSceneAssetBundle = type.GetProperty("isStreamedSceneAssetBundle").GetValue(bundle),
                    assetCount = assets.Length, sceneCount = scenes.Length,
                    entries = Page(entries, pageSize, pageNumber), totalEntries = entries.Count,
                    pageSize, pageNumber, nextPageNumber = NextPage(entries.Count, pageSize, pageNumber)
                });
            }
            catch (TargetInvocationException)
            {
                return new ErrorResponse("Unity could not inspect this AssetBundle. Check its build target and file integrity.");
            }
            finally
            {
                // Metadata inspection never loads assets. Release only the handle opened here.
                if (bundle != null) type.GetMethod("Unload", new[] { typeof(bool) }).Invoke(bundle, new object[] { false });
            }
        }

        private static object[] Page(List<object> entries, int pageSize, int pageNumber)
        {
            long offset = ((long)pageNumber - 1) * pageSize;
            return offset >= entries.Count ? Array.Empty<object>() : entries.Skip((int)offset).Take(pageSize).ToArray();
        }

        private static int? NextPage(int count, int pageSize, int pageNumber) => (long)pageNumber * pageSize < count ? (int?)(pageNumber + 1) : null;
    }
}
