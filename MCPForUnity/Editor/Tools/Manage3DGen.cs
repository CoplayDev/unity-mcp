using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Runtime;
using Trellis.Editor;

namespace MCPForUnity.Editor.Tools
{
    /// <summary>
    /// Manages 3D model generation and object transformation using Trellis AI.
    /// Supports generating new objects or transforming existing scene objects.
    /// </summary>
    [McpForUnityTool("manage_3d_gen", AutoRegister = false, RequiresPolling = true, PollAction = "status")]
    public static class Manage3DGen
    {
        private const string ToolName = "manage_3d_gen";
        private const float DefaultPollIntervalSeconds = 3f;
        private const int MaxPromptAssetCacheEntries = 32;
        private const int MaxTrellisLogEntries = 40;

        // Valid actions for this tool
        private static readonly List<string> ValidActions = new List<string>
        {
            "generate",    // Generate a new 3D object from prompt
            "transform",   // Transform existing source object to target
            "status",      // Poll action for checking generation status
            "revert",      // Revert to previous state
            "revert_original", // Revert to original state
            "list_history" // List all objects with transform history
        };

        /// <summary>
        /// State persisted across domain reloads for async Trellis generation.
        /// </summary>
        [Serializable]
        private class GenerationJobState
        {
            public string status;           // "searching", "generating", "loading_glb", "instantiating", "completed", "error"
            public string actionType;       // "generate" or "transform"
            public string sourceObjectId;   // Instance ID of source object (for transform)
            public string sourceObjectName; // Name of source object (for transform)
            public string targetPrompt;
            public string promptKey;
            public string foundAssetPath;   // If found existing asset
            public bool isGenerating;       // True if waiting for Trellis
            public string generatedGlbPath; // Path to generated GLB
            public string errorMessage;
            public float[] originalPosition;    // [x, y, z]
            public float[] originalRotation;    // [x, y, z] Euler angles
            public float[] originalScale;       // [x, y, z]
            public float[] originalBoundsSize;  // [x, y, z] (for transform)
            public string originalParentPath;
            public int originalSiblingIndex;
            public string gltfLoadingContainerId; // Instance ID of container waiting for glTFast loading
            public string importAssetPath;
            public long? importFileSizeBytes;
            public string importStage;
            public bool usedGltfFast;
            public List<string> importLogs;
        }

        private class PromptAssetRecord
        {
            public string assetPath;
            public DateTime lastUsedUtc;
            public long fileSize;
        }

        private class AssetCandidate
        {
            public string path;
            public float score;
            public DateTime? timestampUtc;
        }

        // Track active Trellis generation
        private static bool s_waitingForTrellis = false;
        private static string s_pendingGlbPath = null;
        private static readonly Dictionary<string, PromptAssetRecord> s_promptAssetCache = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Helper to get a parameter value supporting both snake_case and camelCase keys.
        /// This ensures compatibility with batch_execute which converts snake_case to camelCase.
        /// </summary>
        private static JToken GetParam(JObject @params, string snakeCaseKey)
        {
            // Try snake_case first (direct calls), then camelCase (batch_execute calls)
            return @params[snakeCaseKey] ?? @params[ToCamelCase(snakeCaseKey)];
        }

