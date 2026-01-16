using System;
using UnityEditor;
using UnityEngine;
using MCPForUnity.Editor.Helpers;

namespace MCPForUnity.Editor.ActionTrace.Core
{
    /// <summary>
    /// Cross-session stable object identifier for ActionTrace events.
    ///
    /// Uses Unity's GlobalObjectId (2020.3+) with fallback to Scene/Asset paths.
    /// This ensures that TargetId references survive domain reloads and editor restarts.
    ///
    /// Reuses existing Helpers:
    /// - GameObjectLookup.GetGameObjectPath() for Scene fallback paths
    /// - GameObjectLookup.FindById() for legacy InstanceID resolution
    /// </summary>
    public static class GlobalIdHelper
    {
        /// <summary>
        /// Prefix for fallback path format when GlobalObjectId is unavailable.
        /// Format: "Scene:{scenePath}@{hierarchyPath}" or "Asset:{assetPath}"
        /// </summary>
        private const string ScenePrefix = "Scene:";
        private const string AssetPrefix = "Asset:";
        private const string InstancePrefix = "Instance:";
        private const string PathSeparator = "@";

        /// <summary>
        /// Converts a UnityEngine.Object to a cross-session stable ID string.
        ///
        /// Priority:
        /// 1. GlobalObjectId (Unity 2020.3+) - Most stable
        /// 2. Scene path + hierarchy path (for GameObjects in scenes)
        /// 3. Asset path (for assets in Project view)
        /// 4. InstanceID (last resort - not cross-session stable)
        /// </summary>
        public static string ToGlobalIdString(UnityEngine.Object obj)
        {
            if (obj == null)
                return string.Empty;

#if UNITY_2020_3_OR_NEWER
            // Priority 1: Use Unity's built-in GlobalObjectId (most stable)
            var globalId = GlobalObjectId.GetGlobalObjectIdSlow(obj);
            // identifierType == 0 means invalid (not a scene object or asset)
            if (globalId.identifierType != 0)
            {
                return globalId.ToString();
            }
            // Fall through to fallback if GlobalObjectId is invalid
#endif

            // Priority 2 & 3: Use fallback paths (reuses GameObjectLookup)
            return GetFallbackId(obj);
        }

        /// <summary>
        /// Attempts to resolve a GlobalId string back to a Unity object.
        /// Returns null if the object no longer exists or the ID is invalid.
        /// </summary>
        public static UnityEngine.Object FromGlobalIdString(string globalIdStr)
        {
            if (string.IsNullOrEmpty(globalIdStr))
                return null;

#if UNITY_2020_3_OR_NEWER
            // Try parsing as GlobalObjectId first
            if (GlobalObjectId.TryParse(globalIdStr, out var globalId))
            {
                var obj = GlobalObjectId.GlobalObjectIdentifierToObjectSlow(globalId);
                if (obj != null)
                    return obj;
            }
#endif

            // Try parsing fallback formats
            return ParseFallbackId(globalIdStr);
        }

        /// <summary>
        /// Generates a fallback ID when GlobalObjectId is unavailable.
        ///
        /// Reuses existing Helpers:
        /// - GameObjectLookup.GetGameObjectPath() for Scene GameObject paths
        ///
        /// Formats:
        /// - Scene GameObject: "Scene:Assets/MyScene.unity@GameObject/Child/Target"
        /// - Asset: "Asset:Assets/Prefabs/MyPrefab.prefab"
        /// - Other: "Instance:12345" (not cross-session stable)
        /// </summary>
        private static string GetFallbackId(UnityEngine.Object obj)
        {
            // GameObjects in valid scenes: use scene path + hierarchy path
            if (obj is GameObject go && go.scene.IsValid())
            {
                // Reuse GameObjectLookup.GetGameObjectPath()
                string hierarchyPath = GameObjectLookup.GetGameObjectPath(go);
                return $"{ScenePrefix}{go.scene.path}{PathSeparator}{hierarchyPath}";
            }

            // Assets (ScriptableObject, Material, Texture, etc.): use AssetDatabase
            string assetPath = AssetDatabase.GetAssetPath(obj);
            if (!string.IsNullOrEmpty(assetPath))
            {
                return $"{AssetPrefix}{assetPath}";
            }

            // Last resort: InstanceID (not cross-session stable)
            return $"{InstancePrefix}{obj.GetInstanceID()}";
        }

