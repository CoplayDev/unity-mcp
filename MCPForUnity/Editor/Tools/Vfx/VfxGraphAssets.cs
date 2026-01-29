using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
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

            // Find and copy template
            string templatePath = FindTemplate(template);
            VisualEffectAsset newAsset = null;

            if (!string.IsNullOrEmpty(templatePath) && System.IO.File.Exists(templatePath))
            {
                // templatePath is a full filesystem path, need to copy file directly
                // Get the full destination path
                string projectRoot = System.IO.Path.GetDirectoryName(Application.dataPath);
                string fullDestPath = System.IO.Path.Combine(projectRoot, assetPath);

                // Ensure directory exists
                string destDir = System.IO.Path.GetDirectoryName(fullDestPath);
                if (!System.IO.Directory.Exists(destDir))
                {
                    System.IO.Directory.CreateDirectory(destDir);
                }

                // Copy the file
                System.IO.File.Copy(templatePath, fullDestPath, true);
                AssetDatabase.Refresh();
                newAsset = AssetDatabase.LoadAssetAtPath<VisualEffectAsset>(assetPath);
            }
            else
            {
                // Create empty VFX asset using reflection to access internal API
                try
                {
                    // Try to use VisualEffectAssetEditorUtility.CreateNewAsset if available
                    var utilityType = Type.GetType("UnityEditor.VFX.VisualEffectAssetEditorUtility, Unity.VisualEffectGraph.Editor");
                    if (utilityType != null)
                    {
                        var createMethod = utilityType.GetMethod("CreateNewAsset", BindingFlags.Public | BindingFlags.Static);
                        if (createMethod != null)
                        {
                            createMethod.Invoke(null, new object[] { assetPath });
                            AssetDatabase.Refresh();
                            newAsset = AssetDatabase.LoadAssetAtPath<VisualEffectAsset>(assetPath);
                        }
                    }

                    // Fallback: Create a ScriptableObject-based asset
                    if (newAsset == null)
                    {
                        // Try direct creation via internal constructor
                        var resourceType = Type.GetType("UnityEditor.VFX.VisualEffectResource, Unity.VisualEffectGraph.Editor");
                        if (resourceType != null)
                        {
                            var createMethod = resourceType.GetMethod("CreateNewAsset", BindingFlags.Public | BindingFlags.Static | BindingFlags.NonPublic);
                            if (createMethod != null)
                            {
                                createMethod.Invoke(null, new object[] { assetPath });
                                AssetDatabase.Refresh();
                                newAsset = AssetDatabase.LoadAssetAtPath<VisualEffectAsset>(assetPath);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    return new { success = false, message = $"Failed to create VFX asset: {ex.Message}" };
                }
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
                if (!System.IO.Directory.Exists(basePath))
                {
                    continue;
                }

                foreach (string pattern in templatePatterns)
                {
                    string[] files = System.IO.Directory.GetFiles(basePath, pattern, System.IO.SearchOption.AllDirectories);
                    if (files.Length > 0)
                    {
                        return files[0];
                    }
                }

                // Also search by partial match
                try
                {
                    string[] allVfxFiles = System.IO.Directory.GetFiles(basePath, "*.vfx", System.IO.SearchOption.AllDirectories);
                    foreach (string file in allVfxFiles)
                    {
                        if (System.IO.Path.GetFileNameWithoutExtension(file).ToLower().Contains(templateName.ToLower()))
                        {
                            return file;
                        }
                    }
                }
                catch { }
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
                return assetPath;
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

            if (!assetPath.StartsWith("Assets/") && !assetPath.StartsWith("Packages/"))
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

                        templates.Add(new { name = name, path = projectRelativePath, source = isPackage ? "package" : "project" });
                    }
                }
                catch { }
            }

            // Also search project assets
            string[] guids = AssetDatabase.FindAssets("t:VisualEffectAsset");
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!templates.Any(t => ((dynamic)t).path == path))
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
#endif
    }
}
