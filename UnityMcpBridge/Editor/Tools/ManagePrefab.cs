using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using MCPForUnity.Editor.Helpers;

namespace MCPForUnity.Editor.Tools
{
    /// <summary>
    /// Handles comprehensive prefab management operations.
    /// Consolidates prefab functionality previously scattered across ManageGameObject and ManageAsset.
    /// </summary>
    public static class ManagePrefab
    {
        /// <summary>
        /// Main handler for all prefab management commands.
        /// </summary>
        public static object HandleCommand(JObject @params)
        {
            if (@params == null)
            {
                return Response.Error("Parameters cannot be null.");
            }

            string action = @params["action"]?.ToString()?.ToLower();
            if (string.IsNullOrEmpty(action))
            {
                return Response.Error("Action parameter is required.");
            }

            try
            {
                switch (action)
                {
                    case "create":
                        return CreatePrefab(@params);
                    case "instantiate":
                        return InstantiatePrefab(@params);
                    case "open":
                        return OpenPrefab(@params);
                    case "close":
                        return ClosePrefab(@params);
                    case "save":
                        return SavePrefab(@params);
                    case "modify":
                        return ModifyPrefab(@params);
                    case "find":
                        return FindPrefabs(@params);
                    case "get_info":
                        return GetPrefabInfo(@params);
                    case "variant":
                        return CreatePrefabVariant(@params);
                    case "unpack":
                        return UnpackPrefab(@params);
                    default:
                        return Response.Error($"Unknown action: '{action}'. Valid actions are: create, instantiate, open, close, save, modify, find, get_info, variant, unpack.");
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[ManagePrefab] Action '{action}' failed: {e}");
                return Response.Error($"Internal error processing action '{action}': {e.Message}");
            }
        }

        /// <summary>
        /// Creates a new prefab from a GameObject or creates an empty prefab.
        /// </summary>
        private static object CreatePrefab(JObject @params)
        {
            string sourceType = @params["sourceType"]?.ToString()?.ToLower() ?? "gameobject";
            string prefabPath = @params["prefabPath"]?.ToString();
            
            if (string.IsNullOrEmpty(prefabPath))
            {
                return Response.Error("prefabPath is required for create action.");
            }

            // Ensure .prefab extension
            if (!prefabPath.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
            {
                prefabPath += ".prefab";
            }

            // Ensure directory exists
            string directory = Path.GetDirectoryName(prefabPath);
            if (!string.IsNullOrEmpty(directory))
            {
                string fullDirPath = Path.Combine(Application.dataPath.Replace("/Assets", ""), directory);
                if (!Directory.Exists(fullDirPath))
                {
                    Directory.CreateDirectory(fullDirPath);
                    AssetDatabase.Refresh();
                }
            }

            try
            {
                GameObject prefabAsset = null;

                switch (sourceType)
                {
                    case "gameobject":
                        prefabAsset = CreateFromGameObject(@params, prefabPath);
                        break;
                    case "empty":
                        prefabAsset = CreateEmptyPrefab(@params, prefabPath);
                        break;
                    default:
                        return Response.Error($"Unknown sourceType: '{sourceType}'. Valid types are: gameobject, empty.");
                }

                if (prefabAsset == null)
                {
                    return Response.Error($"Failed to create prefab at '{prefabPath}'.");
                }

                return Response.Success($"Prefab created successfully at '{prefabPath}'", new
                {
                    prefabPath = prefabPath,
                    prefabName = prefabAsset.name,
                    sourceType = sourceType,
                    guid = AssetDatabase.AssetPathToGUID(prefabPath)
                });
            }
            catch (Exception e)
            {
                Debug.LogError($"[ManagePrefab] Create failed: {e.Message}");
                return Response.Error($"Failed to create prefab: {e.Message}");
            }
        }

