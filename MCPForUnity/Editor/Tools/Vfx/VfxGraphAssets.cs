using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

#if UNITY_VFX_GRAPH
using UnityEngine.VFX;
#endif

namespace MCPForUnity.Editor.Tools.Vfx
{
    /// <summary>
    /// Asset management operations for VFX Graph.
    /// Handles creating, assigning, and listing VFX assets.
    /// Requires com.unity.visualeffectgraph package and UNITY_VFX_GRAPH symbol.
    /// </summary>
    internal static class VfxGraphAssets
    {
#if !UNITY_VFX_GRAPH
        public static object CreateAsset(JObject @params)
        {
            return new { success = false, message = "VFX Graph package (com.unity.visualeffectgraph) not installed" };
        }

        public static object AssignAsset(JObject @params)
        {
            return new { success = false, message = "VFX Graph package (com.unity.visualeffectgraph) not installed" };
        }

        public static object ListTemplates(JObject @params)
        {
            return new { success = false, message = "VFX Graph package (com.unity.visualeffectgraph) not installed" };
        }

        public static object ListAssets(JObject @params)
        {
            return new { success = false, message = "VFX Graph package (com.unity.visualeffectgraph) not installed" };
        }
#else
        private static readonly string[] SupportedVfxGraphVersions = { "12.1" };

        /// <summary>
        /// Creates a new VFX Graph asset file from a template.
        /// </summary>
        public static object CreateAsset(JObject @params)
        {
            string assetName = @params["assetName"]?.ToString();
            string folderPath = @params["folderPath"]?.ToString() ?? "Assets/VFX";
            string template = @params["template"]?.ToString() ?? "empty";

            if (string.IsNullOrEmpty(assetName))
            {
                return new { success = false, message = "assetName is required" };
            }

            string versionError = ValidateVfxGraphVersion();
            if (!string.IsNullOrEmpty(versionError))
            {
                return new { success = false, message = versionError };
            }

            // Ensure folder exists
            if (!AssetDatabase.IsValidFolder(folderPath))
            {
                string[] folders = folderPath.Split('/');
                string currentPath = folders[0];
                for (int i = 1; i < folders.Length; i++)
                {
                    string newPath = currentPath + "/" + folders[i];
                    if (!AssetDatabase.IsValidFolder(newPath))
                    {
                        AssetDatabase.CreateFolder(currentPath, folders[i]);
                    }
                    currentPath = newPath;
                }
            }

            string assetPath = $"{folderPath}/{assetName}.vfx";

            // Check if asset already exists
            if (AssetDatabase.LoadAssetAtPath<VisualEffectAsset>(assetPath) != null)
            {
                bool overwrite = @params["overwrite"]?.ToObject<bool>() ?? false;
                if (!overwrite)
                {
                    return new { success = false, message = $"Asset already exists at {assetPath}. Set overwrite=true to replace." };
                }
                AssetDatabase.DeleteAsset(assetPath);
            }

            // Find template asset and copy it
            string templatePath = FindTemplate(template);
            string templateAssetPath = TryGetAssetPathFromFileSystem(templatePath);
            VisualEffectAsset newAsset = null;

            if (!string.IsNullOrEmpty(templateAssetPath))
            {
                // Copy the asset to create a new VFX Graph asset
                if (!AssetDatabase.CopyAsset(templateAssetPath, assetPath))
                {
                    return new { success = false, message = $"Failed to copy VFX template from {templateAssetPath}" };
                }
                AssetDatabase.Refresh();
                newAsset = AssetDatabase.LoadAssetAtPath<VisualEffectAsset>(assetPath);
            }
            else
            {
                return new { success = false, message = "VFX template not found. Add a .vfx template asset or install VFX Graph templates." };
            }

            if (newAsset == null)
            {
                return new { success = false, message = "Failed to create VFX asset. Try using a template from list_templates." };
            }

            return new
            {
                success = true,
                message = $"Created VFX asset: {assetPath}",
                data = new
                {
                    assetPath = assetPath,
                    assetName = newAsset.name,
                    template = template
                }
            };
        }