        /// <summary>
        /// Converts snake_case to camelCase for parameter lookup.
        /// </summary>
        private static string ToCamelCase(string key)
        {
            if (string.IsNullOrEmpty(key) || key.IndexOf('_') < 0)
                return key;

            var parts = key.Split(new[] { '_' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
                return key;

            var result = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                if (!string.IsNullOrEmpty(parts[i]))
                {
                    result += char.ToUpperInvariant(parts[i][0]) + parts[i].Substring(1);
                }
            }
            return result;
        }

        private static void AppendStateLog(GenerationJobState state, string message)
        {
            if (state == null || string.IsNullOrWhiteSpace(message))
            {
                return;
            }

            state.importLogs ??= new List<string>();
            state.importLogs.Add($"{DateTime.UtcNow:O} {message}");
            if (state.importLogs.Count > MaxTrellisLogEntries)
            {
                state.importLogs.RemoveRange(0, state.importLogs.Count - MaxTrellisLogEntries);
            }
        }

        private static void AppendPersistedStateLog(string message)
        {
            var state = McpJobStateStore.LoadState<GenerationJobState>(ToolName);
            if (state == null)
            {
                return;
            }

            AppendStateLog(state, message);
            McpJobStateStore.SaveState(ToolName, state);
        }

        private static void TrackImportedAsset(GenerationJobState state, string assetPath, string stage)
        {
            if (state == null || string.IsNullOrWhiteSpace(assetPath))
            {
                return;
            }

            string normalizedPath = ToAssetsRelativePath(assetPath);
            state.importAssetPath = normalizedPath;
            state.importFileSizeBytes = TryGetAssetFileSize(normalizedPath);
            state.importStage = stage;

            string sizeText = state.importFileSizeBytes.HasValue
                ? $"{state.importFileSizeBytes.Value} bytes"
                : "size unknown";
            AppendStateLog(state, $"Import stage '{stage}': {normalizedPath} ({sizeText}).");
        }

        private static void MarkStateError(GenerationJobState state, string message)
        {
            if (state == null)
            {
                return;
            }

            state.status = "error";
            state.errorMessage = message;
            AppendStateLog(state, $"ERROR: {message}");
        }

        private static object BuildStatusData(GenerationJobState state)
        {
            if (state == null)
            {
                return null;
            }

            return new
            {
                state.status,
                state.actionType,
                state.sourceObjectName,
                state.targetPrompt,
                state.importStage,
                importAssetPath = state.importAssetPath ?? state.foundAssetPath ?? state.generatedGlbPath,
                state.importFileSizeBytes,
                state.usedGltfFast,
                importLogs = state.importLogs ?? new List<string>()
            };
        }

        public static object HandleCommand(JObject @params)
        {
            string action = GetParam(@params, "action")?.ToString()?.ToLower() ?? "generate";

            if (!ValidActions.Contains(action))
            {
                string validActionsList = string.Join(", ", ValidActions);
                return new ErrorResponse($"Unknown action: '{action}'. Valid actions are: {validActionsList}");
            }

            try
            {
                switch (action)
                {
                    case "generate":
                        return StartGenerate(@params);
                    case "transform":
                        return StartTransform(@params);
                    case "status":
                        return CheckStatus(@params);
                    case "revert":
                        return RevertObject(@params, revertToOriginal: false);
                    case "revert_original":
                        return RevertObject(@params, revertToOriginal: true);
                    case "list_history":
                        return ListTransformHistory();
                    default:
                        return new ErrorResponse($"Unhandled action: {action}");
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[Manage3DGen] Error: {e.Message}\n{e.StackTrace}");
                return new ErrorResponse($"Error executing manage_3d_gen: {e.Message}");
            }
        }

        /// <summary>
        /// Initiates the generate operation - creates a NEW 3D object from a prompt.
        /// </summary>
        private static object StartGenerate(JObject @params)
        {
            string targetName = GetParam(@params, "target_name")?.ToString();
            bool searchExisting = GetParam(@params, "search_existing")?.ToObject<bool>() ?? true;
            bool generateIfMissing = GetParam(@params, "generate_if_missing")?.ToObject<bool>() ?? true;

            if (string.IsNullOrEmpty(targetName))
                return new ErrorResponse("'target_name' parameter is required for generate action.");

            // Parse position, rotation, scale with defaults
            float[] position = ParseVector3Array(GetParam(@params, "position")) ?? new float[] { 0, 0, 0 };
            float[] rotation = ParseVector3Array(GetParam(@params, "rotation")) ?? new float[] { 0, 0, 0 };
            float[] scale = ParseVector3Array(GetParam(@params, "scale")) ?? new float[] { 1, 1, 1 };
            string parentPath = GetParam(@params, "parent")?.ToString();
            string promptKey = NormalizePromptKey(targetName);

            var state = new GenerationJobState
            {
                status = "searching",
                actionType = "generate",
                targetPrompt = targetName,
                promptKey = promptKey,
                originalPosition = position,
                originalRotation = rotation,
                originalScale = scale,
                originalParentPath = parentPath,
                originalSiblingIndex = -1 // Will be set to last sibling
            };
            AppendStateLog(state, $"Requested generate for '{targetName}' (search_existing={searchExisting}, generate_if_missing={generateIfMissing}).");

            // Step 1: Search for existing asset
            if (searchExisting)
            {
                string foundPath = SearchForAsset(targetName, promptKey);
                if (!string.IsNullOrEmpty(foundPath))
                {
                    state.foundAssetPath = foundPath;
                    state.status = "instantiating";
                    TrackImportedAsset(state, foundPath, "asset_search_hit");
                    AppendStateLog(state, "Found an existing matching asset. Skipping Trellis generation.");
                    McpJobStateStore.SaveState(ToolName, state);

                    // Immediately instantiate and complete
                    return CompleteGenerate(state);
                }
            }

            // Step 2: Generate with Trellis if no asset found
            if (generateIfMissing)
            {
                state.status = "generating";
                state.isGenerating = true;
                AppendStateLog(state, "No matching asset found. Starting Trellis generation.");
                McpJobStateStore.SaveState(ToolName, state);

                // Start Trellis generation
                StartTrellisGeneration(targetName, state);

                return new PendingResponse(
                    $"No existing asset found for '{targetName}'. Starting Trellis generation...",
                    DefaultPollIntervalSeconds,
                    BuildStatusData(state)
                );
            }

            return new ErrorResponse($"No asset found for '{targetName}' and generation is disabled.");
        }

        /// <summary>
        /// Initiates the transform operation - replaces an existing object.
        /// </summary>
        private static object StartTransform(JObject @params)
        {
            string sourceObject = GetParam(@params, "source_object")?.ToString();
            string targetName = GetParam(@params, "target_name")?.ToString();
            bool searchExisting = GetParam(@params, "search_existing")?.ToObject<bool>() ?? true;
            bool generateIfMissing = GetParam(@params, "generate_if_missing")?.ToObject<bool>() ?? true;
            string promptKey = NormalizePromptKey(targetName);

            if (string.IsNullOrEmpty(sourceObject))
                return new ErrorResponse("'source_object' parameter is required for transform action.");
            if (string.IsNullOrEmpty(targetName))
                return new ErrorResponse("'target_name' parameter is required for transform action.");

            // Find the source object in scene
            GameObject sourceGo = FindSceneObject(sourceObject);
            if (sourceGo == null)
                return new ErrorResponse($"Source object '{sourceObject}' not found in scene.");

            // Capture source object info
            var sourceBounds = GetObjectBounds(sourceGo);
            var sourceTransform = sourceGo.transform;

            var state = new GenerationJobState
            {
                status = "searching",
                actionType = "transform",
                sourceObjectId = sourceGo.GetInstanceID().ToString(),
                sourceObjectName = sourceGo.name,
                targetPrompt = targetName,
                promptKey = promptKey,
                originalPosition = new float[] { sourceTransform.position.x, sourceTransform.position.y, sourceTransform.position.z },
                originalRotation = new float[] { sourceTransform.eulerAngles.x, sourceTransform.eulerAngles.y, sourceTransform.eulerAngles.z },
                originalScale = new float[] { sourceTransform.localScale.x, sourceTransform.localScale.y, sourceTransform.localScale.z },
                originalBoundsSize = new float[] { sourceBounds.size.x, sourceBounds.size.y, sourceBounds.size.z },
                originalParentPath = GetGameObjectPath(sourceTransform.parent?.gameObject),
                originalSiblingIndex = sourceTransform.GetSiblingIndex()
            };
            AppendStateLog(state, $"Requested transform '{sourceGo.name}' -> '{targetName}' (search_existing={searchExisting}, generate_if_missing={generateIfMissing}).");

            // Step 1: Search for existing asset
            if (searchExisting)
            {
                string foundPath = SearchForAsset(targetName, promptKey);
                if (!string.IsNullOrEmpty(foundPath))
                {
                    state.foundAssetPath = foundPath;
                    state.status = "instantiating";
                    TrackImportedAsset(state, foundPath, "asset_search_hit");
                    AppendStateLog(state, "Found an existing matching asset. Skipping Trellis generation.");
                    McpJobStateStore.SaveState(ToolName, state);

                    // Immediately instantiate and complete
                    return CompleteTransform(state, sourceGo);
                }
            }

            // Step 2: Generate with Trellis if no asset found
            if (generateIfMissing)
            {
                state.status = "generating";
                state.isGenerating = true;
                AppendStateLog(state, "No matching asset found. Starting Trellis generation.");
                McpJobStateStore.SaveState(ToolName, state);

                // Start Trellis generation
                StartTrellisGeneration(targetName, state);

                return new PendingResponse(
                    $"No existing asset found for '{targetName}'. Starting Trellis generation...",
                    DefaultPollIntervalSeconds,
                    BuildStatusData(state)
                );
            }

            return new ErrorResponse($"No asset found for '{targetName}' and generation is disabled.");
        }

        /// <summary>
        /// Checks the status of an ongoing generation/transform operation (poll action).
        /// </summary>
        private static object CheckStatus(JObject @params)
        {
            var state = McpJobStateStore.LoadState<GenerationJobState>(ToolName);
            if (state == null)
            {
                return new { _mcp_status = "complete", message = "No active generation job." };
            }

            switch (state.status)
            {
                case "completed":
                    McpJobStateStore.ClearState(ToolName);
                    string completedMessage = state.actionType == "generate"
                        ? $"Generation completed: '{state.targetPrompt}'"
                        : $"Transform completed: '{state.sourceObjectName}' → '{state.targetPrompt}'";
                    return new
                    {
                        _mcp_status = "complete",
                        message = completedMessage,
                        data = new
                        {
                            actionType = state.actionType,
                            sourceObject = state.sourceObjectName,
                            targetPrompt = state.targetPrompt,
                            assetUsed = state.foundAssetPath ?? state.generatedGlbPath,
                            wasGenerated = !string.IsNullOrEmpty(state.generatedGlbPath),
                            trellisImport = BuildStatusData(state)
                        }
                    };

                case "error":
                    McpJobStateStore.ClearState(ToolName);
                    return new
                    {
                        _mcp_status = "error",
                        error = state.errorMessage ?? "Unknown error during operation",
                        data = BuildStatusData(state)
                    };

                case "generating":
                    // Check if Trellis has completed
                    if (!string.IsNullOrEmpty(s_pendingGlbPath))
                    {
                        state.generatedGlbPath = s_pendingGlbPath;
                        state.status = "instantiating";
                        s_pendingGlbPath = null;
                        s_waitingForTrellis = false;
                        TrackImportedAsset(state, state.generatedGlbPath, "trellis_output_ready");
                        AppendStateLog(state, "Trellis generation completed and GLB path received.");
                        McpJobStateStore.SaveState(ToolName, state);

                        // Handle based on action type
                        if (state.actionType == "generate")
                        {
                            return CompleteGenerate(state);
                        }
                        else
                        {
                            // Transform action - find source object again and complete
                            if (int.TryParse(state.sourceObjectId, out int instanceId))
                            {
                                GameObject sourceGo = EditorUtility.InstanceIDToObject(instanceId) as GameObject;
                                if (sourceGo != null)
                                {
                                    return CompleteTransform(state, sourceGo);
                                }
                            }

                            state.status = "error";
                            state.errorMessage = "Source object was destroyed during generation.";
                            AppendStateLog(state, $"ERROR: {state.errorMessage}");
                            McpJobStateStore.SaveState(ToolName, state);
                        }
                    }

                    return new PendingResponse(
                        $"Generating model for '{state.targetPrompt}'...",
                        DefaultPollIntervalSeconds,
                        BuildStatusData(state)
                    );

                case "loading_glb":
                    // Check if glTFast has completed loading
                    if (!string.IsNullOrEmpty(state.gltfLoadingContainerId) && 
                        int.TryParse(state.gltfLoadingContainerId, out int containerId))
                    {
                        GameObject container = EditorUtility.InstanceIDToObject(containerId) as GameObject;
                        
                        // Check if container has children (loading complete) or if loading failed
                        if (container == null)
                        {
                            MarkStateError(state, "glTFast loading failed - container was destroyed.");
                            McpJobStateStore.SaveState(ToolName, state);
                            return new ErrorResponse(state.errorMessage);
                        }
                        
                        // Check if loading is complete (container has children when glTFast finishes)
                        if (container.transform.childCount > 0 || !s_gltfLoadingInProgress)
                        {
                            if (container.transform.childCount > 0)
                            {
                                Debug.Log($"[Manage3DGen] glTFast loading complete, container has {container.transform.childCount} children");
                                AppendStateLog(state, $"glTFast container is ready with {container.transform.childCount} child object(s).");
                                return FinalizeGltfLoading(state, container);
                            }
                            else
                            {
                                // Loading finished but no children - failure
                                string gltfError = !string.IsNullOrEmpty(s_gltfLoadingError)
                                    ? $" glTFast error: {s_gltfLoadingError}"
                                    : string.Empty;
                                MarkStateError(state, $"glTFast loading completed but no model was instantiated.{gltfError}");
                                UnityEngine.Object.DestroyImmediate(container);
                                McpJobStateStore.SaveState(ToolName, state);
                                return new ErrorResponse(state.errorMessage);
                            }
                        }
                    }
                    
                    return new PendingResponse(
                        $"Loading GLB model for '{state.targetPrompt}'...",
                        DefaultPollIntervalSeconds,
                        BuildStatusData(state)
                    );

                default:
                    return new PendingResponse(
                        $"Operation in progress: {state.status}",
                        DefaultPollIntervalSeconds,
                        BuildStatusData(state)
                    );
            }
        }

        /// <summary>
        /// Finalizes the object after glTFast loading is complete.
        /// </summary>
        private static object FinalizeGltfLoading(GenerationJobState state, GameObject container)
        {
            bool isPlayMode = EditorApplication.isPlaying;
            
            // Name the object
            container.name = state.targetPrompt;
            
            // Apply transform - position and rotation from parameters
            Vector3 position = new Vector3(state.originalPosition[0], state.originalPosition[1], state.originalPosition[2]);
            Vector3 rotation = new Vector3(state.originalRotation[0], state.originalRotation[1], state.originalRotation[2]);
            Vector3 scale = new Vector3(state.originalScale[0], state.originalScale[1], state.originalScale[2]);

            container.transform.position = position;
            container.transform.rotation = Quaternion.Euler(rotation);
            container.transform.localScale = scale;

            // Set parent if specified
            if (!string.IsNullOrEmpty(state.originalParentPath))
            {
                GameObject parent = FindSceneObject(state.originalParentPath);
                if (parent != null)
                {
                    container.transform.SetParent(parent.transform);
                }
            }

            // Mark scene dirty (only in Edit Mode)
            if (!isPlayMode)
            {
                EditorUtility.SetDirty(container);
                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                    UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());
            }

            // Update state
            state.status = "completed";
            state.gltfLoadingContainerId = null;
            state.usedGltfFast = true;
            TrackImportedAsset(state, state.generatedGlbPath ?? state.foundAssetPath, "gltfast_finalize");
            AppendStateLog(state, $"glTFast finalized for '{state.targetPrompt}' with {container.transform.childCount} child object(s).");
            McpJobStateStore.SaveState(ToolName, state);

            Selection.activeGameObject = container;

            RememberPromptAsset(state.targetPrompt, state.generatedGlbPath ?? state.foundAssetPath);

            return new SuccessResponse(
                $"Successfully generated '{state.targetPrompt}'" + (isPlayMode ? (IsGltfFastAvailable() ? " (Play Mode via glTFast)" : " (Play Mode)") : ""),
                new
                {
                    newObjectName = container.name,
                    newObjectId = container.GetInstanceID(),
                    assetUsed = state.generatedGlbPath ?? state.foundAssetPath,
                    wasGenerated = !string.IsNullOrEmpty(state.generatedGlbPath),
                    playMode = isPlayMode,
                    loadedViaGltfFast = IsGltfFastAvailable(),
                    position = new { x = position.x, y = position.y, z = position.z },
                    rotation = new { x = rotation.x, y = rotation.y, z = rotation.z },
                    scale = new { x = scale.x, y = scale.y, z = scale.z },
                    trellisImport = BuildStatusData(state)
                }
            );
        }

        /// <summary>
        /// Finds an object that Trellis may have auto-instantiated from the GLB.
        /// Trellis typically creates objects with names matching the GLB filename.
        /// </summary>
        private static GameObject FindTrellisInstantiatedObject(string glbPath)
        {
            if (string.IsNullOrEmpty(glbPath)) return null;

            string baseName = Path.GetFileNameWithoutExtension(glbPath);
            
            // Look for recently created objects that match the GLB name
            var allObjects = UnityEngine.Object.FindObjectsByType<GameObject>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);

            foreach (var obj in allObjects)
            {
                // Check for exact match or match with (Clone) suffix
                if (obj.name == baseName || 
                    obj.name == baseName + "(Clone)" ||
                    obj.name.StartsWith(baseName))
                {
                    // Verify it's at origin (Trellis default placement)
                    if (obj.transform.position == Vector3.zero && obj.transform.parent == null)
                    {
                        Debug.Log($"[Manage3DGen] Found Trellis-instantiated object: {obj.name}");
                        return obj;
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Completes the generate action by instantiating a NEW 3D object.
        /// If Trellis already instantiated the object, we reuse it instead of creating a duplicate.
        /// In Play Mode with GLB files, uses glTFast async loading.
        /// </summary>
        private static object CompleteGenerate(GenerationJobState state)
        {
            string assetPath = state.foundAssetPath ?? state.generatedGlbPath;
            if (string.IsNullOrEmpty(assetPath))
            {
                state.status = "error";
                state.errorMessage = "No asset path available for instantiation.";
                McpJobStateStore.SaveState(ToolName, state);
                return new ErrorResponse(state.errorMessage);
            }

            // Convert to Assets-relative path if needed
            assetPath = ToAssetsRelativePath(assetPath);
            TrackImportedAsset(state, assetPath, "prepare_instantiate");
            
            bool isPlayMode = EditorApplication.isPlaying;
            bool isGlbFile = assetPath.EndsWith(".glb", StringComparison.OrdinalIgnoreCase) || 
                             assetPath.EndsWith(".gltf", StringComparison.OrdinalIgnoreCase);
            bool canUseGltfFast = isPlayMode && isGlbFile && IsGltfFastAvailable();
            state.usedGltfFast = canUseGltfFast;

            // In Play Mode with GLB files, use async glTFast loading
            if (canUseGltfFast)
            {
                GameObject container = LoadGlbWithGltfFast(assetPath);
                if (container != null)
                {
                    // Store container ID and set status to loading_glb
                    state.status = "loading_glb";
                    state.gltfLoadingContainerId = container.GetInstanceID().ToString();
                    AppendStateLog(state, $"Started glTFast loading for '{assetPath}'.");
                    McpJobStateStore.SaveState(ToolName, state);
                    
                    return new PendingResponse(
                        $"Loading GLB model for '{state.targetPrompt}' via glTFast...",
                        DefaultPollIntervalSeconds,
                        BuildStatusData(state)
                    );
                }
                else
                {
                    MarkStateError(state, $"Failed to start glTFast loading for '{assetPath}'.");
                    McpJobStateStore.SaveState(ToolName, state);
                    return new ErrorResponse(state.errorMessage);
                }
            }

            // Non-glTFast path (Edit Mode or non-GLB files)
            GameObject newObject = null;
            bool wasAlreadyInstantiated = false;

            // Check if Trellis already instantiated this object (for generated assets)
            if (!string.IsNullOrEmpty(state.generatedGlbPath))
            {
                newObject = FindTrellisInstantiatedObject(state.generatedGlbPath);
                if (newObject != null)
                {
                    wasAlreadyInstantiated = true;
                    Debug.Log($"[Manage3DGen] Reusing Trellis-instantiated object instead of creating duplicate");
                    AppendStateLog(state, "Reused object that Trellis already instantiated in-scene.");
                }
            }

            // If not found, load and instantiate
            if (newObject == null)
            {
                GameObject prefab = null;

                // Use the Play Mode compatible loader
                prefab = LoadAssetPlayModeCompatible(assetPath);

                if (prefab == null)
                {
                    // In Play Mode with a newly generated GLB, the asset might not be loadable
                    // but Trellis should have already instantiated it - search more broadly
                    if (isPlayMode && !string.IsNullOrEmpty(state.generatedGlbPath))
                    {
                        string baseName = Path.GetFileNameWithoutExtension(state.generatedGlbPath);
                        var allObjects = UnityEngine.Object.FindObjectsByType<GameObject>(
                            FindObjectsInactive.Include, FindObjectsSortMode.None);
                        
                        foreach (var obj in allObjects)
                        {
                            if (obj.name.Contains(baseName) || obj.name.Contains(state.targetPrompt))
                            {
                                newObject = obj;
                                Debug.Log($"[Manage3DGen] Found object by name search in Play Mode: {obj.name}");
                                break;
                            }
                        }
                        
                        if (newObject != null)
                        {
                            // Skip the rest of loading, we found it
                            goto FoundObject;
                        }
                    }
                    
                    state.status = "error";
                    state.errorMessage = $"Failed to load asset at '{assetPath}'.";
                    AppendStateLog(state, $"ERROR: {state.errorMessage}");
                    McpJobStateStore.SaveState(ToolName, state);
                    return new ErrorResponse(state.errorMessage);
                }

                // Instantiate the new object
                if (isPlayMode)
                {
                    // In Play Mode, use regular Instantiate
                    newObject = UnityEngine.Object.Instantiate(prefab);
                }
                else
                {
                    // In Edit Mode, prefer PrefabUtility for prefab link preservation
                    newObject = PrefabUtility.InstantiatePrefab(prefab) as GameObject;
                    if (newObject == null)
                    {
                        newObject = UnityEngine.Object.Instantiate(prefab);
                    }
                }

                if (newObject == null)
                {
                    MarkStateError(state, $"Failed to instantiate asset '{assetPath}'.");
                    McpJobStateStore.SaveState(ToolName, state);
                    return new ErrorResponse(state.errorMessage);
                }

                // Only register Undo in Edit Mode
                if (!isPlayMode)
                {
                    Undo.RegisterCreatedObjectUndo(newObject, $"Generate {state.targetPrompt}");
                }
            }

            FoundObject:

            // Name the new object
            newObject.name = state.targetPrompt;

            // Apply transform - position and rotation from parameters
            Vector3 position = new Vector3(state.originalPosition[0], state.originalPosition[1], state.originalPosition[2]);
            Vector3 rotation = new Vector3(state.originalRotation[0], state.originalRotation[1], state.originalRotation[2]);
            Vector3 scale = new Vector3(state.originalScale[0], state.originalScale[1], state.originalScale[2]);

            newObject.transform.position = position;
            newObject.transform.rotation = Quaternion.Euler(rotation);
            newObject.transform.localScale = scale;

            // Set parent if specified
            if (!string.IsNullOrEmpty(state.originalParentPath))
            {
                GameObject parent = FindSceneObject(state.originalParentPath);
                if (parent != null)
                {
                    newObject.transform.SetParent(parent.transform);
                }
            }

            // Mark scene dirty (only in Edit Mode)
            if (!isPlayMode)
            {
                EditorUtility.SetDirty(newObject);
                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                    UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());
            }

            // Update state
            state.status = "completed";
            AppendStateLog(state, $"Instantiated object '{newObject.name}' from '{assetPath}' (reused={wasAlreadyInstantiated}).");
            McpJobStateStore.SaveState(ToolName, state);

            Selection.activeGameObject = newObject;

            RememberPromptAsset(state.targetPrompt, assetPath);

            return new SuccessResponse(
                $"Successfully generated '{state.targetPrompt}'" + (isPlayMode ? " (Play Mode)" : ""),
                new
                {
                    newObjectName = newObject.name,
                    newObjectId = newObject.GetInstanceID(),
                    assetUsed = assetPath,
                    wasGenerated = !string.IsNullOrEmpty(state.generatedGlbPath),
                    playMode = isPlayMode,
                    position = new { x = position.x, y = position.y, z = position.z },
                    rotation = new { x = rotation.x, y = rotation.y, z = rotation.z },
                    scale = new { x = scale.x, y = scale.y, z = scale.z },
                    trellisImport = BuildStatusData(state)
                }
            );
        }

        /// <summary>
        /// Completes the transform by instantiating the asset and replacing the source.
        /// If Trellis already instantiated the object, we reuse it instead of creating a duplicate.
        /// Works in both Edit Mode and Play Mode.
        /// </summary>
        private static object CompleteTransform(GenerationJobState state, GameObject sourceGo)
        {
            string assetPath = state.foundAssetPath ?? state.generatedGlbPath;
            if (string.IsNullOrEmpty(assetPath))
            {
                state.status = "error";
                state.errorMessage = "No asset path available for instantiation.";
                McpJobStateStore.SaveState(ToolName, state);
                return new ErrorResponse(state.errorMessage);
            }

            // Convert to Assets-relative path if needed
            assetPath = ToAssetsRelativePath(assetPath);
            TrackImportedAsset(state, assetPath, "prepare_transform");
            
            bool isPlayMode = EditorApplication.isPlaying;
            bool isGlbFile = assetPath.EndsWith(".glb", StringComparison.OrdinalIgnoreCase) || 
                             assetPath.EndsWith(".gltf", StringComparison.OrdinalIgnoreCase);
            bool canUseGltfFast = isPlayMode && isGlbFile && IsGltfFastAvailable();
            state.usedGltfFast = canUseGltfFast;

            // In Play Mode with GLB files, use async glTFast loading
            // Note: For transform, we still need the source object, so we handle this differently
            if (canUseGltfFast)
            {
                GameObject container = LoadGlbWithGltfFast(assetPath);
                if (container != null)
                {
                    // Store container ID and set status to loading_glb
                    state.status = "loading_glb";
                    state.gltfLoadingContainerId = container.GetInstanceID().ToString();
                    AppendStateLog(state, $"Started glTFast loading for transform asset '{assetPath}'.");
                    McpJobStateStore.SaveState(ToolName, state);
                    
                    return new PendingResponse(
                        $"Loading GLB model for transform to '{state.targetPrompt}' via glTFast...",
                        DefaultPollIntervalSeconds,
                        BuildStatusData(state)
                    );
                }
                else
                {
                    MarkStateError(state, $"Failed to start glTFast loading for '{assetPath}'.");
                    McpJobStateStore.SaveState(ToolName, state);
                    return new ErrorResponse(state.errorMessage);
                }
            }

            GameObject newObject = null;
            bool wasAlreadyInstantiated = false;

            // Check if Trellis already instantiated this object (for generated assets)
            if (!string.IsNullOrEmpty(state.generatedGlbPath))
            {
                newObject = FindTrellisInstantiatedObject(state.generatedGlbPath);
                if (newObject != null)
                {
                    wasAlreadyInstantiated = true;
                    Debug.Log($"[Manage3DGen] Reusing Trellis-instantiated object for transform");
                    AppendStateLog(state, "Reused object that Trellis already instantiated in-scene for transform.");
                }
            }

            // If not found, load and instantiate
            if (newObject == null)
            {
                GameObject prefab = null;

                // Use the Play Mode compatible loader
                prefab = LoadAssetPlayModeCompatible(assetPath);

                if (prefab == null)
                {
                    // In Play Mode with a newly generated GLB, the asset might not be loadable
                    // but Trellis should have already instantiated it - search more broadly
                    if (isPlayMode && !string.IsNullOrEmpty(state.generatedGlbPath))
                    {
                        string baseName = Path.GetFileNameWithoutExtension(state.generatedGlbPath);
                        var allObjects = UnityEngine.Object.FindObjectsByType<GameObject>(
                            FindObjectsInactive.Include, FindObjectsSortMode.None);
                        
                        foreach (var obj in allObjects)
                        {
                            if (obj.name.Contains(baseName) || obj.name.Contains(state.targetPrompt))
                            {
                                newObject = obj;
                                Debug.Log($"[Manage3DGen] Found object by name search in Play Mode: {obj.name}");
                                break;
                            }
                        }
                        
                        if (newObject != null)
                        {
                            // Skip the rest of loading, we found it
                            goto FoundTransformObject;
                        }
                    }
                    
                    state.status = "error";
                    state.errorMessage = $"Failed to load asset at '{assetPath}'. In Play Mode, Trellis-generated assets may not be loadable immediately.";
                    AppendStateLog(state, $"ERROR: {state.errorMessage}");
                    McpJobStateStore.SaveState(ToolName, state);
                    return new ErrorResponse(state.errorMessage);
                }

                // Instantiate the new object
                if (isPlayMode)
                {
                    newObject = UnityEngine.Object.Instantiate(prefab);
                }
                else
                {
                    newObject = PrefabUtility.InstantiatePrefab(prefab) as GameObject;
                    if (newObject == null)
                    {
                        newObject = UnityEngine.Object.Instantiate(prefab);
                    }
                }

                if (newObject == null)
                {
                    MarkStateError(state, $"Failed to instantiate asset '{assetPath}'.");
                    McpJobStateStore.SaveState(ToolName, state);
                    return new ErrorResponse(state.errorMessage);
                }

                if (!isPlayMode)
                {
                    Undo.RegisterCreatedObjectUndo(newObject, $"Transform {sourceGo.name} to {state.targetPrompt}");
                }
            }

            FoundTransformObject:

            // Name the new object
            newObject.name = state.targetPrompt;

            // Apply transform - position and rotation
            Vector3 position = new Vector3(state.originalPosition[0], state.originalPosition[1], state.originalPosition[2]);
            Vector3 rotation = new Vector3(state.originalRotation[0], state.originalRotation[1], state.originalRotation[2]);
            newObject.transform.position = position;
            newObject.transform.rotation = Quaternion.Euler(rotation);

            Vector3 originalScale = new Vector3(state.originalScale[0], state.originalScale[1], state.originalScale[2]);
            Vector3 prefabScaleSnapshot = newObject.transform.localScale;
            newObject.transform.localScale = Vector3.one;

            var newBounds = GetObjectBounds(newObject);
            Vector3 originalBoundsSize = new Vector3(state.originalBoundsSize[0], state.originalBoundsSize[1], state.originalBoundsSize[2]);

            Vector3 appliedScale = CalculateReplacementScale(prefabScaleSnapshot, newBounds.size, originalBoundsSize, originalScale);
            newObject.transform.localScale = appliedScale;

            Debug.Log($"[Manage3DGen] Applied scale {appliedScale} to match bounds (original: {originalBoundsSize}, new: {newBounds.size})");

            // Set parent
            if (!string.IsNullOrEmpty(state.originalParentPath))
            {
                GameObject parent = GameObject.Find(state.originalParentPath);
                if (parent != null)
                {
                    newObject.transform.SetParent(parent.transform);
                    newObject.transform.SetSiblingIndex(state.originalSiblingIndex);
                }
            }

            // Add history component and record the transform
            var history = newObject.GetComponent<ObjectTransformHistory>();
            if (history == null)
            {
                history = newObject.AddComponent<ObjectTransformHistory>();
            }

            // Check if source has history (chained transform)
            var sourceHistory = sourceGo.GetComponent<ObjectTransformHistory>();
            if (sourceHistory != null)
            {
                history.CopyHistoryFrom(sourceHistory);
            }

            // Get source asset path if it's a prefab (only works in Edit Mode)
            string sourceAssetPath = isPlayMode ? null : PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(sourceGo);

            history.RecordTransform(
                sourceObject: sourceGo,
                targetPrompt: state.targetPrompt,
                replacementAssetPath: assetPath,
                wasGenerated: !string.IsNullOrEmpty(state.generatedGlbPath),
                originalPosition: position,
                originalRotation: Quaternion.Euler(rotation),
                originalScale: originalScale,
                originalBoundsSize: originalBoundsSize,
                sourceAssetPath: sourceAssetPath
            );

            // Disable the source object
            if (!isPlayMode)
            {
                Undo.RecordObject(sourceGo, $"Disable {sourceGo.name}");
            }
            sourceGo.SetActive(false);

            // Mark scene dirty (only in Edit Mode)
            if (!isPlayMode)
            {
                EditorUtility.SetDirty(newObject);
                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                    UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());
            }

            // Update state
            state.status = "completed";
            AppendStateLog(state, $"Transformed '{state.sourceObjectName}' to '{state.targetPrompt}' using '{assetPath}' (reused={wasAlreadyInstantiated}).");
            McpJobStateStore.SaveState(ToolName, state);

            Selection.activeGameObject = newObject;

            RememberPromptAsset(state.targetPrompt, assetPath);

            return new SuccessResponse(
                $"Successfully transformed '{state.sourceObjectName}' to '{state.targetPrompt}'" + (isPlayMode ? " (Play Mode)" : ""),
                new
                {
                    newObjectName = newObject.name,
                    newObjectId = newObject.GetInstanceID(),
                    assetUsed = assetPath,
                    wasGenerated = !string.IsNullOrEmpty(state.generatedGlbPath),
                    playMode = isPlayMode,
                    disabledOriginal = state.sourceObjectName,
                    appliedScale = new { x = newObject.transform.localScale.x, y = newObject.transform.localScale.y, z = newObject.transform.localScale.z },
                    trellisImport = BuildStatusData(state)
                }
            );
        }

        /// <summary>
        /// Reverts an object to its previous or original state.
        /// </summary>
        private static object RevertObject(JObject @params, bool revertToOriginal)
        {
            string target = GetParam(@params, "target")?.ToString();
            if (string.IsNullOrEmpty(target))
                return new ErrorResponse("'target' parameter is required for revert.");

            GameObject targetGo = FindSceneObject(target);
            if (targetGo == null)
                return new ErrorResponse($"Target object '{target}' not found in scene.");

            var history = targetGo.GetComponent<ObjectTransformHistory>();
            if (history == null || history.History.Count == 0)
                return new ErrorResponse($"Object '{target}' has no transform history to revert.");

            GameObject revertedObject;
            if (revertToOriginal)
            {
                revertedObject = history.RevertToOriginal();
            }
            else
            {
                revertedObject = history.RevertToPrevious();
            }

            if (revertedObject == null)
                return new ErrorResponse("Failed to revert - source object reference is missing.");

            Selection.activeGameObject = revertedObject;

            return new SuccessResponse(
                $"Reverted to '{revertedObject.name}'",
                new
                {
                    revertedObjectName = revertedObject.name,
                    revertedObjectId = revertedObject.GetInstanceID()
                }
            );
        }

        /// <summary>
        /// Lists all objects in scene with transform history.
        /// </summary>
        private static object ListTransformHistory()
        {
            var allHistories = UnityEngine.Object.FindObjectsByType<ObjectTransformHistory>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);

            var results = new List<object>();
            foreach (var history in allHistories)
            {
                results.Add(new
                {
                    objectName = history.gameObject.name,
                    objectId = history.gameObject.GetInstanceID(),
                    isActive = history.gameObject.activeInHierarchy,
                    historyCount = history.History.Count,
                    latestTransform = history.LatestEntry != null ? new
                    {
                        from = history.LatestEntry.sourceObjectName,
                        to = history.LatestEntry.targetPrompt,
                        timestamp = history.LatestEntry.timestamp,
                        wasGenerated = history.LatestEntry.wasGenerated
                    } : null
                });
            }

            return new SuccessResponse(
                $"Found {results.Count} object(s) with transform history.",
                new { objects = results }
            );
        }

        #region Helper Methods

        /// <summary>
        /// Searches for an existing asset by prompt, factoring in caching, recency, and token similarity.
        /// </summary>
        private static string SearchForAsset(string targetName, string promptKey)
        {
            if (string.IsNullOrWhiteSpace(targetName))
                return null;

            if (!string.IsNullOrEmpty(promptKey) && TryGetCachedAsset(promptKey, out var cachedPath))
            {
                Debug.Log($"[Manage3DGen] Using cached asset '{cachedPath}' for prompt '{promptKey}'");
                return cachedPath;
            }

            var promptTokens = TokenizePrompt(targetName);
            var candidates = new List<AssetCandidate>();
            var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            void AddCandidates(IEnumerable<string> guids, bool forceFolderBoost = false)
            {
                int processed = 0;
                foreach (var guid in guids)
                {
                    if (processed++ > 200)
                        break;

                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    if (string.IsNullOrEmpty(path))
                        continue;
                    if (!seenPaths.Add(path))
                        continue;

                    string fileName = Path.GetFileNameWithoutExtension(path);
                    if (string.IsNullOrEmpty(fileName))
                        continue;

                    DateTime? timestamp = TryGetAssetTimestampUtc(path);
                    float score = ScoreAssetCandidate(path, fileName, targetName, promptKey, promptTokens, timestamp, forceFolderBoost);
                    if (score <= 0f)
                        continue;

                    candidates.Add(new AssetCandidate
                    {
                        path = path,
                        score = score,
                        timestampUtc = timestamp
                    });
                }
            }

            foreach (var filter in new[] { "t:Prefab", "t:Model" })
            {
                AddCandidates(AssetDatabase.FindAssets($"{filter} {targetName}"));
            }

            string trellisFolder = "Assets/TrellisResults";
            if (AssetDatabase.IsValidFolder(trellisFolder))
            {
                AddCandidates(AssetDatabase.FindAssets("t:Model", new[] { trellisFolder }), forceFolderBoost: true);
            }

            if (candidates.Count == 0)
            {
                Debug.Log($"[Manage3DGen] No existing asset found for '{targetName}'");
                return null;
            }

            var best = candidates
                .OrderByDescending(c => c.score)
                .ThenByDescending(c => c.timestampUtc ?? DateTime.MinValue)
                .ThenBy(c => c.path.Length)
                .First();

            Debug.Log($"[Manage3DGen] Selected asset '{best.path}' (score {best.score:F1}) for '{targetName}'");
            return best.path;
        }

        private static bool TryGetCachedAsset(string promptKey, out string assetPath)
        {
            assetPath = null;
            if (string.IsNullOrEmpty(promptKey))
                return false;

            if (s_promptAssetCache.TryGetValue(promptKey, out var record))
            {
                if (!string.IsNullOrEmpty(record.assetPath) && AssetFileExists(record.assetPath))
                {
                    record.lastUsedUtc = DateTime.UtcNow;
                    assetPath = record.assetPath;
                    return true;
                }

                s_promptAssetCache.Remove(promptKey);
            }

            return false;
        }

        private static void RememberPromptAsset(string prompt, string assetPath)
        {
            string promptKey = NormalizePromptKey(prompt);
            if (string.IsNullOrEmpty(promptKey))
                return;

            string normalizedPath = ToAssetsRelativePath(assetPath);
            if (string.IsNullOrEmpty(normalizedPath))
                return;

            long fileSize = TryGetAssetFileSize(normalizedPath) ?? 0;

            s_promptAssetCache[promptKey] = new PromptAssetRecord
            {
                assetPath = normalizedPath,
                lastUsedUtc = DateTime.UtcNow,
                fileSize = fileSize
            };

            if (s_promptAssetCache.Count > MaxPromptAssetCacheEntries)
            {
                TrimPromptCache();
            }
        }

        private static void TrimPromptCache()
        {
            while (s_promptAssetCache.Count > MaxPromptAssetCacheEntries)
            {
                var oldest = s_promptAssetCache.OrderBy(kvp => kvp.Value.lastUsedUtc).FirstOrDefault();
                if (oldest.Key == null)
                    break;
                s_promptAssetCache.Remove(oldest.Key);
            }
        }

        private static IEnumerable<string> TokenizePrompt(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return Array.Empty<string>();

            string lower = text.ToLowerInvariant();
            string sanitized = Regex.Replace(lower, "[^a-z0-9]+", " ");
            return sanitized.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        }

        private static string NormalizePromptKey(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            var tokens = TokenizePrompt(text);
            return string.Join("_", tokens);
        }

        private static float ScoreAssetCandidate(
            string assetPath,
            string fileName,
            string targetName,
            string promptKey,
            IEnumerable<string> promptTokens,
            DateTime? timestampUtc,
            bool folderBoost)
        {
            float score = 0f;
            string normalizedName = NormalizePromptKey(fileName);

            if (string.Equals(fileName, targetName, StringComparison.OrdinalIgnoreCase))
                score += 80f;
            if (!string.IsNullOrEmpty(promptKey))
            {
                if (normalizedName == promptKey)
                    score += 60f;
                else if (normalizedName.StartsWith(promptKey, StringComparison.OrdinalIgnoreCase))
                    score += 25f;
            }

            if (fileName.StartsWith(targetName, StringComparison.OrdinalIgnoreCase))
                score += 25f;
            if (fileName.IndexOf(targetName, StringComparison.OrdinalIgnoreCase) >= 0)
                score += 10f;

            foreach (var token in promptTokens)
            {
                if (fileName.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0)
                    score += 6f;
                if (!string.IsNullOrEmpty(normalizedName) && normalizedName.Contains(token))
                    score += 2f;
            }

            if (assetPath.IndexOf("TrellisResults", StringComparison.OrdinalIgnoreCase) >= 0)
                score += 30f;
            else if (folderBoost)
                score += 10f;

            if (timestampUtc.HasValue)
            {
                double minutes = Math.Max(0, (DateTime.UtcNow - timestampUtc.Value).TotalMinutes);
                if (minutes <= 60)
                {
                    score += (float)(60 - minutes) * 0.3f;
                }
            }

            return score;
        }

        private static DateTime? TryGetAssetTimestampUtc(string assetPath)
        {
            try
            {
                string absolutePath = GetAbsolutePathForAsset(assetPath);
                if (string.IsNullOrEmpty(absolutePath) || !File.Exists(absolutePath))
                    return null;
                return File.GetLastWriteTimeUtc(absolutePath);
            }
            catch
            {
                return null;
            }
        }

        private static long? TryGetAssetFileSize(string assetPath)
        {
            try
            {
                string absolutePath = GetAbsolutePathForAsset(assetPath);
                if (string.IsNullOrEmpty(absolutePath) || !File.Exists(absolutePath))
                    return null;
                var info = new FileInfo(absolutePath);
                return info.Length;
            }
            catch
            {
                return null;
            }
        }

        private static bool AssetFileExists(string assetPath)
        {
            string absolutePath = GetAbsolutePathForAsset(assetPath);
            return !string.IsNullOrEmpty(absolutePath) && File.Exists(absolutePath);
        }

        private static string GetAbsolutePathForAsset(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath))
                return null;