        /// <summary>
        /// Creates a prefab from an existing GameObject.
        /// </summary>
        private static GameObject CreateFromGameObject(JObject @params, string prefabPath)
        {
            string sourceObject = @params["sourceObject"]?.ToString();
            if (string.IsNullOrEmpty(sourceObject))
            {
                return Response.Error("sourceObject is required when sourceType is 'gameobject'.") as GameObject;
            }

            // Find the source GameObject
            GameObject sourceGo = null;
            
            // Try by instance ID first
            if (int.TryParse(sourceObject, out int instanceId))
            {
                var allObjects = UnityEngine.Object.FindObjectsOfType<GameObject>(true);
                sourceGo = allObjects.FirstOrDefault(go => go.GetInstanceID() == instanceId);
            }
            
            // Try by name if ID search failed
            if (sourceGo == null)
            {
                sourceGo = GameObject.Find(sourceObject);
            }

            if (sourceGo == null)
            {
                throw new ArgumentException($"Source GameObject '{sourceObject}' not found.");
            }

            // Create the prefab
            GameObject prefabAsset = PrefabUtility.SaveAsPrefabAsset(sourceGo, prefabPath);
            
            if (prefabAsset != null)
            {
                Debug.Log($"[ManagePrefab] Created prefab '{prefabPath}' from GameObject '{sourceGo.name}'");
            }

            return prefabAsset;
        }

        /// <summary>
        /// Creates an empty prefab with optional basic components.
        /// </summary>
        private static GameObject CreateEmptyPrefab(JObject @params, string prefabPath)
        {
            string prefabName = @params["prefabName"]?.ToString() ?? Path.GetFileNameWithoutExtension(prefabPath);
            
            // Create temporary GameObject
            GameObject tempGo = new GameObject(prefabName);
            
            // Add any requested components
            if (@params["components"] is JArray componentsArray)
            {
                foreach (var component in componentsArray)
                {
                    string componentName = component.ToString();
                    Type componentType = GetComponentType(componentName);
                    if (componentType != null && componentType != typeof(Transform))
                    {
                        tempGo.AddComponent(componentType);
                    }
                }
            }

            try
            {
                // Save as prefab
                GameObject prefabAsset = PrefabUtility.SaveAsPrefabAsset(tempGo, prefabPath);
                
                if (prefabAsset != null)
                {
                    Debug.Log($"[ManagePrefab] Created empty prefab '{prefabPath}'");
                }

                return prefabAsset;
            }
            finally
            {
                // Clean up temporary object
                UnityEngine.Object.DestroyImmediate(tempGo);
            }
        }

        /// <summary>
        /// Instantiates a prefab in the current scene.
        /// </summary>
        private static object InstantiatePrefab(JObject @params)
        {
            string prefabPath = @params["prefabPath"]?.ToString();
            if (string.IsNullOrEmpty(prefabPath))
            {
                return Response.Error("prefabPath is required for instantiate action.");
            }

            GameObject prefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefabAsset == null)
            {
                return Response.Error($"Prefab not found at path: '{prefabPath}'");
            }

            try
            {
                GameObject instance = PrefabUtility.InstantiatePrefab(prefabAsset) as GameObject;
                
                if (instance == null)
                {
                    return Response.Error($"Failed to instantiate prefab '{prefabPath}'");
                }

                // Apply position, rotation, scale if provided
                ApplyTransformParams(instance, @params);

                // Set parent if provided
                string parentName = @params["parent"]?.ToString();
                if (!string.IsNullOrEmpty(parentName))
                {
                    GameObject parent = GameObject.Find(parentName);
                    if (parent != null)
                    {
                        instance.transform.SetParent(parent.transform);
                    }
                }

                // Register undo
                Undo.RegisterCreatedObjectUndo(instance, $"Instantiate Prefab '{prefabAsset.name}'");

                return Response.Success($"Prefab '{prefabPath}' instantiated successfully", new
                {
                    instanceName = instance.name,
                    instanceID = instance.GetInstanceID(),
                    prefabPath = prefabPath,
                    position = new { x = instance.transform.position.x, y = instance.transform.position.y, z = instance.transform.position.z }
                });
            }
            catch (Exception e)
            {
                Debug.LogError($"[ManagePrefab] Instantiate failed: {e.Message}");
                return Response.Error($"Failed to instantiate prefab: {e.Message}");
            }
        }

        /// <summary>
        /// Opens a prefab in Prefab Mode for editing.
        /// </summary>
        private static object OpenPrefab(JObject @params)
        {
            string prefabPath = @params["prefabPath"]?.ToString();
            if (string.IsNullOrEmpty(prefabPath))
            {
                return Response.Error("prefabPath is required for open action.");
            }