        /// <summary>
        /// Parses a fallback ID string back to a Unity object.
        /// Handles Scene, Asset, and Instance formats.
        /// </summary>
        private static UnityEngine.Object ParseFallbackId(string idStr)
        {
            if (string.IsNullOrEmpty(idStr))
                return null;

            // Format: "Scene:{scenePath}@{hierarchyPath}"
            if (idStr.StartsWith(ScenePrefix))
            {
                int separatorIndex = idStr.IndexOf(PathSeparator);
                if (separatorIndex > 0)
                {
                    string scenePath = idStr.Substring(ScenePrefix.Length, separatorIndex - ScenePrefix.Length);
                    string hierarchyPath = idStr.Substring(separatorIndex + 1);

                    // Load the scene if not already loaded
                    var scene = UnityEditor.SceneManagement.EditorSceneManager.GetSceneByPath(scenePath);
                    if (!scene.IsValid())
                    {
                        // Scene not loaded - cannot resolve
                        return null;
                    }

                    // Find GameObject by hierarchy path
                    var found = GameObject.Find(hierarchyPath);
                    return found;
                }
            }

            // Format: "Asset:{assetPath}"
            if (idStr.StartsWith(AssetPrefix))
            {
                string assetPath = idStr.Substring(AssetPrefix.Length);
                return AssetDatabase.LoadMainAssetAtPath(assetPath);
            }

            // Format: "Instance:{instanceId}"
            // Reuse GameObjectLookup.FindById()
            if (idStr.StartsWith(InstancePrefix))
            {
                string instanceStr = idStr.Substring(InstancePrefix.Length);
                if (int.TryParse(instanceStr, out int instanceId))
                {
                    return GameObjectLookup.FindById(instanceId);
                }
            }

            return null;
        }

        /// <summary>
        /// Extracts a human-readable display name from a GlobalId string.
        /// Useful for ActionTrace Viewer UI display.
        /// Returns the object name if resolvable, otherwise a formatted ID string.
        /// </summary>
        public static string GetDisplayName(string globalIdStr)
        {
            if (string.IsNullOrEmpty(globalIdStr))
                return "<null>";

            // Try to resolve the object
            var obj = FromGlobalIdString(globalIdStr);
            if (obj != null)
                return obj.name;

            // Object not found, extract readable parts from ID
#if UNITY_2020_3_OR_NEWER
            if (GlobalObjectId.TryParse(globalIdStr, out var globalId))
            {
                return $"[{globalId.identifierType} {globalId.assetGUID.ToString().Substring(0, 8)}...]";
            }
#endif

            // Fallback format
            if (globalIdStr.StartsWith(ScenePrefix))
            {
                int separatorIndex = globalIdStr.IndexOf(PathSeparator);
                if (separatorIndex > 0)
                {
                    string hierarchyPath = globalIdStr.Substring(separatorIndex + 1);
                    // Extract just the object name (last part of path)
                    int lastSlash = hierarchyPath.LastIndexOf('/');
                    return lastSlash >= 0
                        ? hierarchyPath.Substring(lastSlash + 1)
                        : hierarchyPath;
                }
            }

            if (globalIdStr.StartsWith(AssetPrefix))
            {
                string assetPath = globalIdStr.Substring(AssetPrefix.Length);
                // Extract just the filename
                int lastSlash = assetPath.LastIndexOf('/');
                return lastSlash >= 0
                    ? assetPath.Substring(lastSlash + 1)
                    : assetPath;
            }

            // Truncate long IDs for display
            if (globalIdStr.Length > 50)
                return globalIdStr.Substring(0, 47) + "...";

            return globalIdStr;
        }

        /// <summary>
        /// Checks if a GlobalId string is valid (non-null and non-empty).
        /// </summary>
        public static bool IsValidId(string globalIdStr)
        {
            return !string.IsNullOrEmpty(globalIdStr);
        }

        /// <summary>
        /// Gets the type of an ID string (GlobalObjectId, Scene, Asset, Instance).
        /// Useful for debugging and categorization.
        /// </summary>
        public static GlobalIdType GetIdType(string globalIdStr)
        {
            if (string.IsNullOrEmpty(globalIdStr))
                return GlobalIdType.Invalid;

#if UNITY_2020_3_OR_NEWER
            if (GlobalObjectId.TryParse(globalIdStr, out var globalId))
                return GlobalIdType.GlobalObjectId;
#endif

            if (globalIdStr.StartsWith(ScenePrefix))
                return GlobalIdType.ScenePath;

            if (globalIdStr.StartsWith(AssetPrefix))
                return GlobalIdType.AssetPath;

            if (globalIdStr.StartsWith(InstancePrefix))
                return GlobalIdType.InstanceId;

            return GlobalIdType.Unknown;
        }
    }

    /// <summary>
    /// Type classification for GlobalId strings.
    /// </summary>
    public enum GlobalIdType
    {
        /// <summary>Null or empty string</summary>
        Invalid,
        /// <summary>Unity 2020.3+ GlobalObjectId format</summary>
        GlobalObjectId,
        /// <summary>"Scene:{path}@{hierarchy}" fallback format</summary>
        ScenePath,
        /// <summary>"Asset:{path}" fallback format</summary>
        AssetPath,
        /// <summary>"Instance:{id}" fallback format (not cross-session stable)</summary>
        InstanceId,
        /// <summary>Unknown format</summary>
        Unknown
    }
}