        /// <summary>
        /// Finds VFX template path by name.
        /// </summary>
        private static string FindTemplate(string templateName)
        {
            // Get the actual filesystem path for the VFX Graph package using PackageManager API
            var packageInfo = UnityEditor.PackageManager.PackageInfo.FindForAssetPath("Packages/com.unity.visualeffectgraph");

            var searchPaths = new List<string>();

            if (packageInfo != null)
            {
                // Use the resolved path from PackageManager (handles Library/PackageCache paths)
                searchPaths.Add(System.IO.Path.Combine(packageInfo.resolvedPath, "Editor/Templates"));
                searchPaths.Add(System.IO.Path.Combine(packageInfo.resolvedPath, "Samples"));
            }

            // Also search project-local paths
            searchPaths.Add("Assets/VFX/Templates");

            string[] templatePatterns = new[]
            {
                $"{templateName}.vfx",
                $"VFX{templateName}.vfx",
                $"Simple{templateName}.vfx",
                $"{templateName}VFX.vfx"
            };

            foreach (string basePath in searchPaths)
            {
                string searchRoot = basePath;
                if (basePath.StartsWith("Assets/"))
                {
                    searchRoot = System.IO.Path.Combine(UnityEngine.Application.dataPath, basePath.Substring("Assets/".Length));
                }

                if (!System.IO.Directory.Exists(searchRoot))
                {
                    continue;
                }

                foreach (string pattern in templatePatterns)
                {
                    string[] files = System.IO.Directory.GetFiles(searchRoot, pattern, System.IO.SearchOption.AllDirectories);
                    if (files.Length > 0)
                    {
                        return files[0];
                    }
                }

                // Also search by partial match
                try
                {
                    string[] allVfxFiles = System.IO.Directory.GetFiles(searchRoot, "*.vfx", System.IO.SearchOption.AllDirectories);
                    foreach (string file in allVfxFiles)
                    {
                        if (System.IO.Path.GetFileNameWithoutExtension(file).ToLower().Contains(templateName.ToLower()))
                        {
                            return file;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"Failed to search VFX templates under '{searchRoot}': {ex.Message}");
                }
            }

            // Search in project assets
            string[] guids = AssetDatabase.FindAssets("t:VisualEffectAsset " + templateName);
            if (guids.Length > 0)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(guids[0]);
                // Convert asset path (e.g., "Assets/...") to absolute filesystem path
                if (!string.IsNullOrEmpty(assetPath) && assetPath.StartsWith("Assets/"))
                {
                    return System.IO.Path.Combine(UnityEngine.Application.dataPath, assetPath.Substring("Assets/".Length));
                }
                if (!string.IsNullOrEmpty(assetPath) && assetPath.StartsWith("Packages/"))
                {
                    var info = UnityEditor.PackageManager.PackageInfo.FindForAssetPath(assetPath);
                    if (info != null)
                    {
                        string relPath = assetPath.Substring(("Packages/" + info.name + "/").Length);
                        return System.IO.Path.Combine(info.resolvedPath, relPath);
                    }
                }
                return null;
            }

            return null;
        }

        /// <summary>
        /// Assigns a VFX asset to a VisualEffect component.
        /// </summary>
        public static object AssignAsset(JObject @params)
        {
            VisualEffect vfx = VfxGraphCommon.FindVisualEffect(@params);
            if (vfx == null)
            {
                return new { success = false, message = "VisualEffect component not found" };
            }

            string assetPath = @params["assetPath"]?.ToString();
            if (string.IsNullOrEmpty(assetPath))
            {
                return new { success = false, message = "assetPath is required" };
            }

            // Validate and normalize path
            // Reject absolute paths, parent directory traversal, and backslashes
            if (assetPath.Contains("\\") || assetPath.Contains("..") || System.IO.Path.IsPathRooted(assetPath))
            {
                return new { success = false, message = "Invalid assetPath: traversal and absolute paths are not allowed" };
            }

            if (assetPath.StartsWith("Packages/"))
            {
                return new { success = false, message = "Invalid assetPath: VFX assets must live under Assets/." };
            }

            if (!assetPath.StartsWith("Assets/"))
            {
                assetPath = "Assets/" + assetPath;
            }
            if (!assetPath.EndsWith(".vfx"))
            {
                assetPath += ".vfx";
            }

            // Verify the normalized path doesn't escape the project
            string fullPath = System.IO.Path.Combine(UnityEngine.Application.dataPath, assetPath.Substring("Assets/".Length));
            string canonicalProjectRoot = System.IO.Path.GetFullPath(UnityEngine.Application.dataPath);
            string canonicalAssetPath = System.IO.Path.GetFullPath(fullPath);
            if (!canonicalAssetPath.StartsWith(canonicalProjectRoot + System.IO.Path.DirectorySeparatorChar) &&
                canonicalAssetPath != canonicalProjectRoot)
            {
                return new { success = false, message = "Invalid assetPath: would escape project directory" };
            }