            // Check if prefab exists
            GameObject prefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefabAsset == null)
            {
                return Response.Error($"Prefab not found at path: '{prefabPath}'");
            }

            try
            {
                bool inContext = @params["inContext"]?.ToObject<bool>() ?? false;
                
                // Open in Prefab Mode
                var stage = PrefabStageUtility.OpenPrefab(prefabPath);
                
                if (stage == null)
                {
                    return Response.Error($"Failed to open prefab '{prefabPath}' in Prefab Mode");
                }

                return Response.Success($"Prefab '{prefabPath}' opened in Prefab Mode", new
                {
                    prefabPath = prefabPath,
                    stagePath = stage.assetPath,
                    prefabName = Path.GetFileNameWithoutExtension(prefabPath),
                    isInContext = stage.mode == PrefabStage.Mode.InContext
                });
            }
            catch (Exception e)
            {
                Debug.LogError($"[ManagePrefab] Open failed: {e.Message}");
                return Response.Error($"Failed to open prefab: {e.Message}");
            }
        }

        /// <summary>
        /// Closes the currently open prefab and returns to main stage.
        /// </summary>
        private static object ClosePrefab(JObject @params)
        {
            var currentStage = PrefabStageUtility.GetCurrentPrefabStage();
            if (currentStage == null)
            {
                return Response.Error("No prefab is currently open in Prefab Mode");
            }

            try
            {
                bool saveChanges = @params["saveChanges"]?.ToObject<bool>() ?? true;
                string prefabPath = currentStage.assetPath;
                bool hadUnsavedChanges = currentStage.scene.isDirty;

                if (saveChanges && hadUnsavedChanges)
                {
                    // Save changes before closing
                    EditorSceneManager.SaveScene(currentStage.scene);
                }

                // Return to main stage
                StageUtility.GoToMainStage();

                return Response.Success($"Closed prefab: '{prefabPath}'", new
                {
                    prefabPath = prefabPath,
                    savedChanges = saveChanges && hadUnsavedChanges,
                    hadUnsavedChanges = hadUnsavedChanges
                });
            }
            catch (Exception e)
            {
                Debug.LogError($"[ManagePrefab] Close failed: {e.Message}");
                return Response.Error($"Failed to close prefab: {e.Message}");
            }
        }

        /// <summary>
        /// Saves the currently open prefab.
        /// </summary>
        private static object SavePrefab(JObject @params)
        {
            var currentStage = PrefabStageUtility.GetCurrentPrefabStage();
            if (currentStage == null)
            {
                return Response.Error("No prefab is currently open in Prefab Mode");
            }

            try
            {
                string prefabPath = currentStage.assetPath;
                
                if (!currentStage.scene.isDirty)
                {
                    return Response.Success($"Prefab '{prefabPath}' has no unsaved changes", new
                    {
                        prefabPath = prefabPath,
                        wasDirty = false
                    });
                }

                // Save the prefab
                EditorSceneManager.SaveScene(currentStage.scene);

                return Response.Success($"Prefab '{prefabPath}' saved successfully", new
                {
                    prefabPath = prefabPath,
                    wasDirty = true
                });
            }
            catch (Exception e)
            {
                Debug.LogError($"[ManagePrefab] Save failed: {e.Message}");
                return Response.Error($"Failed to save prefab: {e.Message}");
            }
        }

        /// <summary>
        /// Finds prefabs based on search criteria.
        /// </summary>
        private static object FindPrefabs(JObject @params)
        {
            string searchTerm = @params["searchTerm"]?.ToString() ?? "";
            string searchType = @params["searchType"]?.ToString()?.ToLower() ?? "name";
            bool includeVariants = @params["includeVariants"]?.ToObject<bool>() ?? true;

            try
            {
                string searchFilter = string.IsNullOrEmpty(searchTerm) 
                    ? "t:Prefab"
                    : $"t:Prefab {searchTerm}";

                string[] guids = AssetDatabase.FindAssets(searchFilter);
                var results = new List<object>();

                foreach (string guid in guids)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);