            if (assetPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
            {
                string relative = assetPath.Substring("Assets/".Length);
                return Path.Combine(Application.dataPath, relative).Replace('\\', '/');
            }

            return assetPath;
        }

        private static Vector3 CalculateReplacementScale(
            Vector3 prefabScale,
            Vector3 newBoundsSize,
            Vector3 originalBoundsSize,
            Vector3 originalScaleSnapshot)
        {
            const float epsilon = 0.0001f;
            bool hasNewBounds = newBoundsSize.x > epsilon && newBoundsSize.y > epsilon && newBoundsSize.z > epsilon;
            bool hasOriginalBounds = originalBoundsSize.x > epsilon && originalBoundsSize.y > epsilon && originalBoundsSize.z > epsilon;

            if (!hasNewBounds || !hasOriginalBounds)
            {
                return originalScaleSnapshot.magnitude > epsilon ? originalScaleSnapshot : prefabScale;
            }

            float ratioX = SafeRatio(originalBoundsSize.x, newBoundsSize.x, 1f);
            float ratioY = SafeRatio(originalBoundsSize.y, newBoundsSize.y, 1f);
            float ratioZ = SafeRatio(originalBoundsSize.z, newBoundsSize.z, 1f);

            float medianRatio = MedianOf(ratioX, ratioY, ratioZ);
            float volumeRatio = SafeRatio(
                originalBoundsSize.x * originalBoundsSize.y * originalBoundsSize.z,
                newBoundsSize.x * newBoundsSize.y * newBoundsSize.z,
                1f);
            float volumeScale = volumeRatio > epsilon ? Mathf.Pow(volumeRatio, 1f / 3f) : medianRatio;

            float uniformScale = Mathf.Clamp(Mathf.Lerp(medianRatio, volumeScale, 0.35f), 0.05f, 20f);

            Vector3 shapeWeights = ShouldApplyShape(originalScaleSnapshot)
                ? NormalizeScaleShape(originalScaleSnapshot)
                : Vector3.one;

            Vector3 combined = Vector3.Scale(Vector3.one * uniformScale, shapeWeights);
            return Vector3.Scale(prefabScale, combined);
        }