            var asset = AssetDatabase.LoadAssetAtPath<VisualEffectAsset>(assetPath);
            if (asset == null)
            {
                // Try searching by name
                string searchName = System.IO.Path.GetFileNameWithoutExtension(assetPath);
                string[] guids = AssetDatabase.FindAssets($"t:VisualEffectAsset {searchName}");
                if (guids.Length > 0)
                {
                    assetPath = AssetDatabase.GUIDToAssetPath(guids[0]);
                    asset = AssetDatabase.LoadAssetAtPath<VisualEffectAsset>(assetPath);
                }
            }

            if (asset == null)
            {
                return new { success = false, message = $"VFX asset not found: {assetPath}" };
            }

            Undo.RecordObject(vfx, "Assign VFX Asset");
            vfx.visualEffectAsset = asset;
            EditorUtility.SetDirty(vfx);

            return new
            {
                success = true,
                message = $"Assigned VFX asset '{asset.name}' to {vfx.gameObject.name}",
                data = new
                {
                    gameObject = vfx.gameObject.name,
                    assetName = asset.name,
                    assetPath = assetPath
                }
            };
        }

        /// <summary>
        /// Lists available VFX templates.
        /// </summary>
        public static object ListTemplates(JObject @params)
        {
            var templates = new List<object>();
            var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Get the actual filesystem path for the VFX Graph package using PackageManager API
            var packageInfo = UnityEditor.PackageManager.PackageInfo.FindForAssetPath("Packages/com.unity.visualeffectgraph");

            var searchPaths = new List<string>();

            if (packageInfo != null)
            {
                // Use the resolved path from PackageManager (handles Library/PackageCache paths)
                searchPaths.Add(System.IO.Path.Combine(packageInfo.resolvedPath, "Editor/Templates"));
                searchPaths.Add(System.IO.Path.Combine(packageInfo.resolvedPath, "Samples"));
            }

            // Also search project-local paths
            searchPaths.Add("Assets/VFX/Templates");
            searchPaths.Add("Assets/VFX");

            // Precompute normalized package path for comparison
            string normalizedPackagePath = null;
            if (packageInfo != null)
            {
                normalizedPackagePath = packageInfo.resolvedPath.Replace("\\", "/");
            }

            // Precompute the Assets base path for converting absolute paths to project-relative
            string assetsBasePath = Application.dataPath.Replace("\\", "/");

            foreach (string basePath in searchPaths)
            {
                if (!System.IO.Directory.Exists(basePath))
                {
                    continue;
                }

                try
                {
                    string[] vfxFiles = System.IO.Directory.GetFiles(basePath, "*.vfx", System.IO.SearchOption.AllDirectories);
                    foreach (string file in vfxFiles)
                    {
                        string absolutePath = file.Replace("\\", "/");
                        string name = System.IO.Path.GetFileNameWithoutExtension(file);
                        bool isPackage = normalizedPackagePath != null && absolutePath.StartsWith(normalizedPackagePath);

                        // Convert absolute path to project-relative path
                        string projectRelativePath;
                        if (isPackage)
                        {
                            // For package paths, convert to Packages/... format
                            projectRelativePath = "Packages/" + packageInfo.name + absolutePath.Substring(normalizedPackagePath.Length);
                        }
                        else if (absolutePath.StartsWith(assetsBasePath))
                        {
                            // For project assets, convert to Assets/... format
                            projectRelativePath = "Assets" + absolutePath.Substring(assetsBasePath.Length);
                        }
                        else
                        {
                            // Fallback: use the absolute path if we can't determine the relative path
                            projectRelativePath = absolutePath;
                        }

                        string normalizedPath = projectRelativePath.Replace("\\", "/");
                        if (seenPaths.Add(normalizedPath))
                        {
                            templates.Add(new { name = name, path = projectRelativePath, source = isPackage ? "package" : "project" });
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"Failed to list VFX templates under '{basePath}': {ex.Message}");
                }
            }

            // Also search project assets
            string[] guids = AssetDatabase.FindAssets("t:VisualEffectAsset");
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                string normalizedPath = path.Replace("\\", "/");
                if (seenPaths.Add(normalizedPath))
                {
                    string name = System.IO.Path.GetFileNameWithoutExtension(path);
                    templates.Add(new { name = name, path = path, source = "project" });
                }
            }

            return new
            {
                success = true,
                data = new
                {
                    count = templates.Count,
                    templates = templates
                }
            };
        }

