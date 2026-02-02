using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
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
    /// Tool to manage Unity Prefabs: create, inspect, and modify prefab assets.
    /// Uses headless editing (no UI, no dialogs) for reliable automated workflows.
    /// </summary>
    public static class ManagePrefabs
    {
        // Action constants
        private const string ACTION_CREATE_FROM_GAMEOBJECT = "create_from_gameobject";
        private const string ACTION_GET_INFO = "get_info";
        private const string ACTION_GET_HIERARCHY = "get_hierarchy";
        private const string ACTION_MODIFY_CONTENTS = "modify_contents";
        private const string SupportedActions = ACTION_CREATE_FROM_GAMEOBJECT + ", " + ACTION_GET_INFO + ", " + ACTION_GET_HIERARCHY + ", " + ACTION_MODIFY_CONTENTS;

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
                    case ACTION_CREATE_FROM_GAMEOBJECT:
                        return CreatePrefabFromGameObject(@params);
                    case ACTION_GET_INFO:
                        return GetInfo(@params);
                    case ACTION_GET_HIERARCHY:
                        return GetHierarchy(@params);
                    case ACTION_MODIFY_CONTENTS:
                        return ModifyContents(@params);
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
                return new ErrorResponse($"GameObject '{targetName}' not found in the active scene or prefab stage{(includeInactive ? " (including inactive objects)" : "")}.");
            }

            // 3. Validate source object state
            var objectValidation = ValidateSourceObjectForPrefab(sourceObject, unlinkIfInstance);
            if (!objectValidation.isValid)
            {
                return new ErrorResponse(objectValidation.errorMessage);
            }

            // 4. Check for path conflicts and track if file will be replaced
            bool fileExistedAtPath = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(finalPath) != null;

            if (!replaceExisting && fileExistedAtPath)
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
                        wasReplaced = replaceExisting && fileExistedAtPath,
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
            if (sanitizedPath == null)
            {
                return (false, $"Invalid prefab path (path traversal detected): '{requestedPath}'", targetName, null, false, false, false);
            }
            if (string.IsNullOrEmpty(sanitizedPath))
            {
                return (false, $"Invalid prefab path '{requestedPath}'. Path cannot be empty.", targetName, null, false, false, false);
            }
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

            // Use Application.dataPath for more reliable path resolution
            // Application.dataPath points to the Assets folder (e.g., ".../ProjectName/Assets")
            string assetsPath = Application.dataPath;
            string projectRoot = Path.GetDirectoryName(assetsPath);
            string fullDirectory = Path.Combine(projectRoot, directory);

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
                    if (transform.name == name && (includeInactive || transform.gameObject.activeSelf))
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
                    if (transform.name == name && (includeInactive || transform.gameObject.activeSelf))
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
            if (string.IsNullOrEmpty(sanitizedPath))
            {
                return new ErrorResponse($"Invalid prefab path: '{prefabPath}'.");
            }
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
        /// Gets the hierarchical structure of a prefab asset with pagination support.
        /// Supports page_size, cursor, max_depth, and filter parameters to limit response size.
        /// </summary>
        private static object GetHierarchy(JObject @params)
        {
            string prefabPath = @params["prefabPath"]?.ToString() ?? @params["path"]?.ToString();
            if (string.IsNullOrEmpty(prefabPath))
            {
                return new ErrorResponse("'prefabPath' parameter is required for get_hierarchy.");
            }

            string sanitizedPath = AssetPathUtility.SanitizeAssetPath(prefabPath);
            if (string.IsNullOrEmpty(sanitizedPath))
            {
                return new ErrorResponse($"Invalid prefab path '{prefabPath}'. Path traversal sequences are not allowed.");
            }

            // Parse pagination parameters
            int pageSize = Mathf.Clamp(
                ParamCoercion.CoerceIntNullable(@params["pageSize"] ?? @params["page_size"]) ?? 50,
                1, 500);
            int cursor = Mathf.Max(0,
                ParamCoercion.CoerceIntNullable(@params["cursor"]) ?? 0);
            int? maxDepth = ParamCoercion.CoerceIntNullable(@params["maxDepth"] ?? @params["max_depth"]);
            string filter = @params["filter"]?.ToString();

            // Load prefab contents in background (without opening stage UI)
            GameObject prefabContents = PrefabUtility.LoadPrefabContents(sanitizedPath);
            if (prefabContents == null)
            {
                return new ErrorResponse($"Failed to load prefab contents from '{sanitizedPath}'.");
            }

            try
            {
                // Build hierarchy items with depth limit
                var allItems = BuildHierarchyItemsWithDepth(prefabContents.transform, sanitizedPath, maxDepth, filter);
                int total = allItems.Count;

                // Apply pagination
                if (cursor > total) cursor = total;
                int end = Mathf.Min(total, cursor + pageSize);

                var pagedItems = new List<object>(Mathf.Max(0, end - cursor));
                for (int i = cursor; i < end; i++)
                {
                    pagedItems.Add(allItems[i]);
                }

                bool truncated = end < total;
                string nextCursor = truncated ? end.ToString() : null;

                return new SuccessResponse(
                    $"Retrieved prefab hierarchy page. Showing {pagedItems.Count} of {total} objects.",
                    new
                    {
                        prefabPath = sanitizedPath,
                        total = total,
                        cursor = cursor,
                        pageSize = pageSize,
                        next_cursor = nextCursor,
                        truncated = truncated,
                        maxDepth = maxDepth,
                        filter = filter,
                        items = pagedItems
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

        #region Headless Prefab Editing

        /// <summary>
        /// Modifies a prefab's contents directly without opening the prefab stage.
        /// This is ideal for automated/agentic workflows as it avoids UI, dirty flags, and dialogs.
        /// </summary>
        private static object ModifyContents(JObject @params)
        {
            string prefabPath = @params["prefabPath"]?.ToString() ?? @params["path"]?.ToString();
            if (string.IsNullOrEmpty(prefabPath))
            {
                return new ErrorResponse("'prefabPath' parameter is required for modify_contents.");
            }

            string sanitizedPath = AssetPathUtility.SanitizeAssetPath(prefabPath);
            if (string.IsNullOrEmpty(sanitizedPath))
            {
                return new ErrorResponse($"Invalid prefab path '{prefabPath}'. Path traversal sequences are not allowed.");
            }

            // Load prefab contents in isolated context (no UI)
            GameObject prefabContents = PrefabUtility.LoadPrefabContents(sanitizedPath);
            if (prefabContents == null)
            {
                return new ErrorResponse($"Failed to load prefab contents from '{sanitizedPath}'.");
            }

            try
            {
                // Find target object within the prefab (defaults to root)
                string targetName = @params["target"]?.ToString();
                GameObject targetGo = FindInPrefabContents(prefabContents, targetName);

                if (targetGo == null)
                {
                    string searchedFor = string.IsNullOrEmpty(targetName) ? "root" : $"'{targetName}'";
                    return new ErrorResponse($"Target {searchedFor} not found in prefab '{sanitizedPath}'.");
                }

                // Apply modifications
                var modifyResult = ApplyModificationsToPrefabObject(targetGo, @params, prefabContents, out List<object> createdChildren);
                if (modifyResult.error != null)
                {
                    return modifyResult.error;
                }

                // Skip saving when no modifications were made to avoid unnecessary asset writes
                if (!modifyResult.modified)
                {
                    return new SuccessResponse(
                        $"Prefab '{sanitizedPath}' is already up to date; no changes were applied.",
                        new
                        {
                            prefabPath = sanitizedPath,
                            targetName = targetGo.name,
                            modified = false,
                            createdChildren = createdChildren
                        }
                    );
                }

                // Save the prefab
                bool success;
                PrefabUtility.SaveAsPrefabAsset(prefabContents, sanitizedPath, out success);

                if (!success)
                {
                    return new ErrorResponse($"Failed to save prefab asset at '{sanitizedPath}'.");
                }

                AssetDatabase.Refresh();

                McpLog.Info($"[ManagePrefabs] Successfully modified and saved prefab '{sanitizedPath}' (headless).");

                return new SuccessResponse(
                    $"Prefab '{sanitizedPath}' modified and saved successfully.",
                    new
                    {
                        prefabPath = sanitizedPath,
                        targetName = targetGo.name,
                        modified = modifyResult.modified,
                        createdChildren = createdChildren,
                        transform = new
                        {
                            position = new { x = targetGo.transform.localPosition.x, y = targetGo.transform.localPosition.y, z = targetGo.transform.localPosition.z },
                            rotation = new { x = targetGo.transform.localEulerAngles.x, y = targetGo.transform.localEulerAngles.y, z = targetGo.transform.localEulerAngles.z },
                            scale = new { x = targetGo.transform.localScale.x, y = targetGo.transform.localScale.y, z = targetGo.transform.localScale.z }
                        },
                        componentTypes = PrefabUtilityHelper.GetComponentTypeNames(targetGo)
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
        /// Finds a GameObject within loaded prefab contents by name or path.
        /// </summary>
        private static GameObject FindInPrefabContents(GameObject prefabContents, string target)
        {
            if (string.IsNullOrEmpty(target))
            {
                // Return root if no target specified
                return prefabContents;
            }

            // Try to find by path first (e.g., "Parent/Child/Target")
            if (target.Contains("/"))
            {
                Transform found = prefabContents.transform.Find(target);
                if (found != null)
                {
                    return found.gameObject;
                }

                // If path starts with root name, try without it
                if (target.StartsWith(prefabContents.name + "/"))
                {
                    string relativePath = target.Substring(prefabContents.name.Length + 1);
                    found = prefabContents.transform.Find(relativePath);
                    if (found != null)
                    {
                        return found.gameObject;
                    }
                }
            }

            // Check if target matches root name
            if (prefabContents.name == target)
            {
                return prefabContents;
            }

            // Search by name in hierarchy
            foreach (Transform t in prefabContents.GetComponentsInChildren<Transform>(true))
            {
                if (t.gameObject.name == target)
                {
                    return t.gameObject;
                }
            }

            return null;
        }

        /// <summary>
        /// Applies modifications to a GameObject within loaded prefab contents.
        /// Backward-compatible overload that discards created children info.
        /// </summary>
        private static (bool modified, ErrorResponse error) ApplyModificationsToPrefabObject(GameObject targetGo, JObject @params, GameObject prefabRoot)
        {
            return ApplyModificationsToPrefabObject(targetGo, @params, prefabRoot, out _);
        }

        /// <summary>
        /// Applies modifications to a GameObject within loaded prefab contents.
        /// Returns (modified: bool, error: ErrorResponse or null). Created children info is returned via out parameter.
        /// </summary>
        private static (bool modified, ErrorResponse error) ApplyModificationsToPrefabObject(GameObject targetGo, JObject @params, GameObject prefabRoot, out List<object> createdChildren)
        {
            bool modified = false;
            createdChildren = new List<object>();

            // Name change
            string newName = @params["name"]?.ToString();
            if (!string.IsNullOrEmpty(newName) && targetGo.name != newName)
            {
                // If renaming the root, this will affect the prefab asset name on save
                targetGo.name = newName;
                modified = true;
            }

            // Active state
            bool? setActive = @params["setActive"]?.ToObject<bool?>();
            if (setActive.HasValue && targetGo.activeSelf != setActive.Value)
            {
                targetGo.SetActive(setActive.Value);
                modified = true;
            }

            // Tag
            string tag = @params["tag"]?.ToString();
            if (tag != null && targetGo.tag != tag)
            {
                string tagToSet = string.IsNullOrEmpty(tag) ? "Untagged" : tag;
                try
                {
                    targetGo.tag = tagToSet;
                    modified = true;
                }
                catch (Exception ex)
                {
                    return (false, new ErrorResponse($"Failed to set tag to '{tagToSet}': {ex.Message}"));
                }
            }

            // Layer
            string layerName = @params["layer"]?.ToString();
            if (!string.IsNullOrEmpty(layerName))
            {
                int layerId = LayerMask.NameToLayer(layerName);
                if (layerId == -1)
                {
                    return (false, new ErrorResponse($"Invalid layer specified: '{layerName}'. Use a valid layer name."));
                }
                if (targetGo.layer != layerId)
                {
                    targetGo.layer = layerId;
                    modified = true;
                }
            }

            // Transform: position, rotation, scale
            Vector3? position = VectorParsing.ParseVector3(@params["position"]);
            Vector3? rotation = VectorParsing.ParseVector3(@params["rotation"]);
            Vector3? scale = VectorParsing.ParseVector3(@params["scale"]);

            if (position.HasValue && targetGo.transform.localPosition != position.Value)
            {
                targetGo.transform.localPosition = position.Value;
                modified = true;
            }
            if (rotation.HasValue && targetGo.transform.localEulerAngles != rotation.Value)
            {
                targetGo.transform.localEulerAngles = rotation.Value;
                modified = true;
            }
            if (scale.HasValue && targetGo.transform.localScale != scale.Value)
            {
                targetGo.transform.localScale = scale.Value;
                modified = true;
            }

            // Parent change (within prefab hierarchy)
            JToken parentToken = @params["parent"];
            if (parentToken != null)
            {
                string parentTarget = parentToken.ToString();
                Transform newParent = null;

                if (!string.IsNullOrEmpty(parentTarget))
                {
                    GameObject parentGo = FindInPrefabContents(prefabRoot, parentTarget);
                    if (parentGo == null)
                    {
                        return (false, new ErrorResponse($"Parent '{parentTarget}' not found in prefab."));
                    }
                    if (parentGo.transform.IsChildOf(targetGo.transform))
                    {
                        return (false, new ErrorResponse($"Cannot parent '{targetGo.name}' to '{parentGo.name}' as it would create a hierarchy loop."));
                    }
                    newParent = parentGo.transform;
                }

                if (targetGo.transform.parent != newParent)
                {
                    targetGo.transform.SetParent(newParent, true);
                    modified = true;
                }
            }

            // Components to add
            if (@params["componentsToAdd"] is JArray componentsToAdd)
            {
                foreach (var compToken in componentsToAdd)
                {
                    string typeName = compToken.Type == JTokenType.String
                        ? compToken.ToString()
                        : (compToken as JObject)?["typeName"]?.ToString();

                    if (!string.IsNullOrEmpty(typeName))
                    {
                        if (!ComponentResolver.TryResolve(typeName, out Type componentType, out string error))
                        {
                            return (false, new ErrorResponse($"Component type '{typeName}' not found: {error}"));
                        }
                        targetGo.AddComponent(componentType);
                        modified = true;
                    }
                }
            }

            // Components to remove
            if (@params["componentsToRemove"] is JArray componentsToRemove)
            {
                foreach (var compToken in componentsToRemove)
                {
                    string typeName = compToken.ToString();
                    if (!string.IsNullOrEmpty(typeName))
                    {
                        if (!ComponentResolver.TryResolve(typeName, out Type componentType, out string error))
                        {
                            return (false, new ErrorResponse($"Component type '{typeName}' not found: {error}"));
                        }
                        Component comp = targetGo.GetComponent(componentType);
                        if (comp != null)
                        {
                            UnityEngine.Object.DestroyImmediate(comp);
                            modified = true;
                        }
                    }
                }
            }

            // Create child GameObjects (supports single object or array)
            JToken createChildToken = @params["createChild"] ?? @params["create_child"];
            if (createChildToken != null)
            {
                // Handle array of children
                if (createChildToken is JArray childArray)
                {
                    foreach (var childToken in childArray)
                    {
                        var childResult = CreateSingleChildInPrefab(childToken, targetGo, prefabRoot, out object childInfo);
                        if (childResult.error != null)
                        {
                            return (false, childResult.error);
                        }
                        if (childResult.created)
                        {
                            modified = true;
                            if (childInfo != null)
                            {
                                createdChildren.Add(childInfo);
                            }
                        }
                    }
                }
                else
                {
                    // Handle single child object
                    var childResult = CreateSingleChildInPrefab(createChildToken, targetGo, prefabRoot, out object childInfo);
                    if (childResult.error != null)
                    {
                        return (false, childResult.error);
                    }
                    if (childResult.created)
                    {
                        modified = true;
                        if (childInfo != null)
                        {
                            createdChildren.Add(childInfo);
                        }
                    }
                }
            }

            // Set component references (wire serialized fields to other objects in the prefab)
            JToken setRefToken = @params["setComponentReference"] ?? @params["set_component_reference"];
            if (setRefToken != null)
            {
                // Handle array of references or single reference
                if (setRefToken is JArray refArray)
                {
                    foreach (var refItem in refArray)
                    {
                        var refResult = SetSingleComponentReference(refItem, targetGo, prefabRoot);
                        if (refResult.error != null)
                        {
                            return (false, refResult.error);
                        }
                        if (refResult.set)
                        {
                            modified = true;
                        }
                    }
                }
                else
                {
                    var refResult = SetSingleComponentReference(setRefToken, targetGo, prefabRoot);
                    if (refResult.error != null)
                    {
                        return (false, refResult.error);
                    }
                    if (refResult.set)
                    {
                        modified = true;
                    }
                }
            }

            // Set component property values
            JToken setPropToken = @params["setProperty"] ?? @params["set_property"];
            if (setPropToken != null)
            {
                // Handle array of property sets or single property set
                if (setPropToken is JArray propArray)
                {
                    foreach (var propItem in propArray)
                    {
                        var propResult = SetSingleComponentProperty(propItem, targetGo);
                        if (propResult.error != null)
                        {
                            return (false, propResult.error);
                        }
                        if (propResult.set)
                        {
                            modified = true;
                        }
                    }
                }
                else
                {
                    var propResult = SetSingleComponentProperty(setPropToken, targetGo);
                    if (propResult.error != null)
                    {
                        return (false, propResult.error);
                    }
                    if (propResult.set)
                    {
                        modified = true;
                    }
                }
            }

            return (modified, null);
        }

        /// <summary>
        /// Creates a single child GameObject within the prefab contents.
        /// Backward-compatible overload that discards created info.
        /// </summary>
        private static (bool created, ErrorResponse error) CreateSingleChildInPrefab(JToken createChildToken, GameObject defaultParent, GameObject prefabRoot)
        {
            return CreateSingleChildInPrefab(createChildToken, defaultParent, prefabRoot, out _);
        }

        /// <summary>
        /// Creates a single child GameObject within the prefab contents.
        /// Returns (created: bool, error: ErrorResponse or null). Created info is returned via out parameter.
        /// </summary>
        private static (bool created, ErrorResponse error) CreateSingleChildInPrefab(JToken createChildToken, GameObject defaultParent, GameObject prefabRoot, out object createdInfo)
        {
            createdInfo = null;

            JObject childParams;
            if (createChildToken is JObject obj)
            {
                childParams = obj;
            }
            else
            {
                return (false, new ErrorResponse("'create_child' must be an object with child properties."));
            }

            // Required: name
            string childName = childParams["name"]?.ToString();
            if (string.IsNullOrEmpty(childName))
            {
                return (false, new ErrorResponse("'create_child.name' is required."));
            }

            // Optional: parent (defaults to the target object)
            string parentName = childParams["parent"]?.ToString();
            Transform parentTransform = defaultParent.transform;
            if (!string.IsNullOrEmpty(parentName))
            {
                GameObject parentGo = FindInPrefabContents(prefabRoot, parentName);
                if (parentGo == null)
                {
                    return (false, new ErrorResponse($"Parent '{parentName}' not found in prefab for create_child."));
                }
                parentTransform = parentGo.transform;
            }

            // Create the GameObject
            GameObject newChild;
            string childPrefabPath = childParams["prefabPath"]?.ToString() ?? childParams["prefab_path"]?.ToString();
            string primitiveType = childParams["primitiveType"]?.ToString() ?? childParams["primitive_type"]?.ToString();
            bool isPrefabInstance = false;
            string sourcePrefabGuid = null;
            string sourcePrefabPath = null;

            if (!string.IsNullOrEmpty(childPrefabPath))
            {
                // Instantiate from a source prefab (creates a nested prefab instance)
                string sanitizedChildPrefabPath = AssetPathUtility.SanitizeAssetPath(childPrefabPath);
                if (string.IsNullOrEmpty(sanitizedChildPrefabPath))
                {
                    return (false, new ErrorResponse($"Invalid prefab path for create_child: '{childPrefabPath}'."));
                }

                GameObject sourcePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(sanitizedChildPrefabPath);
                if (sourcePrefab == null)
                {
                    return (false, new ErrorResponse($"Source prefab not found at '{sanitizedChildPrefabPath}' for create_child."));
                }

                // Use InstantiatePrefab to create a proper nested prefab instance
                newChild = PrefabUtility.InstantiatePrefab(sourcePrefab, parentTransform) as GameObject;
                if (newChild == null)
                {
                    return (false, new ErrorResponse($"Failed to instantiate prefab '{sanitizedChildPrefabPath}' for create_child."));
                }

                // Apply the custom name if different from the source prefab name
                if (newChild.name != childName)
                {
                    newChild.name = childName;
                }

                isPrefabInstance = true;
                sourcePrefabPath = sanitizedChildPrefabPath;
                sourcePrefabGuid = AssetDatabase.AssetPathToGUID(sanitizedChildPrefabPath);

                McpLog.Info($"[ManagePrefabs] Instantiated nested prefab '{sanitizedChildPrefabPath}' as '{childName}'.");
            }
            else if (!string.IsNullOrEmpty(primitiveType))
            {
                try
                {
                    PrimitiveType type = (PrimitiveType)Enum.Parse(typeof(PrimitiveType), primitiveType, true);
                    newChild = GameObject.CreatePrimitive(type);
                    newChild.name = childName;
                }
                catch (ArgumentException)
                {
                    return (false, new ErrorResponse($"Invalid primitive type: '{primitiveType}'. Valid types: {string.Join(", ", Enum.GetNames(typeof(PrimitiveType)))}"));
                }
            }
            else
            {
                newChild = new GameObject(childName);
            }

            // Set parent
            newChild.transform.SetParent(parentTransform, false);

            // Apply transform properties
            Vector3? position = VectorParsing.ParseVector3(childParams["position"]);
            Vector3? rotation = VectorParsing.ParseVector3(childParams["rotation"]);
            Vector3? scale = VectorParsing.ParseVector3(childParams["scale"]);

            if (position.HasValue)
            {
                newChild.transform.localPosition = position.Value;
            }
            if (rotation.HasValue)
            {
                newChild.transform.localEulerAngles = rotation.Value;
            }
            if (scale.HasValue)
            {
                newChild.transform.localScale = scale.Value;
            }

            // Add components and track them
            var addedComponents = new List<object>();
            JArray componentsToAdd = childParams["componentsToAdd"] as JArray ?? childParams["components_to_add"] as JArray;
            if (componentsToAdd != null)
            {
                for (int i = 0; i < componentsToAdd.Count; i++)
                {
                    var compToken = componentsToAdd[i];
                    string typeName = compToken.Type == JTokenType.String
                        ? compToken.ToString()
                        : (compToken as JObject)?["typeName"]?.ToString();

                    if (string.IsNullOrEmpty(typeName))
                    {
                        // Clean up partially created child
                        UnityEngine.Object.DestroyImmediate(newChild);
                        return (false, new ErrorResponse($"create_child.components_to_add[{i}] must be a string or object with 'typeName' field, got {compToken.Type}"));
                    }

                    if (!ComponentResolver.TryResolve(typeName, out Type componentType, out string error))
                    {
                        // Clean up partially created child
                        UnityEngine.Object.DestroyImmediate(newChild);
                        return (false, new ErrorResponse($"Component type '{typeName}' not found for create_child: {error}"));
                    }
                    Component addedComp = newChild.AddComponent(componentType);
                    addedComponents.Add(new
                    {
                        type = componentType.Name,
                        fullType = componentType.FullName,
                        instanceId = addedComp.GetInstanceID()
                    });
                }
            }

            // Set tag if specified
            string tag = childParams["tag"]?.ToString();
            if (!string.IsNullOrEmpty(tag))
            {
                try
                {
                    newChild.tag = tag;
                }
                catch (Exception ex)
                {
                    UnityEngine.Object.DestroyImmediate(newChild);
                    return (false, new ErrorResponse($"Failed to set tag '{tag}' on child '{childName}': {ex.Message}"));
                }
            }

            // Set layer if specified
            string layerName = childParams["layer"]?.ToString();
            if (!string.IsNullOrEmpty(layerName))
            {
                int layerId = LayerMask.NameToLayer(layerName);
                if (layerId == -1)
                {
                    UnityEngine.Object.DestroyImmediate(newChild);
                    return (false, new ErrorResponse($"Invalid layer '{layerName}' for child '{childName}'. Use a valid layer name."));
                }
                newChild.layer = layerId;
            }

            // Set active state
            bool? setActive = childParams["setActive"]?.ToObject<bool?>() ?? childParams["set_active"]?.ToObject<bool?>();
            if (setActive.HasValue)
            {
                newChild.SetActive(setActive.Value);
            }

            // Build created info response
            createdInfo = new
            {
                name = newChild.name,
                path = GetGameObjectPath(newChild, prefabRoot),
                instanceId = newChild.GetInstanceID(),
                transformInstanceId = newChild.transform.GetInstanceID(),
                isPrefabInstance = isPrefabInstance,
                sourcePrefabGuid = sourcePrefabGuid,
                sourcePrefabPath = sourcePrefabPath,
                components = addedComponents,
                allComponents = GetComponentInfoList(newChild)
            };

            McpLog.Info($"[ManagePrefabs] Created child '{childName}' under '{parentTransform.name}' in prefab.");
            return (true, null);
        }

        /// <summary>
        /// Gets the hierarchy path of a GameObject within a prefab.
        /// </summary>
        private static string GetGameObjectPath(GameObject go, GameObject root)
        {
            if (go == root) return go.name;

            var path = new List<string>();
            Transform current = go.transform;
            while (current != null)
            {
                path.Add(current.name);
                if (current == root.transform) break;
                current = current.parent;
            }
            path.Reverse();
            return string.Join("/", path);
        }

        /// <summary>
        /// Gets info about all components on a GameObject.
        /// </summary>
        private static List<object> GetComponentInfoList(GameObject go)
        {
            var components = new List<object>();
            foreach (Component comp in go.GetComponents<Component>())
            {
                if (comp == null) continue;
                components.Add(new
                {
                    type = comp.GetType().Name,
                    fullType = comp.GetType().FullName,
                    instanceId = comp.GetInstanceID()
                });
            }
            return components;
        }

        /// <summary>
        /// Sets a component's serialized field to reference another object in the prefab.
        /// Supports referencing GameObjects or specific Components on GameObjects.
        /// </summary>
        private static (bool set, ErrorResponse error) SetSingleComponentReference(JToken refToken, GameObject targetGo, GameObject prefabRoot)
        {
            if (refToken is not JObject refParams)
            {
                return (false, new ErrorResponse("'set_component_reference' must be an object with component_type, field, and reference_target."));
            }

            // Required: component_type - the type of component on targetGo that has the field
            string componentTypeName = refParams["componentType"]?.ToString() ?? refParams["component_type"]?.ToString();
            if (string.IsNullOrEmpty(componentTypeName))
            {
                return (false, new ErrorResponse("'set_component_reference.component_type' is required."));
            }

            // Required: field - the name of the serialized field to set
            string fieldName = refParams["field"]?.ToString();
            if (string.IsNullOrEmpty(fieldName))
            {
                return (false, new ErrorResponse("'set_component_reference.field' is required."));
            }

            // Required: reference_target - path to the GameObject (or GameObject/ComponentType) to reference
            string referenceTarget = refParams["referenceTarget"]?.ToString() ?? refParams["reference_target"]?.ToString();
            if (string.IsNullOrEmpty(referenceTarget))
            {
                return (false, new ErrorResponse("'set_component_reference.reference_target' is required."));
            }

            // Find the component on targetGo
            if (!ComponentResolver.TryResolve(componentTypeName, out Type componentType, out string resolveError))
            {
                return (false, new ErrorResponse($"Component type '{componentTypeName}' not found: {resolveError}"));
            }

            Component targetComponent = targetGo.GetComponent(componentType);
            if (targetComponent == null)
            {
                return (false, new ErrorResponse($"Component '{componentTypeName}' not found on '{targetGo.name}'."));
            }

            // Parse reference_target - could be "Path/To/Object" or "Path/To/Object:ComponentType"
            string targetObjectPath = referenceTarget;
            string targetComponentType = null;
            int colonIndex = referenceTarget.LastIndexOf(':');
            if (colonIndex > 0 && colonIndex < referenceTarget.Length - 1)
            {
                targetObjectPath = referenceTarget.Substring(0, colonIndex);
                targetComponentType = referenceTarget.Substring(colonIndex + 1);
            }

            // Find the referenced GameObject
            GameObject referencedGo = FindInPrefabContents(prefabRoot, targetObjectPath);
            if (referencedGo == null)
            {
                return (false, new ErrorResponse($"Reference target '{targetObjectPath}' not found in prefab."));
            }

            // Determine what object to assign (GameObject or Component)
            UnityEngine.Object objectToAssign;
            if (!string.IsNullOrEmpty(targetComponentType))
            {
                // User specified a component type, find it on the referenced GameObject
                if (!ComponentResolver.TryResolve(targetComponentType, out Type refCompType, out string refResolveError))
                {
                    return (false, new ErrorResponse($"Reference component type '{targetComponentType}' not found: {refResolveError}"));
                }
                Component refComponent = referencedGo.GetComponent(refCompType);
                if (refComponent == null)
                {
                    return (false, new ErrorResponse($"Component '{targetComponentType}' not found on '{referencedGo.name}'."));
                }
                objectToAssign = refComponent;
            }
            else
            {
                // No component specified - need to determine from field type
                objectToAssign = referencedGo;
            }

            // Find and set the field using reflection
            BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.IgnoreCase;
            Type compType = targetComponent.GetType();

            // Try public field first
            FieldInfo field = compType.GetField(fieldName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);

            // Try private [SerializeField] if public not found
            if (field == null)
            {
                field = compType.GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.IgnoreCase);
                if (field != null && field.GetCustomAttribute<SerializeField>() == null)
                {
                    // Private field without SerializeField - not serialized, skip
                    field = null;
                }
            }

            // Try property as fallback
            PropertyInfo prop = null;
            if (field == null)
            {
                prop = compType.GetProperty(fieldName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            }

            if (field == null && prop == null)
            {
                var availableFields = ComponentResolver.GetAllComponentProperties(compType);
                return (false, new ErrorResponse($"Field '{fieldName}' not found on '{componentTypeName}'. Available: [{string.Join(", ", availableFields)}]"));
            }

            // Check type compatibility and assign
            try
            {
                if (field != null)
                {
                    Type fieldType = field.FieldType;

                    // If the field expects a Component but we have a GameObject, try to get the component
                    if (typeof(Component).IsAssignableFrom(fieldType) && objectToAssign is GameObject go)
                    {
                        Component autoResolvedComp = go.GetComponent(fieldType);
                        if (autoResolvedComp == null)
                        {
                            return (false, new ErrorResponse($"Field '{fieldName}' expects type '{fieldType.Name}', but '{go.name}' does not have that component. Specify the component explicitly using 'reference_target: \"{targetObjectPath}:{fieldType.Name}\"'."));
                        }
                        objectToAssign = autoResolvedComp;
                    }

                    if (!fieldType.IsAssignableFrom(objectToAssign.GetType()))
                    {
                        return (false, new ErrorResponse($"Type mismatch: field '{fieldName}' is of type '{fieldType.Name}', but reference is of type '{objectToAssign.GetType().Name}'."));
                    }

                    field.SetValue(targetComponent, objectToAssign);
                }
                else if (prop != null && prop.CanWrite)
                {
                    Type propType = prop.PropertyType;

                    // If the property expects a Component but we have a GameObject, try to get the component
                    if (typeof(Component).IsAssignableFrom(propType) && objectToAssign is GameObject go)
                    {
                        Component autoResolvedComp = go.GetComponent(propType);
                        if (autoResolvedComp == null)
                        {
                            return (false, new ErrorResponse($"Property '{fieldName}' expects type '{propType.Name}', but '{go.name}' does not have that component."));
                        }
                        objectToAssign = autoResolvedComp;
                    }

                    if (!propType.IsAssignableFrom(objectToAssign.GetType()))
                    {
                        return (false, new ErrorResponse($"Type mismatch: property '{fieldName}' is of type '{propType.Name}', but reference is of type '{objectToAssign.GetType().Name}'."));
                    }

                    prop.SetValue(targetComponent, objectToAssign);
                }
                else
                {
                    return (false, new ErrorResponse($"Field/property '{fieldName}' is not writable."));
                }

                McpLog.Info($"[ManagePrefabs] Set '{componentTypeName}.{fieldName}' to reference '{referencedGo.name}'" +
                    (targetComponentType != null ? $" ({targetComponentType})" : ""));
                return (true, null);
            }
            catch (Exception ex)
            {
                return (false, new ErrorResponse($"Failed to set reference '{fieldName}' on '{componentTypeName}': {ex.Message}"));
            }
        }

        /// <summary>
        /// Sets property values on a component within the prefab.
        /// Supports both single property (property+value) and multiple properties (properties object).
        /// </summary>
        /// <param name="propToken">The property specification token.</param>
        /// <param name="targetGo">The target GameObject within the prefab.</param>
        /// <returns>Tuple of (set: bool, error: ErrorResponse or null).</returns>
        private static (bool set, ErrorResponse error) SetSingleComponentProperty(JToken propToken, GameObject targetGo)
        {
            if (propToken is not JObject propParams)
            {
                return (false, new ErrorResponse("'set_property' must be an object with component_type and either property+value or properties."));
            }

            // Required: component_type
            string componentTypeName = propParams["componentType"]?.ToString() ?? propParams["component_type"]?.ToString();
            if (string.IsNullOrEmpty(componentTypeName))
            {
                return (false, new ErrorResponse("'set_property.component_type' is required."));
            }

            // Find the component on targetGo
            if (!ComponentResolver.TryResolve(componentTypeName, out Type componentType, out string resolveError))
            {
                return (false, new ErrorResponse($"Component type '{componentTypeName}' not found: {resolveError}"));
            }

            Component targetComponent = targetGo.GetComponent(componentType);
            if (targetComponent == null)
            {
                return (false, new ErrorResponse($"Component '{componentTypeName}' not found on '{targetGo.name}'."));
            }

            // Single property mode: property + value
            string propertyName = propParams["property"]?.ToString();
            JToken valueToken = propParams["value"];

            // Multiple properties mode: properties object
            JObject propertiesObj = propParams["properties"] as JObject;

            if (string.IsNullOrEmpty(propertyName) && (propertiesObj == null || !propertiesObj.HasValues))
            {
                return (false, new ErrorResponse("'set_property' requires either 'property'+'value' or 'properties' object."));
            }

            var errors = new List<string>();
            bool anySet = false;

            try
            {
                // Single property mode
                if (!string.IsNullOrEmpty(propertyName) && valueToken != null)
                {
                    if (ComponentOps.SetProperty(targetComponent, propertyName, valueToken, out string error))
                    {
                        McpLog.Info($"[ManagePrefabs] Set '{componentTypeName}.{propertyName}' on '{targetGo.name}'.");
                        anySet = true;
                    }
                    else
                    {
                        errors.Add(error);
                    }
                }

                // Multiple properties mode
                if (propertiesObj != null && propertiesObj.HasValues)
                {
                    foreach (var prop in propertiesObj.Properties())
                    {
                        if (ComponentOps.SetProperty(targetComponent, prop.Name, prop.Value, out string error))
                        {
                            McpLog.Info($"[ManagePrefabs] Set '{componentTypeName}.{prop.Name}' on '{targetGo.name}'.");
                            anySet = true;
                        }
                        else
                        {
                            errors.Add(error);
                        }
                    }
                }

                if (errors.Count > 0)
                {
                    string errorMsg = string.Join("; ", errors);
                    if (anySet)
                    {
                        // Partial success - log warning but continue
                        McpLog.Warn($"[ManagePrefabs] Some properties failed to set on '{componentTypeName}': {errorMsg}");
                    }
                    else
                    {
                        // Complete failure
                        return (false, new ErrorResponse($"Failed to set properties on '{componentTypeName}': {errorMsg}"));
                    }
                }

                return (anySet, null);
            }
            catch (Exception ex)
            {
                return (false, new ErrorResponse($"Error setting properties on '{componentTypeName}': {ex.Message}"));
            }
        }

        #endregion

        #region Hierarchy Builder

        /// <summary>
        /// Builds a flat list of hierarchy items from a transform root.
        /// </summary>
        /// <param name="root">The root transform of the prefab.</param>
        /// <param name="mainPrefabPath">Asset path of the main prefab.</param>
        /// <returns>List of hierarchy items with prefab information.</returns>
        private static List<object> BuildHierarchyItems(Transform root, string mainPrefabPath)
        {
            return BuildHierarchyItemsWithDepth(root, mainPrefabPath, null, null);
        }

        /// <summary>
        /// Builds a flat list of hierarchy items from a transform root with optional depth limit and filter.
        /// </summary>
        /// <param name="root">The root transform of the prefab.</param>
        /// <param name="mainPrefabPath">Asset path of the main prefab.</param>
        /// <param name="maxDepth">Optional maximum depth to traverse (0 = root only, null = unlimited).</param>
        /// <param name="filter">Optional name filter (case-insensitive substring match).</param>
        /// <returns>List of hierarchy items with prefab information.</returns>
        private static List<object> BuildHierarchyItemsWithDepth(Transform root, string mainPrefabPath, int? maxDepth, string filter)
        {
            var items = new List<object>();
            bool hasFilter = !string.IsNullOrEmpty(filter);
            BuildHierarchyItemsRecursiveWithDepth(root, root, mainPrefabPath, "", items, 0, maxDepth, hasFilter ? filter.ToLowerInvariant() : null);
            return items;
        }

        /// <summary>
        /// Recursively builds hierarchy items with depth limiting and filtering.
        /// </summary>
        /// <param name="transform">Current transform being processed.</param>
        /// <param name="mainPrefabRoot">Root transform of the main prefab asset.</param>
        /// <param name="mainPrefabPath">Asset path of the main prefab.</param>
        /// <param name="parentPath">Parent path for building full hierarchy path.</param>
        /// <param name="items">List to accumulate hierarchy items.</param>
        /// <param name="currentDepth">Current depth in the hierarchy (0 = root).</param>
        /// <param name="maxDepth">Maximum depth to traverse (null = unlimited).</param>
        /// <param name="filterLower">Lowercase filter string for name matching (null = no filter).</param>
        private static void BuildHierarchyItemsRecursiveWithDepth(
            Transform transform,
            Transform mainPrefabRoot,
            string mainPrefabPath,
            string parentPath,
            List<object> items,
            int currentDepth,
            int? maxDepth,
            string filterLower)
        {
            if (transform == null) return;

            string name = transform.gameObject.name;
            string path = string.IsNullOrEmpty(parentPath) ? name : $"{parentPath}/{name}";

            // Check filter - include if name contains filter string (case-insensitive)
            bool matchesFilter = filterLower == null || name.ToLowerInvariant().Contains(filterLower);

            if (matchesFilter)
            {
                int instanceId = transform.gameObject.GetInstanceID();
                bool activeSelf = transform.gameObject.activeSelf;
                int childCount = transform.childCount;
                var componentTypes = PrefabUtilityHelper.GetComponentTypeNames(transform.gameObject);

                // Prefab information
                bool isNestedPrefab = PrefabUtility.IsAnyPrefabInstanceRoot(transform.gameObject);
                bool isPrefabRoot = transform == mainPrefabRoot;
                int nestingDepth = isPrefabRoot ? 0 : PrefabUtilityHelper.GetPrefabNestingDepth(transform.gameObject, mainPrefabRoot);
                string parentPrefabPath = isNestedPrefab && !isPrefabRoot
                    ? PrefabUtilityHelper.GetParentPrefabPath(transform.gameObject, mainPrefabRoot)
                    : null;
                string nestedPrefabPath = isNestedPrefab ? PrefabUtilityHelper.GetNestedPrefabPath(transform.gameObject) : null;

                var item = new
                {
                    name = name,
                    instanceId = instanceId,
                    path = path,
                    depth = currentDepth,
                    activeSelf = activeSelf,
                    childCount = childCount,
                    componentTypes = componentTypes,
                    prefab = new
                    {
                        isRoot = isPrefabRoot,
                        isNestedRoot = isNestedPrefab,
                        nestingDepth = nestingDepth,
                        assetPath = isNestedPrefab ? nestedPrefabPath : mainPrefabPath,
                        parentPath = parentPrefabPath
                    }
                };

                items.Add(item);
            }

            // Check depth limit before recursing into children
            // If maxDepth is set and we've reached it, don't process children
            if (maxDepth.HasValue && currentDepth >= maxDepth.Value)
            {
                return;
            }

            // Recursively process children
            foreach (Transform child in transform)
            {
                BuildHierarchyItemsRecursiveWithDepth(child, mainPrefabRoot, mainPrefabPath, path, items, currentDepth + 1, maxDepth, filterLower);
            }
        }

        #endregion
    }
}