        private static float SafeRatio(float numerator, float denominator, float fallback)
        {
            return Mathf.Abs(denominator) < 1e-4f ? fallback : numerator / denominator;
        }

        private static float MedianOf(float a, float b, float c)
        {
            float[] values = { a, b, c };
            Array.Sort(values);
            return values[1];
        }

        private static bool ShouldApplyShape(Vector3 scale)
        {
            float avg = (Mathf.Abs(scale.x) + Mathf.Abs(scale.y) + Mathf.Abs(scale.z)) / 3f;
            if (avg < 1e-4f)
                return false;

            float maxDeviation = Mathf.Max(Mathf.Abs(scale.x - avg), Mathf.Abs(scale.y - avg), Mathf.Abs(scale.z - avg));
            return maxDeviation / avg > 0.2f;
        }

        private static Vector3 NormalizeScaleShape(Vector3 scale)
        {
            float avg = (Mathf.Abs(scale.x) + Mathf.Abs(scale.y) + Mathf.Abs(scale.z)) / 3f;
            if (avg < 1e-4f)
                return Vector3.one;

            Vector3 normalized = new Vector3(scale.x / avg, scale.y / avg, scale.z / avg);
            return new Vector3(
                Mathf.Clamp(normalized.x, 0.25f, 4f),
                Mathf.Clamp(normalized.y, 0.25f, 4f),
                Mathf.Clamp(normalized.z, 0.25f, 4f));
        }

