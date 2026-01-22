using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using MCPForUnity.Editor.Helpers;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MCPForUnity.Editor.Tools.Prefabs
{
    [McpForUnityTool("manage_prefabs", AutoRegister = false)]
    /// <summary>
    /// Tool to manage Unity Prefab stages and create prefabs from GameObjects.
    /// </summary>
    public static class ManagePrefabs
    {
        private const string SupportedActions = "open_stage, close_stage, save_open_stage, create_from_gameobject, get_info, get_hierarchy, list_prefabs";

        public static object HandleCommand(JObject @params)
        {
            if (@params == null)
            {
                return new ErrorResponse("Parameters cannot be null.");
            }

            string action = @params["action"]?.ToString()?.ToLowerInvariant();
            if (string.IsNullOrEmpty(action))
            {
                return new ErrorResponse($"Action parameter is required. Valid actions are: {SupportedActions}.");
            }

            try
            {
                switch (action)
                {
                    case "open_stage":
                        return OpenStage(@params);
                    case "close_stage":
                        return CloseStage(@params);
                    case "save_open_stage":
                        return SaveOpenStage();
                    case "create_from_gameobject":
                        return CreatePrefabFromGameObject(@params);
                    case "get_info":
                        return GetInfo(@params);
                    case "get_hierarchy":
                        return GetHierarchy(@params);
                    case "list_prefabs":
                        return ListPrefabs(@params);
                    default:
                        return new ErrorResponse($"Unknown action: '{action}'. Valid actions are: {SupportedActions}.");
                }
            }
            catch (Exception e)
            {
                McpLog.Error($"[ManagePrefabs] Action '{action}' failed: {e}");
                return new ErrorResponse($"Internal error: {e.Message}");
            }
        }

        private static object OpenStage(JObject @params)
        {
            string prefabPath = @params["prefabPath"]?.ToString();
            if (string.IsNullOrEmpty(prefabPath))
            {
                return new ErrorResponse("'prefabPath' parameter is required for open_stage.");
            }

            string sanitizedPath = AssetPathUtility.SanitizeAssetPath(prefabPath);
            GameObject prefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(sanitizedPath);
            if (prefabAsset == null)
            {
                return new ErrorResponse($"No prefab asset found at path '{sanitizedPath}'.");
            }

            string modeValue = @params["mode"]?.ToString();
            if (!string.IsNullOrEmpty(modeValue) && !modeValue.Equals(PrefabStage.Mode.InIsolation.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                return new ErrorResponse("Only PrefabStage mode 'InIsolation' is supported at this time.");
            }

            PrefabStage stage = PrefabStageUtility.OpenPrefab(sanitizedPath);
            if (stage == null)
            {
                return new ErrorResponse($"Failed to open prefab stage for '{sanitizedPath}'.");
            }

            return new SuccessResponse($"Opened prefab stage for '{sanitizedPath}'.", SerializeStage(stage));
        }

        private static object CloseStage(JObject @params)
        {
            PrefabStage stage = PrefabStageUtility.GetCurrentPrefabStage();
            if (stage == null)
            {
                return new SuccessResponse("No prefab stage was open.");
            }

            bool saveBeforeClose = @params["saveBeforeClose"]?.ToObject<bool>() ?? false;
            if (saveBeforeClose && stage.scene.isDirty)
            {
                SaveStagePrefab(stage);
                AssetDatabase.SaveAssets();
            }

            StageUtility.GoToMainStage();
            return new SuccessResponse($"Closed prefab stage for '{stage.assetPath}'.");
        }

        private static object SaveOpenStage()
        {
            PrefabStage stage = PrefabStageUtility.GetCurrentPrefabStage();
            if (stage == null)
            {
                return new ErrorResponse("No prefab stage is currently open.");
            }

            SaveStagePrefab(stage);
            AssetDatabase.SaveAssets();
            return new SuccessResponse($"Saved prefab stage for '{stage.assetPath}'.", SerializeStage(stage));
        }

        private static void SaveStagePrefab(PrefabStage stage)
        {
            if (stage?.prefabContentsRoot == null)
            {
                throw new InvalidOperationException("Cannot save prefab stage without a prefab root.");
            }

            bool saved = PrefabUtility.SaveAsPrefabAsset(stage.prefabContentsRoot, stage.assetPath);
            if (!saved)
            {
                throw new InvalidOperationException($"Failed to save prefab asset at '{stage.assetPath}'.");
            }
        }

        private static object CreatePrefabFromGameObject(JObject @params)
        {
            string targetName = @params["target"]?.ToString() ?? @params["name"]?.ToString();
            if (string.IsNullOrEmpty(targetName))
            {
                return new ErrorResponse("'target' parameter is required for create_from_gameobject.");
            }

            bool includeInactive = @params["searchInactive"]?.ToObject<bool>() ?? false;
            GameObject sourceObject = FindSceneObjectByName(targetName, includeInactive);
            if (sourceObject == null)
            {
                return new ErrorResponse($"GameObject '{targetName}' not found in the active scene.");
            }

            if (PrefabUtility.IsPartOfPrefabAsset(sourceObject))
            {
                return new ErrorResponse(
                    $"GameObject '{sourceObject.name}' is part of a prefab asset. Open the prefab stage to save changes instead."
                );
            }

            PrefabInstanceStatus status = PrefabUtility.GetPrefabInstanceStatus(sourceObject);
            if (status != PrefabInstanceStatus.NotAPrefab)
            {
                return new ErrorResponse(
                    $"GameObject '{sourceObject.name}' is already linked to an existing prefab instance."
                );
            }

            string requestedPath = @params["prefabPath"]?.ToString();
            if (string.IsNullOrWhiteSpace(requestedPath))
            {
                return new ErrorResponse("'prefabPath' parameter is required for create_from_gameobject.");
            }

            string sanitizedPath = AssetPathUtility.SanitizeAssetPath(requestedPath);
            if (!sanitizedPath.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
            {
                sanitizedPath += ".prefab";
            }

            bool allowOverwrite = @params["allowOverwrite"]?.ToObject<bool>() ?? false;
            string finalPath = sanitizedPath;

            if (!allowOverwrite && AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(finalPath) != null)
            {
                finalPath = AssetDatabase.GenerateUniqueAssetPath(finalPath);
            }

            EnsureAssetDirectoryExists(finalPath);

            try
            {
                GameObject connectedInstance = PrefabUtility.SaveAsPrefabAssetAndConnect(
                    sourceObject,
                    finalPath,
                    InteractionMode.AutomatedAction
                );

                if (connectedInstance == null)
                {
                    return new ErrorResponse($"Failed to save prefab asset at '{finalPath}'.");
                }

                Selection.activeGameObject = connectedInstance;

                return new SuccessResponse(
                    $"Prefab created at '{finalPath}' and instance linked.",
                    new
                    {
                        prefabPath = finalPath,
                        instanceId = connectedInstance.GetInstanceID()
                    }
                );
            }
            catch (Exception e)
            {
                return new ErrorResponse($"Error saving prefab asset at '{finalPath}': {e.Message}");
            }
        }

        private static void EnsureAssetDirectoryExists(string assetPath)
        {
            string directory = Path.GetDirectoryName(assetPath);
            if (string.IsNullOrEmpty(directory))
            {
                return;
            }

            string fullDirectory = Path.Combine(Directory.GetCurrentDirectory(), directory);
            if (!Directory.Exists(fullDirectory))
            {
                Directory.CreateDirectory(fullDirectory);
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            }
        }

        private static GameObject FindSceneObjectByName(string name, bool includeInactive)
        {
            PrefabStage stage = PrefabStageUtility.GetCurrentPrefabStage();
            if (stage?.prefabContentsRoot != null)
            {
                foreach (Transform transform in stage.prefabContentsRoot.GetComponentsInChildren<Transform>(includeInactive))
                {
                    if (transform.name == name)
                    {
                        return transform.gameObject;
                    }
                }
            }

            Scene activeScene = SceneManager.GetActiveScene();
            foreach (GameObject root in activeScene.GetRootGameObjects())
            {
                foreach (Transform transform in root.GetComponentsInChildren<Transform>(includeInactive))
                {
                    GameObject candidate = transform.gameObject;
                    if (candidate.name == name)
                    {
                        return candidate;
                    }
                }
            }

            return null;
        }

        #region Read Operations

        /// <summary>
        /// Gets basic metadata information about a prefab asset.
        /// </summary>
        private static object GetInfo(JObject @params)
        {
            string prefabPath = @params["prefabPath"]?.ToString() ?? @params["path"]?.ToString();
            if (string.IsNullOrEmpty(prefabPath))
            {
                return new ErrorResponse("'prefabPath' parameter is required for get_info.");
            }

            string sanitizedPath = AssetPathUtility.SanitizeAssetPath(prefabPath);
            GameObject prefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(sanitizedPath);
            if (prefabAsset == null)
            {
                return new ErrorResponse($"No prefab asset found at path '{sanitizedPath}'.");
            }

            // Get GUID
            string guid = PrefabUtilityHelper.GetPrefabGUID(sanitizedPath);

            // Get prefab type
            PrefabAssetType assetType = PrefabUtility.GetPrefabAssetType(prefabAsset);
            string prefabTypeString = assetType.ToString();

            // Get component types on root
            var componentTypes = PrefabUtilityHelper.GetComponentTypeNames(prefabAsset);

            // Count children recursively
            int childCount = PrefabUtilityHelper.CountChildrenRecursive(prefabAsset.transform);

            // Get variant info
            var (isVariant, parentPrefab, _) = PrefabUtilityHelper.GetVariantInfo(prefabAsset);

            return new SuccessResponse(
                $"成功获取 prefab 信息。",
                new
                {
                    assetPath = sanitizedPath,
                    guid = guid,
                    prefabType = prefabTypeString,
                    rootObjectName = prefabAsset.name,
                    rootComponentTypes = componentTypes,
                    childCount = childCount,
                    isVariant = isVariant,
                    parentPrefab = parentPrefab
                }
            );
        }

        /// <summary>
        /// Gets the hierarchical structure of a prefab asset.
        /// </summary>
        private static object GetHierarchy(JObject @params)
        {
            string prefabPath = @params["prefabPath"]?.ToString() ?? @params["path"]?.ToString();
            if (string.IsNullOrEmpty(prefabPath))
            {
                return new ErrorResponse("'prefabPath' parameter is required for get_hierarchy.");
            }

            string sanitizedPath = AssetPathUtility.SanitizeAssetPath(prefabPath);

            // Parse pagination parameters
            var pagination = PaginationRequest.FromParams(@params, defaultPageSize: 50);
            int pageSize = Mathf.Clamp(pagination.PageSize, 1, 500);
            int cursor = pagination.Cursor;

            // Load prefab contents in background (without opening stage UI)
            GameObject prefabContents = PrefabUtility.LoadPrefabContents(sanitizedPath);
            if (prefabContents == null)
            {
                return new ErrorResponse($"Failed to load prefab contents from '{sanitizedPath}'.");
            }

            try
            {
                // Build hierarchy items
                var allItems = BuildHierarchyItems(prefabContents.transform, sanitizedPath);
                int totalCount = allItems.Count;

                // Apply pagination
                int startIndex = Mathf.Min(cursor, totalCount);
                int endIndex = Mathf.Min(startIndex + pageSize, totalCount);
                var paginatedItems = allItems.Skip(startIndex).Take(endIndex - startIndex).ToList();

                bool truncated = endIndex < totalCount;
                string nextCursor = truncated ? endIndex.ToString() : null;

                return new SuccessResponse(
                    $"成功获取 prefab 层级。",
                    new
                    {
                        prefabPath = sanitizedPath,
                        cursor = cursor.ToString(),
                        pageSize = pageSize,
                        nextCursor = nextCursor,
                        truncated = truncated,
                        total = totalCount,
                        items = paginatedItems
                    }
                );
            }
            finally
            {
                // Always unload prefab contents to free memory
                PrefabUtility.UnloadPrefabContents(prefabContents);
            }
        }

        /// <summary>
        /// Lists prefabs in the project with optional filtering.
        /// </summary>
        private static object ListPrefabs(JObject @params)
        {
            string path = @params["path"]?.ToString() ?? @params["prefabPath"]?.ToString() ?? "Assets";
            string search = @params["search"]?.ToString() ?? string.Empty;
            int pageSize = ParamCoercion.CoerceInt(@params["pageSize"] ?? @params["page_size"], 50);
            int pageNumber = ParamCoercion.CoerceInt(@params["pageNumber"] ?? @params["page_number"], 1);

            // Clamp values
            pageSize = Mathf.Clamp(pageSize, 1, 500);
            pageNumber = Mathf.Max(1, pageNumber);

            // Sanitize path - handle Assets folder specially to avoid double prefix
            string sanitizedPath;
            if (path.Equals("Assets", StringComparison.OrdinalIgnoreCase) || path.Equals("Assets/", StringComparison.OrdinalIgnoreCase))
            {
                sanitizedPath = "Assets";
            }
            else
            {
                sanitizedPath = AssetPathUtility.SanitizeAssetPath(path);
            }
            if (!AssetDatabase.IsValidFolder(sanitizedPath))
            {
                return new ErrorResponse($"Invalid path '{sanitizedPath}'. Path must be a valid folder.");
            }

            // Find all prefabs in the specified path
            string[] guids = AssetDatabase.FindAssets($"t:Prefab {search}".Trim(), new[] { sanitizedPath });

            // Convert to items
            var allItems = new List<object>();
            foreach (string guid in guids)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                string name = System.IO.Path.GetFileNameWithoutExtension(assetPath);
                allItems.Add(new
                {
                    path = assetPath,
                    name = name,
                    guid = guid
                });
            }

            // Apply pagination
            int startIndex = (pageNumber - 1) * pageSize;
            int endIndex = Mathf.Min(startIndex + pageSize, allItems.Count);
            int totalCount = allItems.Count;

            var pageItems = startIndex < totalCount
                ? allItems.Skip(startIndex).Take(endIndex - startIndex).ToList()
                : new List<object>();

            bool hasMore = endIndex < totalCount;

            return new SuccessResponse(
                $"找到 {totalCount} 个 prefab。",
                new
                {
                    items = pageItems,
                    totalCount = totalCount,
                    pageNumber = pageNumber,
                    pageSize = pageSize,
                    hasMore = hasMore
                }
            );
        }

        #endregion

        #region Hierarchy Builder

        /// <summary>
        /// Builds a flat list of hierarchy items from a transform root.
        /// </summary>
        private static List<object> BuildHierarchyItems(Transform root, string prefabPath)
        {
            var items = new List<object>();
            BuildHierarchyItemsRecursive(root, prefabPath, "", items);
            return items;
        }

        /// <summary>
        /// Recursively builds hierarchy items.
        /// </summary>
        private static void BuildHierarchyItemsRecursive(Transform transform, string prefabPath, string parentPath, List<object> items)
        {
            if (transform == null) return;

            string name = transform.gameObject.name;
            string path = string.IsNullOrEmpty(parentPath) ? name : $"{parentPath}/{name}";
            int instanceId = transform.gameObject.GetInstanceID();
            bool activeSelf = transform.gameObject.activeSelf;
            int childCount = transform.childCount;
            var componentTypes = PrefabUtilityHelper.GetComponentTypeNames(transform.gameObject);

            // Check if this is a nested prefab root
            bool isNestedPrefab = PrefabUtility.IsAnyPrefabInstanceRoot(transform.gameObject);
            bool isPrefabRoot = transform == transform.root;

            var item = new
            {
                name = name,
                instanceId = instanceId,
                path = path,
                activeSelf = activeSelf,
                childCount = childCount,
                componentTypes = componentTypes,
                isPrefabRoot = isPrefabRoot,
                isNestedPrefab = isNestedPrefab,
                nestedPrefabPath = isNestedPrefab ? PrefabUtilityHelper.GetNestedPrefabPath(transform.gameObject) : null
            };

            items.Add(item);

            // Recursively process children
            foreach (Transform child in transform)
            {
                BuildHierarchyItemsRecursive(child, prefabPath, path, items);
            }
        }

        #endregion

        private static object SerializeStage(PrefabStage stage)
        {
            if (stage == null)
            {
                return new { isOpen = false };
            }

            return new
            {
                isOpen = true,
                assetPath = stage.assetPath,
                prefabRootName = stage.prefabContentsRoot != null ? stage.prefabContentsRoot.name : null,
                mode = stage.mode.ToString(),
                isDirty = stage.scene.isDirty
            };
        }

    }
}
