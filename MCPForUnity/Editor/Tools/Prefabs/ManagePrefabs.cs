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
        // Action constants
        private const string ACTION_OPEN_STAGE = "open_stage";
        private const string ACTION_CLOSE_STAGE = "close_stage";
        private const string ACTION_SAVE_OPEN_STAGE = "save_open_stage";
        private const string ACTION_CREATE_FROM_GAMEOBJECT = "create_from_gameobject";
        private const string ACTION_GET_INFO = "get_info";
        private const string ACTION_GET_HIERARCHY = "get_hierarchy";
        
        private const string SupportedActions = ACTION_OPEN_STAGE + ", " + ACTION_CLOSE_STAGE + ", " + ACTION_SAVE_OPEN_STAGE + ", " + ACTION_CREATE_FROM_GAMEOBJECT + ", " + ACTION_GET_INFO + ", " + ACTION_GET_HIERARCHY;

        // Pagination constants
        private const int DefaultPageSize = 50;
        private const int MaxPageSize = 500;

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
                    case ACTION_OPEN_STAGE:
                        return OpenStage(@params);
                    case ACTION_CLOSE_STAGE:
                        return CloseStage(@params);
                    case ACTION_SAVE_OPEN_STAGE:
                        return SaveOpenStage();
                    case ACTION_CREATE_FROM_GAMEOBJECT:
                        return CreatePrefabFromGameObject(@params);
                    case ACTION_GET_INFO:
                        return GetInfo(@params);
                    case ACTION_GET_HIERARCHY:
                        return GetHierarchy(@params);
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

        /// <summary>
        /// Opens a prefab in prefab mode for editing.
        /// </summary>
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

        /// <summary>
        /// Closes the currently open prefab stage, optionally saving first.
        /// </summary>
        private static object CloseStage(JObject @params)
        {
            PrefabStage stage = PrefabStageUtility.GetCurrentPrefabStage();
            if (stage == null)
            {
                return new SuccessResponse("No prefab stage was open.");
            }

            string assetPath = stage.assetPath;
            bool saveBeforeClose = @params["saveBeforeClose"]?.ToObject<bool>() ?? false;
            
            if (saveBeforeClose && stage.scene.isDirty)
            {
                try
                {
                    SaveAndRefreshStage(stage);
                }
                catch (Exception e)
                {
                    return new ErrorResponse($"Failed to save prefab before closing: {e.Message}");
                }
            }

            StageUtility.GoToMainStage();
            return new SuccessResponse($"Closed prefab stage for '{assetPath}'.");
        }

        /// <summary>
        /// Saves changes to the currently open prefab stage.
        /// </summary>
        private static object SaveOpenStage()
        {
            PrefabStage stage = PrefabStageUtility.GetCurrentPrefabStage();
            if (stage == null)
            {
                return new ErrorResponse("No prefab stage is currently open.");
            }

            if (!ValidatePrefabStageForSave(stage))
            {
                return new ErrorResponse("Prefab stage validation failed. Cannot save.");
            }

            try
            {
                SaveAndRefreshStage(stage);
                return new SuccessResponse($"Saved prefab stage for '{stage.assetPath}'.", SerializeStage(stage));
            }
            catch (Exception e)
            {
                return new ErrorResponse($"Failed to save prefab: {e.Message}");
            }
        }

        #region Prefab Save Operations

        /// <summary>
        /// Saves the prefab stage and refreshes the asset database.
        /// </summary>
        private static void SaveAndRefreshStage(PrefabStage stage)
        {
            if (stage == null)
            {
                throw new ArgumentNullException(nameof(stage), "Prefab stage cannot be null.");
            }

            SaveStagePrefab(stage);

            // Save all assets to ensure changes persist to disk
            AssetDatabase.SaveAssets();

            McpLog.Info($"[ManagePrefabs] Successfully saved prefab '{stage.assetPath}'.");
        }

        /// <summary>
        /// Saves the prefab stage asset using the correct Unity API (Unity 2021.3+).
        ///
        /// When editing in PrefabStage, the prefabContentsRoot is treated as a prefab instance.
        /// We use SetDirty + SaveAssets pattern which is the correct way to save changes
        /// made to a prefab that's open in PrefabStage.
        /// Note: AssetDatabase.SaveAssets() is called by SaveAndRefreshStage after this method.
        /// </summary>
        private static void SaveStagePrefab(PrefabStage stage)
        {
            if (stage?.prefabContentsRoot == null)
            {
                throw new InvalidOperationException("Cannot save prefab stage without a prefab root.");
            }

            if (string.IsNullOrEmpty(stage.assetPath))
            {
                throw new InvalidOperationException("Prefab stage has invalid asset path.");
            }

            // Mark the prefab as modified so Unity knows it needs to be saved
            EditorUtility.SetDirty(stage.prefabContentsRoot);

            McpLog.Info($"[ManagePrefabs] Prefab stage marked dirty: {stage.assetPath}");
        }

        /// <summary>
        /// Validates prefab stage before saving.
        /// </summary>
        private static bool ValidatePrefabStageForSave(PrefabStage stage)
        {
            if (stage == null)
            {
                McpLog.Warn("[ManagePrefabs] No prefab stage is open.");
                return false;
            }

            if (stage.prefabContentsRoot == null)
            {
                McpLog.Error($"[ManagePrefabs] Prefab stage '{stage.assetPath}' has no root object.");
                return false;
            }

            if (string.IsNullOrEmpty(stage.assetPath))
            {
                McpLog.Error("[ManagePrefabs] Prefab stage has invalid asset path.");
                return false;
            }

            return true;
        }

        #endregion

        #region Create Prefab from GameObject

        /// <summary>
        /// Creates a prefab asset from a GameObject in the scene.
        /// </summary>
        private static object CreatePrefabFromGameObject(JObject @params)
        {
            // 1. Validate and parse parameters
            var validation = ValidateCreatePrefabParams(@params);
            if (!validation.isValid)
            {
                return new ErrorResponse(validation.errorMessage);
            }

            string targetName = validation.targetName;
            string finalPath = validation.finalPath;
            bool includeInactive = validation.includeInactive;
            bool replaceExisting = validation.replaceExisting;
            bool unlinkIfInstance = validation.unlinkIfInstance;

            // 2. Find the source object
            GameObject sourceObject = FindSceneObjectByName(targetName, includeInactive);
            if (sourceObject == null)
            {
                return new ErrorResponse($"GameObject '{targetName}' not found in the active scene{(includeInactive ? " (including inactive objects)" : "")}.");
            }

            // 3. Validate source object state
            var objectValidation = ValidateSourceObjectForPrefab(sourceObject, unlinkIfInstance);
            if (!objectValidation.isValid)
            {
                return new ErrorResponse(objectValidation.errorMessage);
            }

            // 4. Check for path conflicts
            if (!replaceExisting && AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(finalPath) != null)
            {
                finalPath = AssetDatabase.GenerateUniqueAssetPath(finalPath);
                McpLog.Info($"[ManagePrefabs] Generated unique path: {finalPath}");
            }

            // 5. Ensure directory exists
            EnsureAssetDirectoryExists(finalPath);

            // 6. Unlink from existing prefab if needed
            if (unlinkIfInstance && objectValidation.shouldUnlink)
            {
                try
                {
                    // UnpackPrefabInstance requires the prefab instance root, not a child object
                    GameObject rootToUnlink = PrefabUtility.GetOutermostPrefabInstanceRoot(sourceObject);
                    if (rootToUnlink != null)
                    {
                        PrefabUtility.UnpackPrefabInstance(rootToUnlink, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                        McpLog.Info($"[ManagePrefabs] Unpacked prefab instance '{rootToUnlink.name}' before creating new prefab.");
                    }
                }
                catch (Exception e)
                {
                    return new ErrorResponse($"Failed to unlink prefab instance: {e.Message}");
                }
            }

            // 7. Create the prefab
            try
            {
                GameObject result = CreatePrefabAsset(sourceObject, finalPath, replaceExisting);

                if (result == null)
                {
                    return new ErrorResponse($"Failed to create prefab asset at '{finalPath}'.");
                }

                // 8. Select the newly created instance
                Selection.activeGameObject = result;

                return new SuccessResponse(
                    $"Prefab created at '{finalPath}' and instance linked.",
                    new
                    {
                        prefabPath = finalPath,
                        instanceId = result.GetInstanceID(),
                        instanceName = result.name,
                        wasUnlinked = unlinkIfInstance && objectValidation.shouldUnlink,
                        wasReplaced = replaceExisting && objectValidation.existingPrefabPath != null,
                        componentCount = result.GetComponents<Component>().Length,
                        childCount = result.transform.childCount
                    }
                );
            }
            catch (Exception e)
            {
                McpLog.Error($"[ManagePrefabs] Error creating prefab at '{finalPath}': {e}");
                return new ErrorResponse($"Error saving prefab asset: {e.Message}");
            }
        }

        /// <summary>
        /// Validates parameters for creating a prefab from GameObject.
        /// </summary>
        private static (bool isValid, string errorMessage, string targetName, string finalPath, bool includeInactive, bool replaceExisting, bool unlinkIfInstance) 
            ValidateCreatePrefabParams(JObject @params)
        {
            string targetName = @params["target"]?.ToString() ?? @params["name"]?.ToString();
            if (string.IsNullOrEmpty(targetName))
            {
                return (false, "'target' parameter is required for create_from_gameobject.", null, null, false, false, false);
            }

            string requestedPath = @params["prefabPath"]?.ToString();
            if (string.IsNullOrWhiteSpace(requestedPath))
            {
                return (false, "'prefabPath' parameter is required for create_from_gameobject.", targetName, null, false, false, false);
            }

            string sanitizedPath = AssetPathUtility.SanitizeAssetPath(requestedPath);
            if (!sanitizedPath.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
            {
                sanitizedPath += ".prefab";
            }

            // Validate path is within Assets folder
            if (!sanitizedPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
            {
                return (false, $"Prefab path must be within the Assets folder. Got: '{sanitizedPath}'", targetName, null, false, false, false);
            }

            bool includeInactive = @params["searchInactive"]?.ToObject<bool>() ?? false;
            bool replaceExisting = @params["allowOverwrite"]?.ToObject<bool>() ?? false;
            bool unlinkIfInstance = @params["unlinkIfInstance"]?.ToObject<bool>() ?? false;

            return (true, null, targetName, sanitizedPath, includeInactive, replaceExisting, unlinkIfInstance);
        }

        /// <summary>
        /// Validates source object can be converted to prefab.
        /// </summary>
        private static (bool isValid, string errorMessage, bool shouldUnlink, string existingPrefabPath)
            ValidateSourceObjectForPrefab(GameObject sourceObject, bool unlinkIfInstance)
        {
            // Check if this is a Prefab Asset (the .prefab file itself in the editor)
            if (PrefabUtility.IsPartOfPrefabAsset(sourceObject))
            {
                return (false,
                    $"GameObject '{sourceObject.name}' is part of a prefab asset. " +
                    "Open the prefab stage to save changes instead.",
                    false, null);
            }

            // Check if this is already a Prefab Instance
            PrefabInstanceStatus status = PrefabUtility.GetPrefabInstanceStatus(sourceObject);
            if (status != PrefabInstanceStatus.NotAPrefab)
            {
                string existingPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(sourceObject);

                if (!unlinkIfInstance)
                {
                    return (false,
                        $"GameObject '{sourceObject.name}' is already linked to prefab '{existingPath}'. " +
                        "Set 'unlinkIfInstance' to true to unlink it first, or modify the existing prefab instead.",
                        false, existingPath);
                }

                // Needs to be unlinked
                return (true, null, true, existingPath);
            }

            return (true, null, false, null);
        }

        /// <summary>
        /// Creates a prefab asset from a GameObject.
        /// </summary>
        private static GameObject CreatePrefabAsset(GameObject sourceObject, string path, bool replaceExisting)
        {
            GameObject result = PrefabUtility.SaveAsPrefabAssetAndConnect(
                sourceObject,
                path,
                InteractionMode.AutomatedAction
            );

            string action = replaceExisting ? "Replaced existing" : "Created new";
            McpLog.Info($"[ManagePrefabs] {action} prefab at '{path}'.");

            if (result != null)
            {
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }

            return result;
        }

        #endregion

        /// <summary>
        /// Ensures the directory for an asset path exists, creating it if necessary.
        /// </summary>
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
                McpLog.Info($"[ManagePrefabs] Created directory: {directory}");
            }
        }

        /// <summary>
        /// Finds a GameObject by name in the active scene or current prefab stage.
        /// </summary>
        private static GameObject FindSceneObjectByName(string name, bool includeInactive)
        {
            // First check if we're in Prefab Stage
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

            // Search in the active scene
            Scene activeScene = SceneManager.GetActiveScene();
            foreach (GameObject root in activeScene.GetRootGameObjects())
            {
                // Check the root object itself
                if (root.name == name && (includeInactive || root.activeSelf))
                {
                    return root;
                }

                // Check children
                foreach (Transform transform in root.GetComponentsInChildren<Transform>(includeInactive))
                {
                    if (transform.name == name)
                    {
                        return transform.gameObject;
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

            string guid = PrefabUtilityHelper.GetPrefabGUID(sanitizedPath);
            PrefabAssetType assetType = PrefabUtility.GetPrefabAssetType(prefabAsset);
            string prefabTypeString = assetType.ToString();
            var componentTypes = PrefabUtilityHelper.GetComponentTypeNames(prefabAsset);
            int childCount = PrefabUtilityHelper.CountChildrenRecursive(prefabAsset.transform);
            var (isVariant, parentPrefab, _) = PrefabUtilityHelper.GetVariantInfo(prefabAsset);

            return new SuccessResponse(
                $"Successfully retrieved prefab info.",
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
            var pagination = PaginationRequest.FromParams(@params, defaultPageSize: DefaultPageSize);
            int pageSize = Mathf.Clamp(pagination.PageSize, 1, MaxPageSize);
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
                var allItems = BuildHierarchyItems(prefabContents.transform);
                int totalCount = allItems.Count;

                // Apply pagination
                int startIndex = Mathf.Min(cursor, totalCount);
                int endIndex = Mathf.Min(startIndex + pageSize, totalCount);
                var paginatedItems = allItems.Skip(startIndex).Take(endIndex - startIndex).ToList();

                bool truncated = endIndex < totalCount;
                string nextCursor = truncated ? endIndex.ToString() : null;

                return new SuccessResponse(
                    $"Successfully retrieved prefab hierarchy. Found {totalCount} objects.",
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

        #endregion

        #region Hierarchy Builder

        /// <summary>
        /// Builds a flat list of hierarchy items from a transform root.
        /// </summary>
        private static List<object> BuildHierarchyItems(Transform root)
        {
            var items = new List<object>();
            BuildHierarchyItemsRecursive(root, "", items);
            return items;
        }

        /// <summary>
        /// Recursively builds hierarchy items.
        /// </summary>
        private static void BuildHierarchyItemsRecursive(Transform transform, string parentPath, List<object> items)
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
                BuildHierarchyItemsRecursive(child, path, items);
            }
        }

        #endregion

        /// <summary>
        /// Serializes the prefab stage information for response.
        /// </summary>
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