        /// <summary>
        /// Starts Trellis model generation.
        /// Works in both Edit Mode and Play Mode.
        /// </summary>
        private static void StartTrellisGeneration(string prompt, GenerationJobState state)
        {
            // Use delayCall to ensure we're on the main editor thread
            // This helps with Play Mode compatibility
            AppendStateLog(state, $"Queueing Trellis request for prompt '{prompt}'.");
            EditorApplication.delayCall += () =>
            {
                try
                {
                    var client = TrellisServiceHost.EnsureClient();
                    s_waitingForTrellis = true;
                    s_pendingGlbPath = null;
                    AppendStateLog(state, "Connected to Trellis service host.");

                    // Subscribe to the GLB ready event
                    client.AddGlbReadyListener(OnTrellisGlbReady);
                    AppendStateLog(state, "Registered Trellis GLB-ready callback.");

                    // Start generation
                    client.SubmitPrompt(prompt);

                    Debug.Log($"[Manage3DGen] Started Trellis generation for '{prompt}'");
                    AppendStateLog(state, "Submitted prompt to Trellis.");
                    McpJobStateStore.SaveState(ToolName, state);
                }
                catch (Exception e)
                {
                    Debug.LogError($"[Manage3DGen] Failed to start Trellis generation: {e.Message}");
                    MarkStateError(state, $"Failed to start Trellis: {e.Message}");
                    McpJobStateStore.SaveState(ToolName, state);
                }
            };
        }

