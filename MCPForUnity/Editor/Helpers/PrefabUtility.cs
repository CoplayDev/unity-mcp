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
    }
}
