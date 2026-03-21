using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using MCPForUnity.Editor.Helpers;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace MCPForUnity.Editor.Tools
{
    /// <summary>
    /// MCP tool for Asset Hunter Pro integration — unused assets, duplicates,
    /// dependencies, build reports, and exclusion settings.
    /// All access via reflection since AHP is an optional package.
    /// </summary>
    [McpForUnityTool("manage_asset_hunter", AutoRegister = false)]
    public static class ManageAssetHunter
    {
        private static readonly Type SettingsType;
        private static readonly Type SerializationHelperType;
        private static readonly Type DuplicateManagerType;
        private static readonly Type DependencyManagerType;
        private static readonly Type BuildInfoType;
        private static bool _initialized;

        static ManageAssetHunter()
        {
            try
            {
                var asm = GetAhpAssembly();
                if (asm == null) return;
                SettingsType = asm.GetType("HeurekaGames.AssetHunterPRO.AH_SettingsManager");
                SerializationHelperType = asm.GetType("HeurekaGames.AssetHunterPRO.AH_SerializationHelper");
                DuplicateManagerType = asm.GetType("HeurekaGames.AssetHunterPRO.AH_DuplicateDataManager");
                DependencyManagerType = asm.GetType(
                    "HeurekaGames.AssetHunterPRO.BaseTreeviewImpl.DependencyGraph.AH_DependencyGraphManager");
                BuildInfoType = asm.GetType("HeurekaGames.AssetHunterPRO.AH_SerializedBuildInfo");
                _initialized = SettingsType != null && SerializationHelperType != null;
            }
            catch (Exception e)
            {
                McpLog.Error($"[ManageAssetHunter] Init failed: {e.Message}");
            }
        }

        public static object HandleCommand(JObject @params)
        {
            if (!_initialized)
                return new ErrorResponse("Asset Hunter Pro not found. Is the package installed?");

            var p = new ToolParams(@params);
            string action = p.Get("action", "get_build_report").ToLower();
            try
            {
                return action switch
                {
                    "scan_unused" => ScanUnused(p),
                    "get_duplicates" => GetDuplicates(p),
                    "get_dependencies" => GetDependencies(p),
                    "get_build_report" => GetBuildReport(),
                    "get_settings" => GetSettings(),
                    _ => new ErrorResponse(
                        $"Unknown action '{action}'. Valid: scan_unused, get_duplicates, get_dependencies, get_build_report, get_settings")
                };
            }
            catch (Exception e)
            {
                McpLog.Error($"[ManageAssetHunter] {action} failed: {e}");
                return new ErrorResponse($"Error in {action}: {e.Message}");
            }
        }

        private static object ScanUnused(ToolParams p)
        {
            int pageSize = p.GetInt("page_size") ?? 50;
            int cursor = p.GetInt("cursor") ?? 0;
            string filterType = p.Get("filter_type");

            string buildInfoFolder = InvokeStatic<string>(SerializationHelperType, "GetBuildInfoFolder");
            if (!Directory.Exists(buildInfoFolder))
                return new ErrorResponse($"No build info folder at '{buildInfoFolder}'. Run a build first.");

            var files = Directory.GetFiles(buildInfoFolder, "*.ahbuildinfo")
                .OrderByDescending(File.GetLastWriteTime).ToArray();
            if (files.Length == 0)
                return new ErrorResponse("No .ahbuildinfo files found. Run a build with Asset Hunter Pro enabled.");

            var buildInfo = InvokeStatic<object>(SerializationHelperType, "LoadBuildReport",
                BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public, files[0]);
            if (buildInfo == null)
                return new ErrorResponse("Failed to parse latest build report.");

            var assetList = GetField<object>(buildInfo, "AssetListUnSorted") as System.Collections.IList;
            if (assetList == null)
                return new ErrorResponse("Build report has no asset list.");

            // Collect unused assets
            var unused = new List<object>();
            foreach (var asset in assetList)
            {
                string id = GetField<string>(asset, "ID");
                var refs = GetField<object>(asset, "Refs") as List<string>;
                bool usedInBuild = refs != null && refs.Count > 0;
                if (usedInBuild) continue;

                string path = AssetDatabase.GUIDToAssetPath(id);
                if (string.IsNullOrEmpty(path) || !path.StartsWith("Assets")) continue;

                // Apply type filter
                if (!string.IsNullOrEmpty(filterType))
                {
                    var obj = AssetDatabase.LoadMainAssetAtPath(path);
                    if (obj == null || !obj.GetType().Name.Equals(filterType, StringComparison.OrdinalIgnoreCase))
                        continue;
                }

                long size = 0;
                try { var fi = new FileInfo(path); if (fi.Exists) size = fi.Length; } catch { }
                string typeName = AssetDatabase.GetMainAssetTypeAtPath(path)?.Name ?? "Unknown";

                unused.Add(new { path, size, type = typeName, guid = id });
            }

            int total = unused.Count;
            var page = unused.Skip(cursor).Take(pageSize).ToList();
            bool truncated = cursor + pageSize < total;

            return new SuccessResponse($"Found {total} unused assets.", new
            {
                cursor, pageSize, total, truncated,
                nextCursor = truncated ? (int?)(cursor + pageSize) : null,
                buildFile = Path.GetFileName(files[0]),
                items = page
            });
        }

        private static object GetDuplicates(ToolParams p)
        {
            if (DuplicateManagerType == null)
                return new ErrorResponse("AH_DuplicateDataManager type not found.");

            int pageSize = p.GetInt("page_size") ?? 50;
            int cursor = p.GetInt("cursor") ?? 0;

            var instance = GetSingletonInstance(DuplicateManagerType);
            if (instance == null)
                return new ErrorResponse("Could not access DuplicateDataManager singleton.");

            bool isDirty = GetField<bool>(instance, "IsDirty");
            if (isDirty)
            {
                DuplicateManagerType.GetMethod("RefreshData", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                    ?.Invoke(instance, null);
            }

            var entries = GetProperty<object>(instance, "Entries") as System.Collections.IDictionary;
            if (entries == null || entries.Count == 0)
                return new SuccessResponse("No duplicate assets found.", new { total = 0, items = new List<object>() });

            var groups = new List<object>();
            foreach (System.Collections.DictionaryEntry kvp in entries)
            {
                string hash = kvp.Key as string;
                var data = kvp.Value;
                var paths = GetProperty<object>(data, "Paths") as List<string>;
                var guids = GetField<object>(data, "Guids") as List<string>;
                if (paths == null || paths.Count < 2) continue;

                long totalSize = 0;
                foreach (var path in paths)
                {
                    try { var fi = new FileInfo(path); if (fi.Exists) totalSize += fi.Length; } catch { }
                }

                groups.Add(new { hash, fileCount = paths.Count, totalSize, paths, guids });
            }

            int total = groups.Count;
            var page = groups.Skip(cursor).Take(pageSize).ToList();
            bool truncated = cursor + pageSize < total;

            return new SuccessResponse($"Found {total} duplicate groups.", new
            {
                cursor, pageSize, total, truncated,
                nextCursor = truncated ? (int?)(cursor + pageSize) : null,
                items = page
            });
        }

        private static object GetDependencies(ToolParams p)
        {
            string assetPath = p.Get("asset_path");
            if (string.IsNullOrEmpty(assetPath))
                return new ErrorResponse("asset_path is required.");

            string direction = p.Get("direction", "references").ToLower();
            if (direction != "references" && direction != "referenced_by")
                return new ErrorResponse("direction must be 'references' or 'referenced_by'.");

            if (!File.Exists(assetPath) && !AssetDatabase.IsValidFolder(assetPath))
                return new ErrorResponse($"Asset not found: {assetPath}");

            if (direction == "references")
            {
                var deps = AssetDatabase.GetDependencies(assetPath, false)
                    .Where(d => d != assetPath).ToList();
                return new SuccessResponse($"{deps.Count} references from '{assetPath}'.", new
                {
                    assetPath, direction, total = deps.Count,
                    items = deps.Select(d => new
                    {
                        path = d,
                        type = AssetDatabase.GetMainAssetTypeAtPath(d)?.Name ?? "Unknown"
                    })
                });
            }
            else
            {
                // referenced_by: scan all assets for references to this asset
                string targetGuid = AssetDatabase.AssetPathToGUID(assetPath);
                var referencedBy = new List<object>();
                foreach (var path in AssetDatabase.GetAllAssetPaths())
                {
                    if (!path.StartsWith("Assets") || path == assetPath) continue;
                    var deps = AssetDatabase.GetDependencies(path, false);
                    if (deps.Contains(assetPath))
                    {
                        referencedBy.Add(new
                        {
                            path,
                            type = AssetDatabase.GetMainAssetTypeAtPath(path)?.Name ?? "Unknown"
                        });
                    }
                }

                return new SuccessResponse($"{referencedBy.Count} assets reference '{assetPath}'.", new
                {
                    assetPath, direction, total = referencedBy.Count, items = referencedBy
                });
            }
        }

        private static object GetBuildReport()
        {
            string buildInfoFolder = InvokeStatic<string>(SerializationHelperType, "GetBuildInfoFolder");
            if (!Directory.Exists(buildInfoFolder))
                return new ErrorResponse($"No build info folder at '{buildInfoFolder}'.");

            var files = Directory.GetFiles(buildInfoFolder, "*.ahbuildinfo")
                .OrderByDescending(File.GetLastWriteTime).ToArray();
            if (files.Length == 0)
                return new ErrorResponse("No .ahbuildinfo files found.");

            var buildInfo = InvokeStatic<object>(SerializationHelperType, "LoadBuildReport",
                BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public, files[0]);
            if (buildInfo == null)
                return new ErrorResponse("Failed to parse build report.");

            var assetList = GetField<object>(buildInfo, "AssetListUnSorted") as System.Collections.IList;
            int totalAssets = assetList?.Count ?? 0;
            int usedCount = 0;
            long totalSize = 0;
            var typeBreakdown = new Dictionary<string, int>();

            if (assetList != null)
            {
                foreach (var asset in assetList)
                {
                    string id = GetField<string>(asset, "ID");
                    var refs = GetField<object>(asset, "Refs") as List<string>;
                    bool used = refs != null && refs.Count > 0;
                    if (used) usedCount++;

                    string path = AssetDatabase.GUIDToAssetPath(id);
                    if (!string.IsNullOrEmpty(path))
                    {
                        string typeName = AssetDatabase.GetMainAssetTypeAtPath(path)?.Name ?? "Unknown";
                        typeBreakdown.TryGetValue(typeName, out int count);
                        typeBreakdown[typeName] = count + 1;
                        try { var fi = new FileInfo(path); if (fi.Exists) totalSize += fi.Length; } catch { }
                    }
                }
            }

            return new SuccessResponse("Build report summary.", new
            {
                buildFile = Path.GetFileName(files[0]),
                buildTarget = GetField<string>(buildInfo, "buildTargetInfo"),
                buildDate = GetField<string>(buildInfo, "dateTime"),
                totalAssets, usedCount, unusedCount = totalAssets - usedCount,
                totalSizeBytes = totalSize,
                typeBreakdown = typeBreakdown.OrderByDescending(kv => kv.Value)
                    .ToDictionary(kv => kv.Key, kv => kv.Value),
                availableReports = files.Select(Path.GetFileName).ToList()
            });
        }

        private static object GetSettings()
        {
            var instance = SettingsType.GetProperty("Instance",
                BindingFlags.Static | BindingFlags.Public)?.GetValue(null);
            if (instance == null)
                return new ErrorResponse("Could not access AH_SettingsManager.Instance.");

            return new SuccessResponse("Asset Hunter Pro settings.", new
            {
                autoCreateLog = GetProperty<bool>(instance, "AutoCreateLog"),
                autoOpenLog = GetProperty<bool>(instance, "AutoOpenLog"),
                autoRefreshLog = GetProperty<bool>(instance, "AutoRefreshLog"),
                estimateAssetSize = GetProperty<bool>(instance, "EstimateAssetSize"),
                ignoreScriptFiles = GetProperty<bool>(instance, "IgnoreScriptFiles"),
                buildInfoPath = GetProperty<string>(instance, "BuildInfoPath"),
                userPreferencePath = GetProperty<string>(instance, "UserPreferencePath"),
                ignoredPathEndings = InvokeInstance<List<string>>(instance, "GetIgnoredPathEndsWith"),
                ignoredExtensions = InvokeInstance<List<string>>(instance, "GetIgnoredFileExtentions"),
                ignoredFiles = InvokeInstance<List<string>>(instance, "GetIgnoredFiles"),
                ignoredFolders = InvokeInstance<List<string>>(instance, "GetIgnoredFolders"),
                dependencyDepth = GetProperty<int>(instance, "DependencyDepth"),
                maxRefCount = GetProperty<int>(instance, "MaxRefCount")
            });
        }

        // --- Reflection helpers ---

        private static Assembly GetAhpAssembly()
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                if (asm.GetName().Name == "HeurekaGames.AssetHunterPRO.Editor") return asm;
            return null;
        }

        private static object GetSingletonInstance(Type type)
        {
            var prop = type.GetProperty("instance",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.FlattenHierarchy);
            return prop?.GetValue(null);
        }

        private static T GetField<T>(object obj, string name)
        {
            var field = obj.GetType().GetField(name,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            return field != null ? (T)field.GetValue(obj) : default;
        }

        private static T GetProperty<T>(object obj, string name)
        {
            var prop = obj.GetType().GetProperty(name,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            return prop != null ? (T)prop.GetValue(obj) : default;
        }

        private static T InvokeStatic<T>(Type type, string method,
            BindingFlags flags = BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public,
            params object[] args)
        {
            var m = type.GetMethod(method, flags);
            return m != null ? (T)m.Invoke(null, args.Length > 0 ? args : null) : default;
        }

        private static T InvokeInstance<T>(object obj, string method)
        {
            var m = obj.GetType().GetMethod(method,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            return m != null ? (T)m.Invoke(obj, null) : default;
        }
    }
}