        /// <summary>
        /// Callback when Trellis finishes generating a GLB.
        /// </summary>
        private static void OnTrellisGlbReady(string remoteUrl, string localPath)
        {
            Debug.Log($"[Manage3DGen] Trellis GLB ready: {localPath}");
            AppendPersistedStateLog($"Trellis GLB ready. remoteUrl='{remoteUrl}', localPath='{localPath}'.");

            // Remove listener
            try
            {
                var client = TrellisServiceHost.EnsureClient();
                client.RemoveGlbReadyListener(OnTrellisGlbReady);
                AppendPersistedStateLog("Removed Trellis GLB-ready callback.");
            }
            catch { }

            s_pendingGlbPath = localPath;
            s_waitingForTrellis = false;

            // Refresh asset database to make the GLB available
            // Use ImportAsset for more reliable import of new files
            string assetsRelativePath = ToAssetsRelativePath(localPath);
            AppendPersistedStateLog($"Importing generated asset '{assetsRelativePath}' into AssetDatabase.");

            try
            {
                AssetDatabase.ImportAsset(assetsRelativePath, ImportAssetOptions.ForceSynchronousImport);
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

                var state = McpJobStateStore.LoadState<GenerationJobState>(ToolName);
                if (state != null)
                {
                    TrackImportedAsset(state, assetsRelativePath, "asset_database_imported");
                    McpJobStateStore.SaveState(ToolName, state);
                }
            }
            catch (Exception e)
            {
                var state = McpJobStateStore.LoadState<GenerationJobState>(ToolName);
                if (state != null)
                {
                    MarkStateError(state, $"Failed to import generated asset '{assetsRelativePath}': {e.Message}");
                    McpJobStateStore.SaveState(ToolName, state);
                }
                Debug.LogError($"[Manage3DGen] Failed to import generated GLB '{assetsRelativePath}': {e.Message}");
            }
        }