                    if (prefab != null)
                    {
                        var prefabType = PrefabUtility.GetPrefabAssetType(prefab);
                        bool isVariant = prefabType == PrefabAssetType.Variant;

                        if (!includeVariants && isVariant) continue;

                        results.Add(new
                        {
                            name = prefab.name,
                            path = path,
                            guid = guid,
                            isVariant = isVariant,
                            prefabType = prefabType.ToString(),
                            fileSize = new FileInfo(path).Length
                        });
                    }
                }

                return Response.Success($"Found {results.Count} prefabs", results);
            }
            catch (Exception e)
            {
                Debug.LogError($"[ManagePrefab] Find failed: {e.Message}");
                return Response.Error($"Failed to find prefabs: {e.Message}");
            }
        }

        /// <summary>
        /// Gets detailed information about a specific prefab.
        /// </summary>
        private static object GetPrefabInfo(JObject @params)
        {
            string prefabPath = @params["prefabPath"]?.ToString();
            if (string.IsNullOrEmpty(prefabPath))
            {
                return Response.Error("prefabPath is required for get_info action.");
            }

            GameObject prefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefabAsset == null)
            {
                return Response.Error($"Prefab not found at path: '{prefabPath}'");
            }

            try
            {
                var prefabType = PrefabUtility.GetPrefabAssetType(prefabAsset);
                var components = prefabAsset.GetComponents<Component>();
                
                var info = new
                {
                    name = prefabAsset.name,
                    path = prefabPath,
                    guid = AssetDatabase.AssetPathToGUID(prefabPath),
                    prefabType = prefabType.ToString(),
                    isVariant = prefabType == PrefabAssetType.Variant,
                    componentCount = components.Length,
                    components = components.Where(c => c != null).Select(c => c.GetType().Name).ToArray(),
                    fileSize = new FileInfo(prefabPath).Length,
                    lastModified = File.GetLastWriteTime(prefabPath).ToString("yyyy-MM-dd HH:mm:ss")
                };

                return Response.Success($"Retrieved info for prefab '{prefabPath}'", info);
            }
            catch (Exception e)
            {
                Debug.LogError($"[ManagePrefab] GetInfo failed: {e.Message}");
                return Response.Error($"Failed to get prefab info: {e.Message}");
            }
        }

        /// <summary>
        /// Creates a prefab variant from an existing prefab.
        /// Note: Creates a copy of the prefab rather than a true variant due to Unity API compatibility.
        /// </summary>
        private static object CreatePrefabVariant(JObject @params)
        {
            string basePrefabPath = @params["basePrefabPath"]?.ToString();
            string variantPath = @params["variantPath"]?.ToString();

            if (string.IsNullOrEmpty(basePrefabPath))
            {
                return Response.Error("basePrefabPath is required for variant action.");
            }

            if (string.IsNullOrEmpty(variantPath))
            {
                return Response.Error("variantPath is required for variant action.");
            }

            GameObject basePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(basePrefabPath);
            if (basePrefab == null)
            {
                return Response.Error($"Base prefab not found at path: '{basePrefabPath}'");
            }

            try
            {
                // Ensure .prefab extension
                if (!variantPath.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
                {
                    variantPath += ".prefab";
                }

                // Create a copy of the prefab (simpler approach for compatibility)
                bool copySuccess = AssetDatabase.CopyAsset(basePrefabPath, variantPath);
                if (!copySuccess)
                {
                    return Response.Error($"Failed to copy prefab from '{basePrefabPath}' to '{variantPath}'");
                }

                // Load the copied prefab
                GameObject variant = AssetDatabase.LoadAssetAtPath<GameObject>(variantPath);
                if (variant == null)
                {
                    return Response.Error($"Failed to load copied prefab at '{variantPath}'");
                }

                return Response.Success($"Prefab copy created at '{variantPath}' (Note: This is a copy, not a true variant)", new
                {
                    variantPath = variantPath,
                    basePrefabPath = basePrefabPath,
                    variantName = variant.name,
                    guid = AssetDatabase.AssetPathToGUID(variantPath),
                    isTrueVariant = false // Indicate this is a copy, not a true variant
                });
            }
            catch (Exception e)
            {
                Debug.LogError($"[ManagePrefab] CreateVariant failed: {e.Message}");
                return Response.Error($"Failed to create prefab variant: {e.Message}");
            }
        }

        /// <summary>
        /// Unpacks a prefab instance to regular GameObjects.
        /// </summary>
        private static object UnpackPrefab(JObject @params)
        {
            string targetObject = @params["targetObject"]?.ToString();
            if (string.IsNullOrEmpty(targetObject))
            {
                return Response.Error("targetObject is required for unpack action.");
            }

            // Find the target GameObject
            GameObject target = FindGameObjectByNameOrId(targetObject);
            if (target == null)
            {
                return Response.Error($"Target GameObject '{targetObject}' not found.");
            }

            // Check if it's a prefab instance
            if (PrefabUtility.GetPrefabInstanceStatus(target) == PrefabInstanceStatus.NotAPrefab)
            {
                return Response.Error($"GameObject '{target.name}' is not a prefab instance.");
            }

            try
            {
                bool unpackCompletely = @params["unpackCompletely"]?.ToObject<bool>() ?? false;
                
                if (unpackCompletely)
                {
                    PrefabUtility.UnpackPrefabInstance(target, PrefabUnpackMode.Completely, InteractionMode.UserAction);
                }
                else
                {
                    PrefabUtility.UnpackPrefabInstance(target, PrefabUnpackMode.OutermostRoot, InteractionMode.UserAction);
                }

                return Response.Success($"Prefab instance '{target.name}' unpacked successfully", new
                {
                    targetName = target.name,
                    instanceID = target.GetInstanceID(),
                    unpackedCompletely = unpackCompletely
                });
            }
            catch (Exception e)
            {
                Debug.LogError($"[ManagePrefab] Unpack failed: {e.Message}");
                return Response.Error($"Failed to unpack prefab instance: {e.Message}");
            }
        }

        /// <summary>
        /// Modifies objects within a prefab by opening it, making changes, and optionally saving.
        /// </summary>
        private static object ModifyPrefab(JObject @params)
        {
            string prefabPath = @params["prefabPath"]?.ToString();
            if (string.IsNullOrEmpty(prefabPath))
            {
                return Response.Error("prefabPath is required for modify action.");
            }

            string modifyTarget = @params["modifyTarget"]?.ToString();
            if (string.IsNullOrEmpty(modifyTarget))
            {
                return Response.Error("modifyTarget is required for modify action. Specify the name of the object to modify within the prefab.");
            }

            GameObject prefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefabAsset == null)
            {
                return Response.Error($"Prefab not found at path: '{prefabPath}'");
            }

            try
            {
                // Clear any existing selection first to prevent inspector conflicts
                Selection.activeObject = null;
                
                bool autoSave = @params["autoSave"]?.ToObject<bool>() ?? true;
                bool wasInPrefabMode = PrefabStageUtility.GetCurrentPrefabStage() != null;
                string originalPrefabPath = null;
                
                // If we're already in prefab mode, note the current prefab
                if (wasInPrefabMode)
                {
                    originalPrefabPath = PrefabStageUtility.GetCurrentPrefabStage().assetPath;
                }

                // Open the target prefab if not already open, or if it's a different prefab
                PrefabStage stage = null;
                if (!wasInPrefabMode || originalPrefabPath != prefabPath)
                {
                    stage = PrefabStageUtility.OpenPrefab(prefabPath);
                    if (stage == null)
                    {
                        return Response.Error($"Failed to open prefab '{prefabPath}' in Prefab Mode");
                    }
                }
                else
                {
                    stage = PrefabStageUtility.GetCurrentPrefabStage();
                }

                // Find the target object within the prefab
                GameObject targetObject = FindObjectInPrefabStage(stage, modifyTarget);
                if (targetObject == null)
                {
                    return Response.Error($"Object '{modifyTarget}' not found in prefab '{prefabPath}'. Available objects: {GetObjectNamesInPrefabStage(stage)}");
                }

                // Clear any current selection to prevent Inspector issues
                Selection.activeObject = null;
                
                // Record undo for the modification - be more careful with what we record
                Undo.RecordObject(targetObject.transform, $"Modify Prefab Object '{modifyTarget}'");
                
                // Only record the GameObject itself if we're going to modify non-transform properties
                bool willModifyGameObject = @params["modifyActive"] != null || 
                                          @params["modifyName"] != null || 
                                          @params["modifyTag"] != null || 
                                          @params["modifyLayer"] != null;
                                          
                if (willModifyGameObject)
                {
                    Undo.RecordObject(targetObject, $"Modify Prefab Object '{modifyTarget}'");
                }

                // Apply modifications
                var modifications = new List<string>();

                // Transform modifications
                if (@params["modifyPosition"] is JArray posArray && posArray.Count >= 3)
                {
                    Vector3 newPos = new Vector3(
                        posArray[0].ToObject<float>(),
                        posArray[1].ToObject<float>(),
                        posArray[2].ToObject<float>()
                    );
                    targetObject.transform.localPosition = newPos;
                    modifications.Add($"position to {newPos}");
                }

                if (@params["modifyRotation"] is JArray rotArray && rotArray.Count >= 3)
                {
                    Vector3 newRot = new Vector3(
                        rotArray[0].ToObject<float>(),
                        rotArray[1].ToObject<float>(),
                        rotArray[2].ToObject<float>()
                    );
                    targetObject.transform.localEulerAngles = newRot;
                    modifications.Add($"rotation to {newRot}");
                }

                if (@params["modifyScale"] is JArray scaleArray && scaleArray.Count >= 3)
                {
                    Vector3 newScale = new Vector3(
                        scaleArray[0].ToObject<float>(),
                        scaleArray[1].ToObject<float>(),
                        scaleArray[2].ToObject<float>()
                    );
                    targetObject.transform.localScale = newScale;
                    modifications.Add($"scale to {newScale}");
                }

                // GameObject property modifications
                if (@params["modifyActive"] is JToken activeToken)
                {
                    bool newActive = activeToken.ToObject<bool>();
                    targetObject.SetActive(newActive);
                    modifications.Add($"active to {newActive}");
                }

                if (@params["modifyName"] is JToken nameToken)
                {
                    string newName = nameToken.ToString();
                    targetObject.name = newName;
                    modifications.Add($"name to '{newName}'");
                }

                if (@params["modifyTag"] is JToken tagToken)
                {
                    string newTag = tagToken.ToString();
                    targetObject.tag = newTag;
                    modifications.Add($"tag to '{newTag}'");
                }

                if (@params["modifyLayer"] is JToken layerToken)
                {
                    string layerName = layerToken.ToString();
                    int layer = LayerMask.NameToLayer(layerName);
                    if (layer != -1)
                    {
                        targetObject.layer = layer;
                        modifications.Add($"layer to '{layerName}'");
                    }
                    else
                    {
                        Debug.LogWarning($"[ManagePrefab] Layer '{layerName}' not found");
                    }
                }

                // Ensure modifications are applied immediately
                EditorUtility.SetDirty(targetObject);
                
                // Mark the scene as dirty to enable saving
                EditorSceneManager.MarkSceneDirty(stage.scene);

                // Auto-save if requested
                if (autoSave)
                {
                    try
                    {
                        EditorSceneManager.SaveScene(stage.scene);
                        modifications.Add("auto-saved");
                    }
                    catch (Exception saveEx)
                    {
                        Debug.LogWarning($"[ManagePrefab] Auto-save failed: {saveEx.Message}");
                        modifications.Add("save-failed");
                    }
                }

                // Clear selection again to prevent lingering references
                Selection.activeObject = null;
                
                // Force a repaint to update the editor
                EditorApplication.QueuePlayerLoopUpdate();
                SceneView.RepaintAll();

                var result = new
                {
                    prefabPath = prefabPath,
                    targetObject = modifyTarget,
                    modifications = modifications,
                    saved = autoSave && !modifications.Contains("save-failed"),
                    stageKeptOpen = true
                };

                Debug.Log($"[ManagePrefab] Modified '{modifyTarget}' in prefab '{prefabPath}': {string.Join(", ", modifications)}");

                return Response.Success($"Successfully modified '{modifyTarget}' in prefab '{prefabPath}'", result);
            }
            catch (Exception e)
            {
                // Clear selection on error to prevent lingering inspector issues
                try { Selection.activeObject = null; } catch { }
                
                Debug.LogError($"[ManagePrefab] Modify failed: {e.Message}\n{e.StackTrace}");
                
                // Provide more specific error messages for common issues
                string errorMessage = e.Message;
                if (e is NullReferenceException)
                {
                    errorMessage = "Unity Editor state conflict. Try closing and reopening the prefab, or restart Unity Editor.";
                }
                else if (e.Message.Contains("Inspector"))
                {
                    errorMessage = "Unity Inspector conflict. The modification was attempted but may have caused editor issues.";
                }
                
                return Response.Error($"Failed to modify prefab: {errorMessage}");
            }
        }

        // --- Helper Methods ---

        private static void ApplyTransformParams(GameObject obj, JObject @params)
        {
            if (@params["position"] is JArray posArray && posArray.Count >= 3)
            {
                obj.transform.position = new Vector3(
                    posArray[0].ToObject<float>(),
                    posArray[1].ToObject<float>(),
                    posArray[2].ToObject<float>()
                );
            }

            if (@params["rotation"] is JArray rotArray && rotArray.Count >= 3)
            {
                obj.transform.eulerAngles = new Vector3(
                    rotArray[0].ToObject<float>(),
                    rotArray[1].ToObject<float>(),
                    rotArray[2].ToObject<float>()
                );
            }

            if (@params["scale"] is JArray scaleArray && scaleArray.Count >= 3)
            {
                obj.transform.localScale = new Vector3(
                    scaleArray[0].ToObject<float>(),
                    scaleArray[1].ToObject<float>(),
                    scaleArray[2].ToObject<float>()
                );
            }
        }

        private static GameObject FindGameObjectByNameOrId(string nameOrId)
        {
            // Try by instance ID first
            if (int.TryParse(nameOrId, out int instanceId))
            {
                var allObjects = UnityEngine.Object.FindObjectsOfType<GameObject>(true);
                var obj = allObjects.FirstOrDefault(go => go.GetInstanceID() == instanceId);
                if (obj != null) return obj;
            }

            // Try by name
            return GameObject.Find(nameOrId);
        }

        private static Type GetComponentType(string componentName)
        {
            // Try common Unity types first
            var unityTypes = new[]
            {
                typeof(Rigidbody), typeof(Collider), typeof(Renderer), typeof(Light),
                typeof(Camera), typeof(AudioSource), typeof(Animation), typeof(Animator)
            };

            foreach (var type in unityTypes)
            {
                if (type.Name.Equals(componentName, StringComparison.OrdinalIgnoreCase))
                    return type;
            }

            // Try to find in all loaded assemblies
            foreach (var assembly in System.AppDomain.CurrentDomain.GetAssemblies())
            {
                var type = assembly.GetTypes().FirstOrDefault(t => 
                    typeof(Component).IsAssignableFrom(t) && 
                    t.Name.Equals(componentName, StringComparison.OrdinalIgnoreCase));
                if (type != null) return type;
            }

            return null;
        }

        /// <summary>
        /// Finds an object by name within a prefab stage.
        /// </summary>
        private static GameObject FindObjectInPrefabStage(PrefabStage stage, string objectName)
        {
            if (stage == null || string.IsNullOrEmpty(objectName))
                return null;

            // Search in the prefab root and all its children
            Transform[] allTransforms = stage.prefabContentsRoot.GetComponentsInChildren<Transform>(true);
            
            foreach (Transform t in allTransforms)
            {
                if (t.name.Equals(objectName, StringComparison.OrdinalIgnoreCase))
                {
                    return t.gameObject;
                }
            }

            return null;
        }

        /// <summary>
        /// Gets a list of all object names in a prefab stage for error reporting.
        /// </summary>
        private static string GetObjectNamesInPrefabStage(PrefabStage stage)
        {
            if (stage == null)
                return "No prefab stage available";

            try
            {
                Transform[] allTransforms = stage.prefabContentsRoot.GetComponentsInChildren<Transform>(true);
                var names = allTransforms.Select(t => t.name).Where(n => !string.IsNullOrEmpty(n)).Distinct().Take(10);
                string result = string.Join(", ", names);
                
                if (allTransforms.Length > 10)
                {
                    result += $" (and {allTransforms.Length - 10} more)";
                }
                
                return result;
            }
            catch (Exception e)
            {
                return $"Error listing objects: {e.Message}";
            }
        }
    }
}