        /// <summary>
        /// Lists all VFX assets in the project.
        /// </summary>
        public static object ListAssets(JObject @params)
        {
            string searchFolder = @params["folder"]?.ToString();
            string searchPattern = @params["search"]?.ToString();

            string filter = "t:VisualEffectAsset";
            if (!string.IsNullOrEmpty(searchPattern))
            {
                filter += " " + searchPattern;
            }

            string[] guids;
            if (!string.IsNullOrEmpty(searchFolder))
            {
                if (searchFolder.Contains("\\") || searchFolder.Contains("..") || System.IO.Path.IsPathRooted(searchFolder))
                {
                    return new { success = false, message = "Invalid folder: traversal and absolute paths are not allowed" };
                }

                if (searchFolder.StartsWith("Packages/"))
                {
                    return new { success = false, message = "Invalid folder: VFX assets must live under Assets/." };
                }

                if (!searchFolder.StartsWith("Assets/"))
                {
                    searchFolder = "Assets/" + searchFolder;
                }

                string fullPath = System.IO.Path.Combine(UnityEngine.Application.dataPath, searchFolder.Substring("Assets/".Length));
                string canonicalProjectRoot = System.IO.Path.GetFullPath(UnityEngine.Application.dataPath);
                string canonicalSearchFolder = System.IO.Path.GetFullPath(fullPath);
                if (!canonicalSearchFolder.StartsWith(canonicalProjectRoot + System.IO.Path.DirectorySeparatorChar) &&
                    canonicalSearchFolder != canonicalProjectRoot)
                {
                    return new { success = false, message = "Invalid folder: would escape project directory" };
                }

                guids = AssetDatabase.FindAssets(filter, new[] { searchFolder });
            }
            else
            {
                guids = AssetDatabase.FindAssets(filter);
            }

            var assets = new List<object>();
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var asset = AssetDatabase.LoadAssetAtPath<VisualEffectAsset>(path);
                if (asset != null)
                {
                    assets.Add(new
                    {
                        name = asset.name,
                        path = path,
                        guid = guid
                    });
                }
            }

            return new
            {
                success = true,
                data = new
                {
                    count = assets.Count,
                    assets = assets
                }
            };
        }

        private static string ValidateVfxGraphVersion()
        {
            var info = UnityEditor.PackageManager.PackageInfo.FindForAssetPath("Packages/com.unity.visualeffectgraph");
            if (info == null)
            {
                return "VFX Graph package (com.unity.visualeffectgraph) not installed";
            }

            if (IsVersionSupported(info.version))
            {
                return null;
            }

            string supported = string.Join(", ", SupportedVfxGraphVersions.Select(version => $"{version}.x"));
            return $"Unsupported VFX Graph version {info.version}. Supported versions: {supported}.";
        }

        private static bool IsVersionSupported(string installedVersion)
        {
            if (string.IsNullOrEmpty(installedVersion))
            {
                return false;
            }

            string normalized = installedVersion;
            int suffixIndex = normalized.IndexOfAny(new[] { '-', '+' });
            if (suffixIndex >= 0)
            {
                normalized = normalized.Substring(0, suffixIndex);
            }

            if (!Version.TryParse(normalized, out Version installed))
            {
                return false;
            }

            foreach (string supported in SupportedVfxGraphVersions)
            {
                if (!Version.TryParse(supported, out Version target))
                {
                    continue;
                }

                if (installed.Major == target.Major && installed.Minor == target.Minor)
                {
                    return true;
                }
            }

            return false;
        }

        private static string TryGetAssetPathFromFileSystem(string templatePath)
        {
            if (string.IsNullOrEmpty(templatePath))
            {
                return null;
            }

            string normalized = templatePath.Replace("\\", "/");
            string assetsRoot = Application.dataPath.Replace("\\", "/");

            if (normalized.StartsWith(assetsRoot + "/"))
            {
                return "Assets/" + normalized.Substring(assetsRoot.Length + 1);
            }

            var packageInfo = UnityEditor.PackageManager.PackageInfo.FindForAssetPath("Packages/com.unity.visualeffectgraph");
            if (packageInfo != null)
            {
                string packageRoot = packageInfo.resolvedPath.Replace("\\", "/");
                if (normalized.StartsWith(packageRoot + "/"))
                {
                    return "Packages/" + packageInfo.name + "/" + normalized.Substring(packageRoot.Length + 1);
                }
            }

            return null;
        }
#endif
    }
}