        /// <summary>
        /// Finds a GameObject in the scene by name or path.
        /// </summary>
        private static GameObject FindSceneObject(string nameOrPath)
        {
            // Try direct find (path)
            GameObject go = GameObject.Find(nameOrPath);
            if (go != null) return go;

            // Try by instance ID (accept numeric strings)
            if (int.TryParse(nameOrPath, out var instanceId))
            {
                var allById = UnityEngine.Object.FindObjectsByType<GameObject>(
                    FindObjectsInactive.Include, FindObjectsSortMode.None);
                foreach (var obj in allById)
                {
                    if (obj.GetInstanceID() == instanceId)
                        return obj;
                }
            }

            // Try by name in all scene objects
            var allObjects = UnityEngine.Object.FindObjectsByType<GameObject>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);

            foreach (var obj in allObjects)
            {
                if (obj.name == nameOrPath)
                    return obj;
            }

            // Try partial match
            foreach (var obj in allObjects)
            {
                if (obj.name.IndexOf(nameOrPath, StringComparison.OrdinalIgnoreCase) >= 0)
                    return obj;
            }

            return null;
        }

        /// <summary>
        /// Gets the world-space bounds of an object (from all renderers or colliders).
        /// </summary>
        private static Bounds GetObjectBounds(GameObject go)
        {
            Bounds bounds = new Bounds(go.transform.position, Vector3.zero);
            bool hasBounds = false;

            // Try renderers first
            var renderers = go.GetComponentsInChildren<Renderer>();
            foreach (var renderer in renderers)
            {
                if (!hasBounds)
                {
                    bounds = renderer.bounds;
                    hasBounds = true;
                }
                else
                {
                    bounds.Encapsulate(renderer.bounds);
                }
            }

            // Fallback to colliders
            if (!hasBounds)
            {
                var colliders = go.GetComponentsInChildren<Collider>();
                foreach (var collider in colliders)
                {
                    if (!hasBounds)
                    {
                        bounds = collider.bounds;
                        hasBounds = true;
                    }
                    else
                    {
                        bounds.Encapsulate(collider.bounds);
                    }
                }
            }

            // Fallback to transform position with small default size
            if (!hasBounds || bounds.size.magnitude < 0.001f)
            {
                bounds = new Bounds(go.transform.position, Vector3.one);
            }

            return bounds;
        }

        /// <summary>
        /// Gets the full hierarchy path of a GameObject.
        /// </summary>
        private static string GetGameObjectPath(GameObject go)
        {
            if (go == null) return null;

            string path = go.name;
            Transform current = go.transform.parent;

            while (current != null)
            {
                path = current.name + "/" + path;
                current = current.parent;
            }

            return path;
        }

        /// <summary>
        /// Converts an absolute path to Assets-relative path.
        /// </summary>
        private static string ToAssetsRelativePath(string path)
        {
            if (string.IsNullOrEmpty(path)) return path;

            // Already relative
            if (path.StartsWith("Assets/") || path.StartsWith("Assets\\"))
                return path;

            // Convert absolute to relative
            string dataPath = Application.dataPath;
            if (path.StartsWith(dataPath))
            {
                return "Assets" + path.Substring(dataPath.Length).Replace("\\", "/");
            }

            return path;
        }

        /// <summary>
        /// Loads and instantiates a prefab, compatible with both Edit Mode and Play Mode.
        /// In Edit Mode: Uses AssetDatabase and PrefabUtility
        /// In Play Mode: Uses Resources.Load or direct instantiation
        /// </summary>
        private static GameObject LoadAssetPlayModeCompatible(string assetPath, Vector3 position, Quaternion rotation)
        {
            bool isPlayMode = EditorApplication.isPlaying;

            if (!isPlayMode)
            {
                // Edit Mode: Use standard Editor APIs
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
                if (prefab == null)
                {
                    Debug.LogWarning($"[Manage3DGen] Could not load prefab at: {assetPath}");
                    return null;
                }

                var instance = PrefabUtility.InstantiatePrefab(prefab) as GameObject;
                if (instance != null)
                {
                    instance.transform.position = position;
                    instance.transform.rotation = rotation;
                    Undo.RegisterCreatedObjectUndo(instance, "Generate 3D Object");
                }
                return instance;
            }
            else
            {
                // Play Mode: Use runtime-compatible APIs
                GameObject prefab = null;
                
                // First try: Load from AssetDatabase (works in Play Mode in Editor)
                prefab = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
                
                if (prefab == null)
                {
                    // Second try: Check if it's a Resources path
                    string resourcesPath = assetPath;
                    if (resourcesPath.Contains("/Resources/"))
                    {
                        int idx = resourcesPath.IndexOf("/Resources/") + "/Resources/".Length;
                        resourcesPath = resourcesPath.Substring(idx);
                        resourcesPath = Path.ChangeExtension(resourcesPath, null); // Remove extension
                        prefab = UnityEngine.Resources.Load<GameObject>(resourcesPath);
                    }
                }

                if (prefab == null)
                {
                    Debug.LogWarning($"[Manage3DGen] Could not load prefab at: {assetPath} (Play Mode)");
                    return null;
                }

                // Instantiate using runtime API
                var instance = UnityEngine.Object.Instantiate(prefab, position, rotation);
                if (instance != null)
                {
                    // Clean up "(Clone)" suffix
                    instance.name = prefab.name;
                }
                return instance;
            }
        }

        /// <summary>
        /// Loads a prefab asset, compatible with both Edit Mode and Play Mode.
        /// Returns the prefab/asset without instantiation.
        /// In Play Mode, uses glTFast for runtime GLB/GLTF loading.
        /// </summary>
        private static GameObject LoadAssetPlayModeCompatible(string assetPath)
        {
            bool isPlayMode = EditorApplication.isPlaying;
            bool isGlbFile = assetPath.EndsWith(".glb", StringComparison.OrdinalIgnoreCase) || 
                             assetPath.EndsWith(".gltf", StringComparison.OrdinalIgnoreCase);
            bool canUseGltfFast = isPlayMode && isGlbFile && IsGltfFastAvailable();

            // In Play Mode with GLB files, use glTFast for runtime loading
            if (canUseGltfFast)
            {
                return LoadGlbWithGltfFast(assetPath);
            }

            // Edit Mode: ensure GLB is imported first
            if (isGlbFile)
            {
                AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);
            }

            // AssetDatabase.LoadAssetAtPath works in Play Mode in the Editor
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            
            // If direct load failed, try loading the main asset (for models/GLB)
            if (prefab == null)
            {
                var mainAsset = AssetDatabase.LoadMainAssetAtPath(assetPath);
                if (mainAsset is GameObject go)
                {
                    prefab = go;
                }
                else if (mainAsset != null)
                {
                    Debug.Log($"[Manage3DGen] Main asset at '{assetPath}' is {mainAsset.GetType().Name}, not GameObject");
                    
                    // Try to find a GameObject in the asset's sub-assets
                    var subAssets = AssetDatabase.LoadAllAssetsAtPath(assetPath);
                    foreach (var subAsset in subAssets)
                    {
                        if (subAsset is GameObject subGo)
                        {
                            prefab = subGo;
                            Debug.Log($"[Manage3DGen] Found GameObject sub-asset: {subGo.name}");
                            break;
                        }
                    }
                }
            }
            
            if (prefab == null && isPlayMode)
            {
                // Fallback: Try Resources.Load if it's in a Resources folder
                if (assetPath.Contains("/Resources/"))
                {
                    int idx = assetPath.IndexOf("/Resources/") + "/Resources/".Length;
                    string resourcesPath = assetPath.Substring(idx);
                    resourcesPath = Path.ChangeExtension(resourcesPath, null); // Remove extension
                    prefab = UnityEngine.Resources.Load<GameObject>(resourcesPath);
                }
            }

            if (prefab == null)
            {
                Debug.LogWarning($"[Manage3DGen] Could not load asset at: {assetPath}" + (isPlayMode ? " (Play Mode)" : "") + 
                    $". File exists: {System.IO.File.Exists(assetPath.Replace("Assets/", Application.dataPath + "/"))}");
            }

