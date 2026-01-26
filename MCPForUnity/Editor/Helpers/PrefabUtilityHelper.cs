using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace MCPForUnity.Editor.Helpers
{
    /// <summary>
    /// Provides common utility methods for working with Unity Prefab assets.
    /// </summary>
    public static class PrefabUtilityHelper
    {
        /// <summary>
        /// Gets the GUID for a prefab asset path.
        /// </summary>
        /// <param name="assetPath">The Unity asset path (e.g., "Assets/Prefabs/MyPrefab.prefab")</param>
        /// <returns>The GUID string, or null if the path is invalid.</returns>
        public static string GetPrefabGUID(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath))
            {
                return null;
            }

            try
            {
                return AssetDatabase.AssetPathToGUID(assetPath);
            }
            catch (Exception ex)
            {
                McpLog.Warn($"Failed to get GUID for asset path '{assetPath}': {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Gets variant information if the prefab is a variant.
        /// </summary>
        /// <param name="prefabAsset">The prefab GameObject to check.</param>
        /// <returns>A tuple containing (isVariant, parentPath, parentGuid).</returns>
        public static (bool isVariant, string parentPath, string parentGuid) GetVariantInfo(GameObject prefabAsset)
        {
            if (prefabAsset == null)
            {
                return (false, null, null);
            }

            try
            {
                PrefabAssetType assetType = PrefabUtility.GetPrefabAssetType(prefabAsset);
                if (assetType != PrefabAssetType.Variant)
                {
                    return (false, null, null);
                }

                GameObject parentAsset = PrefabUtility.GetCorrespondingObjectFromSource(prefabAsset);
                if (parentAsset == null)
                {
                    return (true, null, null);
                }

                string parentPath = AssetDatabase.GetAssetPath(parentAsset);
                string parentGuid = GetPrefabGUID(parentPath);

                return (true, parentPath, parentGuid);
            }
            catch (Exception ex)
            {
                McpLog.Warn($"Failed to get variant info for '{prefabAsset.name}': {ex.Message}");
                return (false, null, null);
            }
        }

        /// <summary>
        /// Gets the list of component type names on a GameObject.
        /// </summary>
        /// <param name="obj">The GameObject to inspect.</param>
        /// <returns>A list of component type full names.</returns>
        public static List<string> GetComponentTypeNames(GameObject obj)
        {
            var typeNames = new List<string>();

            if (obj == null)
            {
                return typeNames;
            }

            try
            {
                var components = obj.GetComponents<Component>();
                foreach (var component in components)
                {
                    if (component != null)
                    {
                        typeNames.Add(component.GetType().FullName);
                    }
                }
            }
            catch (Exception ex)
            {
                McpLog.Warn($"Failed to get component types for '{obj.name}': {ex.Message}");
            }

            return typeNames;
        }

        /// <summary>
        /// Recursively counts all children in the hierarchy.
        /// </summary>
        /// <param name="transform">The root transform to count from.</param>
        /// <returns>Total number of children in the hierarchy.</returns>
        public static int CountChildrenRecursive(Transform transform)
        {
            if (transform == null)
            {
                return 0;
            }

            int count = transform.childCount;
            for (int i = 0; i < transform.childCount; i++)
            {
                count += CountChildrenRecursive(transform.GetChild(i));
            }
            return count;
        }

        /// <summary>
        /// Gets the source prefab path for a nested prefab instance.
        /// </summary>
        /// <param name="gameObject">The GameObject to check.</param>
        /// <returns>The asset path of the source prefab, or null if not a nested prefab.</returns>
        public static string GetNestedPrefabPath(GameObject gameObject)
        {
            if (gameObject == null || !PrefabUtility.IsAnyPrefabInstanceRoot(gameObject))
            {
                return null;
            }

            try
            {
                var sourcePrefab = PrefabUtility.GetCorrespondingObjectFromSource(gameObject);
                if (sourcePrefab != null)
                {
                    return AssetDatabase.GetAssetPath(sourcePrefab);
                }
            }
            catch (Exception ex)
            {
                McpLog.Warn($"Failed to get nested prefab path for '{gameObject.name}': {ex.Message}");
            }

            return null;
        }

        /// <summary>
        /// Gets the nesting depth of a prefab instance within the prefab hierarchy.
        /// Returns 0 for main prefab root, 1 for first-level nested, 2 for second-level, etc.
        /// Returns -1 for non-prefab-root objects.
        /// </summary>
        /// <param name="gameObject">The GameObject to analyze.</param>
        /// <param name="mainPrefabRoot">The root transform of the main prefab asset.</param>
        /// <returns>Nesting depth (0=main root, 1+=nested), or -1 if not a prefab root.</returns>
        public static int GetPrefabNestingDepth(GameObject gameObject, Transform mainPrefabRoot)
        {
            if (gameObject == null)
                return -1;

            // Main prefab root
            if (gameObject.transform == mainPrefabRoot)
                return 0;

            // Not a prefab instance root
            if (!PrefabUtility.IsAnyPrefabInstanceRoot(gameObject))
                return -1;

            // Calculate depth by walking up the hierarchy
            int depth = 0;
            Transform current = gameObject.transform;

            while (current != null && current != mainPrefabRoot)
            {
                if (PrefabUtility.IsAnyPrefabInstanceRoot(current.gameObject))
                {
                    depth++;
                }
                current = current.parent;
            }

            return depth;
        }

        /// <summary>
        /// Gets the parent prefab path for a nested prefab instance.
        /// Returns null for main prefab root or non-prefab objects.
        /// </summary>
        /// <param name="gameObject">The GameObject to analyze.</param>
        /// <param name="mainPrefabRoot">The root transform of the main prefab asset.</param>
        /// <returns>The asset path of the parent prefab, or null if none.</returns>
        public static string GetParentPrefabPath(GameObject gameObject, Transform mainPrefabRoot)
        {
            if (gameObject == null || gameObject.transform == mainPrefabRoot)
                return null;

            if (!PrefabUtility.IsAnyPrefabInstanceRoot(gameObject))
                return null;

            // Walk up the hierarchy to find the parent prefab instance
            Transform current = gameObject.transform.parent;

            while (current != null && current != mainPrefabRoot)
            {
                if (PrefabUtility.IsAnyPrefabInstanceRoot(current.gameObject))
                {
                    return GetNestedPrefabPath(current.gameObject);
                }
                current = current.parent;
            }

            // Parent is the main prefab root - get its asset path
            if (mainPrefabRoot != null)
            {
                return AssetDatabase.GetAssetPath(mainPrefabRoot.gameObject);
            }

            return null;
        }
    }
}