            return prefab;
        }

        /// <summary>
        /// Loads a GLB/GLTF file at runtime using glTFast.
        /// This works in Play Mode where Unity's import pipeline doesn't run.
        /// Note: This starts async loading. The result is retrieved via polling.
        /// </summary>
        private static GameObject LoadGlbWithGltfFast(string assetPath)
        {
            if (!IsGltfFastAvailable())
            {
                Debug.LogWarning("[Manage3DGen] glTFast is not available; skipping Play Mode GLB load path.");
                return null;
            }

            // Convert Assets-relative path to absolute file path
            string absolutePath = assetPath;
            if (assetPath.StartsWith("Assets/") || assetPath.StartsWith("Assets\\"))
            {
                absolutePath = Path.Combine(Application.dataPath, assetPath.Substring("Assets/".Length));
            }

            if (!File.Exists(absolutePath))
            {
                Debug.LogError($"[Manage3DGen] GLB file not found: {absolutePath}");
                return null;
            }

            // Validate file size - GLB files should be at least a few KB
            var fileInfo = new FileInfo(absolutePath);
            Debug.Log($"[Manage3DGen] GLB file size: {fileInfo.Length} bytes ({fileInfo.Length / 1024f:F1} KB)");
            
            if (fileInfo.Length < 100)
            {
                Debug.LogError($"[Manage3DGen] GLB file is too small ({fileInfo.Length} bytes), likely corrupted or incomplete: {absolutePath}");
                return null;
            }

            // Validate GLB magic number (first 4 bytes should be "glTF" = 0x46546C67)
            try
            {
                using (var fs = new FileStream(absolutePath, FileMode.Open, FileAccess.Read))
                {
                    byte[] magic = new byte[4];
                    fs.Read(magic, 0, 4);
                    uint magicNumber = BitConverter.ToUInt32(magic, 0);
                    
                    if (magicNumber != 0x46546C67) // "glTF" in little-endian
                    {
                        Debug.LogError($"[Manage3DGen] Invalid GLB file - magic number mismatch. Expected 'glTF', got bytes: {magic[0]:X2} {magic[1]:X2} {magic[2]:X2} {magic[3]:X2}");
                        return null;
                    }
                    
                    Debug.Log($"[Manage3DGen] GLB magic number validated: glTF");
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[Manage3DGen] Failed to validate GLB file: {e.Message}");
                return null;
            }

            Debug.Log($"[Manage3DGen] Loading GLB with glTFast: {absolutePath}");

            // Create a parent GameObject to hold the loaded model
            string modelName = Path.GetFileNameWithoutExtension(assetPath);
            GameObject container = new GameObject(modelName + "_glTFast");

            // Use glTFast for runtime loading with file:// prefix
            string fileUri = "file://" + absolutePath.Replace("\\", "/");
            
            // Start async loading - use fire-and-forget pattern with completion callback
            StartGltfLoadAsync(fileUri, container);
            
            // Return the container immediately - it will be populated by the async loader
            // The caller should check if the container has children to know if loading is complete
            return container;
        }

        // Static state for async glTFast loading  
        private static bool s_gltfLoadingInProgress = false;
        private static GameObject s_gltfLoadingContainer = null;
        private static string s_gltfLoadingError = null;
        private static bool? s_hasGltfFast = null;
        private static bool s_warnedMissingGltfFast = false;

        private static bool IsGltfFastAvailable()
        {
            if (s_hasGltfFast.HasValue)
            {
                return s_hasGltfFast.Value;
            }

            var gltfType = Type.GetType("GLTFast.GltfImport, glTFast") ?? Type.GetType("GLTFast.GltfImport");
            s_hasGltfFast = gltfType != null;

            if (!s_hasGltfFast.Value && !s_warnedMissingGltfFast)
            {
                Debug.LogWarning("[Manage3DGen] glTFast package not found. Install com.atteneder.gltfast to enable Play Mode GLB loading; falling back to standard asset loading.");
                s_warnedMissingGltfFast = true;
            }

            return s_hasGltfFast.Value;
        }

        private static async void StartGltfLoadAsync(string uri, GameObject container)
        {
            s_gltfLoadingInProgress = true;
            s_gltfLoadingContainer = container;
            s_gltfLoadingError = null;
            
            if (!IsGltfFastAvailable())
            {
                s_gltfLoadingError = "glTFast package not available for Play Mode GLB loading.";
                Debug.LogError($"[Manage3DGen] {s_gltfLoadingError}");
                s_gltfLoadingInProgress = false;
                return;
            }
            
            try
            {
                // Create importer dynamically to avoid hard dependency when the package is absent
                var gltfImportType = Type.GetType("GLTFast.GltfImport, glTFast") ?? Type.GetType("GLTFast.GltfImport");
                var loggerType = Type.GetType("GLTFast.Logging.ConsoleLogger, glTFast") ?? Type.GetType("GLTFast.Logging.ConsoleLogger");

                if (gltfImportType == null)
                {
                    s_gltfLoadingError = "glTFast types could not be located at runtime.";
                    Debug.LogError($"[Manage3DGen] {s_gltfLoadingError}");
                    return;
                }

                object loggerInstance = null;
                if (loggerType != null)
                {
                    try { loggerInstance = Activator.CreateInstance(loggerType); }
                    catch (Exception loggerEx) { Debug.LogWarning($"[Manage3DGen] Could not create glTFast logger: {loggerEx.Message}"); }
                }

                dynamic gltf = Activator.CreateInstance(gltfImportType);

                // Attempt to attach the logger if supported
                if (loggerInstance != null)
                {
                    try { gltf.Logger = loggerInstance; }
                    catch { try { gltf.logger = loggerInstance; } catch { } }
                }

                Debug.Log($"[Manage3DGen] Starting glTFast.Load for: {uri}");
                bool success = false;
                var loadTaskObj = gltf.Load(uri);
                if (loadTaskObj is Task<bool> boolTask)
                {
                    success = await boolTask;
                }
                else if (loadTaskObj is Task task)
                {
                    await task;
                    success = true;
                }
                
                if (success && container != null)
                {
                    Debug.Log($"[Manage3DGen] glTFast.Load succeeded, instantiating scene...");
                    var instantiateTaskObj = gltf.InstantiateMainSceneAsync(container.transform);
                    if (instantiateTaskObj is Task instantiateTask)
                    {
                        await instantiateTask;
                    }
                    Debug.Log($"[Manage3DGen] glTFast loading completed successfully for: {container.name}, children: {container.transform.childCount}");
                }
                else
                {
                    s_gltfLoadingError = $"glTFast.Load returned false for: {uri}";
                    Debug.LogError($"[Manage3DGen] {s_gltfLoadingError}");
                }
            }
            catch (Exception e)
            {
                s_gltfLoadingError = e.Message;
                Debug.LogError($"[Manage3DGen] glTFast exception: {e.Message}\n{e.StackTrace}");
            }
            finally
            {
                s_gltfLoadingInProgress = false;
                s_gltfLoadingContainer = null;
            }
        }

        /// <summary>
        /// Parses a JToken into a float array for Vector3 representation.
        /// Handles arrays like [x, y, z], strings like "[x, y, z]" or "x, y, z", and objects like {x: 0, y: 0, z: 0}
        /// </summary>
        private static float[] ParseVector3Array(JToken token)
        {
            if (token == null) return null;

            try
            {
                // Handle JArray: [x, y, z]
                if (token is JArray arr && arr.Count >= 3)
                {
                    return new float[]
                    {
                        arr[0].ToObject<float>(),
                        arr[1].ToObject<float>(),
                        arr[2].ToObject<float>()
                    };
                }

                // Handle JObject: {x: 0, y: 0, z: 0}
                if (token is JObject obj)
                {
                    if (obj.ContainsKey("x") && obj.ContainsKey("y") && obj.ContainsKey("z"))
                    {
                        return new float[]
                        {
                            obj["x"].ToObject<float>(),
                            obj["y"].ToObject<float>(),
                            obj["z"].ToObject<float>()
                        };
                    }
                }

                // Handle string: "[x, y, z]" or "x, y, z"
                if (token.Type == JTokenType.String)
                {
                    string str = token.ToString().Trim();
                    
                    // Remove brackets if present
                    if (str.StartsWith("[") && str.EndsWith("]"))
                    {
                        str = str.Substring(1, str.Length - 2);
                    }
                    
                    // Split by comma and parse
                    string[] parts = str.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 3)
                    {
                        return new float[]
                        {
                            float.Parse(parts[0].Trim(), System.Globalization.CultureInfo.InvariantCulture),
                            float.Parse(parts[1].Trim(), System.Globalization.CultureInfo.InvariantCulture),
                            float.Parse(parts[2].Trim(), System.Globalization.CultureInfo.InvariantCulture)
                        };
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Manage3DGen] Failed to parse Vector3 from token '{token}': {e.Message}");
            }

            return null;
        }

        #endregion

        #region Parameters Class for Tool Discovery

        public class Parameters
        {
            [ToolParameter("Action to perform: generate, transform, status, revert, revert_original, list_history", Required = false)]
            public string action { get; set; }

            [ToolParameter("Name or path of the source object to transform (for 'transform' action)")]
            public string source_object { get; set; }

            [ToolParameter("Name/prompt of the 3D model to generate or transform into")]
            public string target_name { get; set; }

            [ToolParameter("World position [x, y, z] for the generated object (for 'generate' action)", Required = false)]
            public float[] position { get; set; }

            [ToolParameter("Euler rotation [x, y, z] for the generated object (for 'generate' action)", Required = false)]
            public float[] rotation { get; set; }

            [ToolParameter("Scale [x, y, z] for the generated object (for 'generate' action)", Required = false)]
            public float[] scale { get; set; }

            [ToolParameter("Parent object name or path (for 'generate' action)", Required = false)]
            public string parent { get; set; }

            [ToolParameter("Whether to search for existing assets first", Required = false)]
            public bool? search_existing { get; set; }

            [ToolParameter("Whether to generate via Trellis if no asset found", Required = false)]
            public bool? generate_if_missing { get; set; }

            [ToolParameter("Target object for revert actions")]
            public string target { get; set; }
        }

        #endregion
    }
}
