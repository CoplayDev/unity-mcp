using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MCPForUnity.Editor.Helpers; // For Response class
using MCPForUnity.Runtime.Helpers; // For ScreenshotUtility
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MCPForUnity.Editor.Tools
{
    /// <summary>
    /// Handles scene management operations like loading, saving, creating, and querying hierarchy.
    /// </summary>
    [McpForUnityTool("manage_scene", AutoRegister = false)]
    public static class ManageScene
    {
        private sealed class SceneCommand
        {
            public string action { get; set; } = string.Empty;
            public string name { get; set; } = string.Empty;
            public string path { get; set; } = string.Empty;
            public int? buildIndex { get; set; }
            public string fileName { get; set; } = string.Empty;
            public int? superSize { get; set; }

            // screenshot: camera selection, inline image, batch, view positioning
            public string camera { get; set; }
            public bool? includeImage { get; set; }
            public int? maxResolution { get; set; }
            public string batch { get; set; }           // "surround" or "orbit" for multi-angle batch capture
            public JToken lookAt { get; set; }          // GO reference or [x,y,z] to aim at before capture
            public Vector3? viewPosition { get; set; }  // camera position for view-based capture
            public Vector3? viewRotation { get; set; }  // euler rotation for view-based capture

            // orbit batch params
            public int? orbitAngles { get; set; }       // number of azimuth samples (default 8)
            public float[] orbitElevations { get; set; } // elevation angles in degrees (default [0, 30, -15])
            public float? orbitDistance { get; set; }    // camera distance from target (default auto from bounds)
            public float? orbitFov { get; set; }         // camera FOV in degrees (default 60)

            // scene_view_frame
            public JToken sceneViewTarget { get; set; }

            // get_hierarchy paging + safety (summary-first)
            public JToken parent { get; set; }
            public int? pageSize { get; set; }
            public int? cursor { get; set; }
            public int? maxNodes { get; set; }
            public int? maxDepth { get; set; }
            public int? maxChildrenPerNode { get; set; }
            public bool? includeTransform { get; set; }
        }

        private static float[] ParseFloatArray(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null) return null;
            if (token.Type == JTokenType.Array)
            {
                var arr = (JArray)token;
                var result = new float[arr.Count];
                for (int i = 0; i < arr.Count; i++)
                    result[i] = arr[i].ToObject<float>();
                return result;
            }
            // Single value → array of one
            var single = ParamCoercion.CoerceFloatNullable(token);
            return single.HasValue ? new[] { single.Value } : null;
        }

        private static SceneCommand ToSceneCommand(JObject p)
        {
            if (p == null) return new SceneCommand();
            return new SceneCommand
            {
                action = (p["action"]?.ToString() ?? string.Empty).Trim().ToLowerInvariant(),
                name = p["name"]?.ToString() ?? string.Empty,
                path = p["path"]?.ToString() ?? string.Empty,
                buildIndex = ParamCoercion.CoerceIntNullable(p["buildIndex"] ?? p["build_index"]),
                fileName = (p["fileName"] ?? p["filename"])?.ToString() ?? string.Empty,
                superSize = ParamCoercion.CoerceIntNullable(p["superSize"] ?? p["super_size"] ?? p["supersize"]),

                // screenshot: camera selection, inline image, batch, view positioning
                camera = (p["camera"])?.ToString(),
                includeImage = ParamCoercion.CoerceBoolNullable(p["includeImage"] ?? p["include_image"]),
                maxResolution = ParamCoercion.CoerceIntNullable(p["maxResolution"] ?? p["max_resolution"]),
                batch = (p["batch"])?.ToString(),
                lookAt = p["lookAt"] ?? p["look_at"],
                viewPosition = VectorParsing.ParseVector3(p["viewPosition"] ?? p["view_position"]),
                viewRotation = VectorParsing.ParseVector3(p["viewRotation"] ?? p["view_rotation"]),

                // orbit batch params
                orbitAngles = ParamCoercion.CoerceIntNullable(p["orbitAngles"] ?? p["orbit_angles"]),
                orbitElevations = ParseFloatArray(p["orbitElevations"] ?? p["orbit_elevations"]),
                orbitDistance = ParamCoercion.CoerceFloatNullable(p["orbitDistance"] ?? p["orbit_distance"]),
                orbitFov = ParamCoercion.CoerceFloatNullable(p["orbitFov"] ?? p["orbit_fov"]),

                // scene_view_frame
                sceneViewTarget = p["sceneViewTarget"] ?? p["scene_view_target"],

                // get_hierarchy paging + safety
                parent = p["parent"],
                pageSize = ParamCoercion.CoerceIntNullable(p["pageSize"] ?? p["page_size"]),
                cursor = ParamCoercion.CoerceIntNullable(p["cursor"]),
                maxNodes = ParamCoercion.CoerceIntNullable(p["maxNodes"] ?? p["max_nodes"]),
                maxDepth = ParamCoercion.CoerceIntNullable(p["maxDepth"] ?? p["max_depth"]),
                maxChildrenPerNode = ParamCoercion.CoerceIntNullable(p["maxChildrenPerNode"] ?? p["max_children_per_node"]),
                includeTransform = ParamCoercion.CoerceBoolNullable(p["includeTransform"] ?? p["include_transform"]),
            };
        }

        /// <summary>
        /// Main handler for scene management actions.
        /// </summary>
        public static object HandleCommand(JObject @params)
        {
            try { McpLog.Info("[ManageScene] HandleCommand: start", always: false); } catch { }
            var cmd = ToSceneCommand(@params);
            string action = cmd.action;
            string name = string.IsNullOrEmpty(cmd.name) ? null : cmd.name;
            string path = string.IsNullOrEmpty(cmd.path) ? null : cmd.path; // Relative to Assets/
            int? buildIndex = cmd.buildIndex;
            // bool loadAdditive = @params["loadAdditive"]?.ToObject<bool>() ?? false; // Example for future extension

            // Ensure path is relative to Assets/, removing any leading "Assets/"
            string relativeDir = path ?? string.Empty;
            if (!string.IsNullOrEmpty(relativeDir))
            {
                relativeDir = AssetPathUtility.NormalizeSeparators(relativeDir).Trim('/');
                if (relativeDir.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
                {
                    relativeDir = relativeDir.Substring("Assets/".Length).TrimStart('/');
                }
            }

            // Apply default *after* sanitizing, using the original path variable for the check
            if (string.IsNullOrEmpty(path) && action == "create") // Check original path for emptiness
            {
                relativeDir = "Scenes"; // Default relative directory
            }

            if (string.IsNullOrEmpty(action))
            {
                return new ErrorResponse("Action parameter is required.");
            }

            string sceneFileName = string.IsNullOrEmpty(name) ? null : $"{name}.unity";
            // Construct full system path correctly: ProjectRoot/Assets/relativeDir/sceneFileName
            string fullPathDir = Path.Combine(Application.dataPath, relativeDir); // Combine with Assets path (Application.dataPath ends in Assets)
            string fullPath = string.IsNullOrEmpty(sceneFileName)
                ? null
                : Path.Combine(fullPathDir, sceneFileName);
            // Ensure relativePath always starts with "Assets/" and uses forward slashes
            string relativePath = string.IsNullOrEmpty(sceneFileName)
                ? null
                : AssetPathUtility.NormalizeSeparators(Path.Combine("Assets", relativeDir, sceneFileName));

            // Ensure directory exists for 'create'
            if (action == "create" && !string.IsNullOrEmpty(fullPathDir))
            {
                try
                {
                    Directory.CreateDirectory(fullPathDir);
                }
                catch (Exception e)
                {
                    return new ErrorResponse(
                        $"Could not create directory '{fullPathDir}': {e.Message}"
                    );
                }
            }

            // Route action
            try { McpLog.Info($"[ManageScene] Route action='{action}' name='{name}' path='{path}' buildIndex={(buildIndex.HasValue ? buildIndex.Value.ToString() : "null")}", always: false); } catch { }
            switch (action)
            {
                case "create":
                    if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(relativePath))
                        return new ErrorResponse(
                            "'name' and 'path' parameters are required for 'create' action."
                        );
                    return CreateScene(fullPath, relativePath);
                case "load":
                    // Loading can be done by path/name or build index
                    if (!string.IsNullOrEmpty(relativePath))
                        return LoadScene(relativePath);
                    else if (buildIndex.HasValue)
                        return LoadScene(buildIndex.Value);
                    else
                        return new ErrorResponse(
                            "Either 'name'/'path' or 'buildIndex' must be provided for 'load' action."
                        );
                case "save":
                    // Save current scene, optionally to a new path
                    return SaveScene(fullPath, relativePath);
                case "get_hierarchy":
                    try { McpLog.Info("[ManageScene] get_hierarchy: entering", always: false); } catch { }
                    var gh = GetSceneHierarchyPaged(cmd);
                    try { McpLog.Info("[ManageScene] get_hierarchy: exiting", always: false); } catch { }
                    return gh;
                case "get_active":
                    try { McpLog.Info("[ManageScene] get_active: entering", always: false); } catch { }
                    var ga = GetActiveSceneInfo();
                    try { McpLog.Info("[ManageScene] get_active: exiting", always: false); } catch { }
                    return ga;
                case "get_build_settings":
                    return GetBuildSettingsScenes();
                case "screenshot":
                    return CaptureScreenshot(cmd);
                case "scene_view_frame":
                    return FrameSceneView(cmd);
                default:
                    return new ErrorResponse(
                        $"Unknown action: '{action}'. Valid actions: create, load, save, get_hierarchy, get_active, get_build_settings, screenshot, scene_view_frame."
                    );
            }
        }

        /// <summary>
        /// Captures a screenshot to Assets/Screenshots and returns a response payload.
        /// Public so the tools UI can reuse the same logic without duplicating parameters.
        /// Available in both Edit Mode and Play Mode.
        /// </summary>
        public static object ExecuteScreenshot(string fileName = null, int? superSize = null)
        {
            var cmd = new SceneCommand { fileName = fileName ?? string.Empty, superSize = superSize };
            return CaptureScreenshot(cmd);
        }

        private static object CreateScene(string fullPath, string relativePath)
        {
            if (File.Exists(fullPath))
            {
                return new ErrorResponse($"Scene already exists at '{relativePath}'.");
            }

            try
            {
                // Create a new empty scene
                Scene newScene = EditorSceneManager.NewScene(
                    NewSceneSetup.EmptyScene,
                    NewSceneMode.Single
                );
                // Save it to the specified path
                bool saved = EditorSceneManager.SaveScene(newScene, relativePath);

                if (saved)
                {
                    AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport); // Ensure Unity sees the new scene file
                    return new SuccessResponse(
                        $"Scene '{Path.GetFileName(relativePath)}' created successfully at '{relativePath}'.",
                        new { path = relativePath }
                    );
                }
                else
                {
                    // If SaveScene fails, it might leave an untitled scene open.
                    // Optionally try to close it, but be cautious.
                    return new ErrorResponse($"Failed to save new scene to '{relativePath}'.");
                }
            }
            catch (Exception e)
            {
                return new ErrorResponse($"Error creating scene '{relativePath}': {e.Message}");
            }
        }

        private static object LoadScene(string relativePath)
        {
            if (
                !File.Exists(
                    Path.Combine(
                        Application.dataPath.Substring(
                            0,
                            Application.dataPath.Length - "Assets".Length
                        ),
                        relativePath
                    )
                )
            )
            {
                return new ErrorResponse($"Scene file not found at '{relativePath}'.");
            }

            // Check for unsaved changes in the current scene
            if (EditorSceneManager.GetActiveScene().isDirty)
            {
                // Optionally prompt the user or save automatically before loading
                return new ErrorResponse(
                    "Current scene has unsaved changes. Please save or discard changes before loading a new scene."
                );
                // Example: bool saveOK = EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo();
                // if (!saveOK) return new ErrorResponse("Load cancelled by user.");
            }

            try
            {
                EditorSceneManager.OpenScene(relativePath, OpenSceneMode.Single);
                return new SuccessResponse(
                    $"Scene '{relativePath}' loaded successfully.",
                    new
                    {
                        path = relativePath,
                        name = Path.GetFileNameWithoutExtension(relativePath),
                    }
                );
            }
            catch (Exception e)
            {
                return new ErrorResponse($"Error loading scene '{relativePath}': {e.Message}");
            }
        }

        private static object LoadScene(int buildIndex)
        {
            if (buildIndex < 0 || buildIndex >= SceneManager.sceneCountInBuildSettings)
            {
                return new ErrorResponse(
                    $"Invalid build index: {buildIndex}. Must be between 0 and {SceneManager.sceneCountInBuildSettings - 1}."
                );
            }

            // Check for unsaved changes
            if (EditorSceneManager.GetActiveScene().isDirty)
            {
                return new ErrorResponse(
                    "Current scene has unsaved changes. Please save or discard changes before loading a new scene."
                );
            }

            try
            {
                string scenePath = SceneUtility.GetScenePathByBuildIndex(buildIndex);
                EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
                return new SuccessResponse(
                    $"Scene at build index {buildIndex} ('{scenePath}') loaded successfully.",
                    new
                    {
                        path = scenePath,
                        name = Path.GetFileNameWithoutExtension(scenePath),
                        buildIndex = buildIndex,
                    }
                );
            }
            catch (Exception e)
            {
                return new ErrorResponse(
                    $"Error loading scene with build index {buildIndex}: {e.Message}"
                );
            }
        }

        private static object SaveScene(string fullPath, string relativePath)
        {
            try
            {
                Scene currentScene = EditorSceneManager.GetActiveScene();
                if (!currentScene.IsValid())
                {
                    return new ErrorResponse("No valid scene is currently active to save.");
                }

                bool saved;
                string finalPath = currentScene.path; // Path where it was last saved or will be saved

                if (!string.IsNullOrEmpty(relativePath) && currentScene.path != relativePath)
                {
                    // Save As...
                    // Ensure directory exists
                    string dir = Path.GetDirectoryName(fullPath);
                    if (!Directory.Exists(dir))
                        Directory.CreateDirectory(dir);

                    saved = EditorSceneManager.SaveScene(currentScene, relativePath);
                    finalPath = relativePath;
                }
                else
                {
                    // Save (overwrite existing or save untitled)
                    if (string.IsNullOrEmpty(currentScene.path))
                    {
                        // Scene is untitled, needs a path
                        return new ErrorResponse(
                            "Cannot save an untitled scene without providing a 'name' and 'path'. Use Save As functionality."
                        );
                    }
                    saved = EditorSceneManager.SaveScene(currentScene);
                }

                if (saved)
                {
                    AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                    return new SuccessResponse(
                        $"Scene '{currentScene.name}' saved successfully to '{finalPath}'.",
                        new { path = finalPath, name = currentScene.name }
                    );
                }
                else
                {
                    return new ErrorResponse($"Failed to save scene '{currentScene.name}'.");
                }
            }
            catch (Exception e)
            {
                return new ErrorResponse($"Error saving scene: {e.Message}");
            }
        }

        private static object CaptureScreenshot(SceneCommand cmd)
        {
            try
            {
                // Batch capture (e.g., "surround" for 6 angles around the scene)
                if (!string.IsNullOrEmpty(cmd.batch))
                {
                    if (cmd.batch.Equals("surround", StringComparison.OrdinalIgnoreCase))
                        return CaptureSurroundBatch(cmd);
                    if (cmd.batch.Equals("orbit", StringComparison.OrdinalIgnoreCase))
                        return CaptureOrbitBatch(cmd);
                    return new ErrorResponse($"Unknown batch mode: '{cmd.batch}'. Valid modes: 'surround', 'orbit'.");
                }

                // Positioned view-based capture (creates temp camera at view_position, aimed at look_at)
                if ((cmd.lookAt != null && cmd.lookAt.Type != JTokenType.Null) || cmd.viewPosition.HasValue)
                {
                    return CapturePositionedScreenshot(cmd);
                }

                string fileName = cmd.fileName;
                int resolvedSuperSize = (cmd.superSize.HasValue && cmd.superSize.Value > 0) ? cmd.superSize.Value : 1;
                bool includeImage = cmd.includeImage ?? false;
                int maxResolution = cmd.maxResolution ?? 0; // 0 = let ScreenshotUtility default to 640
                string cameraRef = cmd.camera;

                // Batch mode warning
                if (Application.isBatchMode)
                {
                    McpLog.Warn("[ManageScene] Screenshot capture in batch mode uses camera-based fallback. Results may vary.");
                }

                // Resolve camera target
                Camera targetCamera = null;
                if (!string.IsNullOrEmpty(cameraRef))
                {
                    targetCamera = ResolveCamera(cameraRef);
                    if (targetCamera == null)
                    {
                        return new ErrorResponse($"Camera '{cameraRef}' not found. Provide a Camera GameObject name, path, or instance ID.");
                    }
                }

                // When a specific camera is requested or include_image is true, always use camera-based capture
                // (synchronous, gives us bytes in memory for base64).
                if (targetCamera != null || includeImage)
                {
                    if (targetCamera == null)
                    {
                        targetCamera = Camera.main;
                        if (targetCamera == null)
                        {
                            var allCams = UnityEngine.Object.FindObjectsOfType<Camera>();
                            targetCamera = allCams.Length > 0 ? allCams[0] : null;
                        }
                    }
                    if (targetCamera == null)
                    {
                        return new ErrorResponse("No camera found in the scene. Add a Camera to use screenshot with camera or include_image.");
                    }

                    if (!Application.isBatchMode) EnsureGameView();

                    ScreenshotCaptureResult result = ScreenshotUtility.CaptureFromCameraToAssetsFolder(
                        targetCamera, fileName, resolvedSuperSize, ensureUniqueFileName: true,
                        includeImage: includeImage, maxResolution: maxResolution);

                    AssetDatabase.ImportAsset(result.AssetsRelativePath, ImportAssetOptions.ForceSynchronousImport);
                    string message = $"Screenshot captured to '{result.AssetsRelativePath}' (camera: {targetCamera.name}).";

                    var data = new Dictionary<string, object>
                    {
                        { "path", result.AssetsRelativePath },
                        { "fullPath", result.FullPath },
                        { "superSize", result.SuperSize },
                        { "isAsync", false },
                        { "camera", targetCamera.name },
                    };
                    if (includeImage && result.ImageBase64 != null)
                    {
                        data["imageBase64"] = result.ImageBase64;
                        data["imageWidth"] = result.ImageWidth;
                        data["imageHeight"] = result.ImageHeight;
                    }
                    return new SuccessResponse(message, data);
                }

                // Default path: use ScreenCapture API if available, camera fallback otherwise
                bool screenCaptureAvailable = ScreenshotUtility.IsScreenCaptureModuleAvailable;
                bool hasCameraFallback = Camera.main != null || UnityEngine.Object.FindObjectsOfType<Camera>().Length > 0;

#if UNITY_2022_1_OR_NEWER
                if (!screenCaptureAvailable && !hasCameraFallback)
                {
                    return new ErrorResponse(
                        "Cannot capture screenshot. The Screen Capture module is not enabled and no Camera was found in the scene. " +
                        "Please either: (1) Enable the Screen Capture module: Window > Package Manager > Built-in > Screen Capture > Enable, " +
                        "or (2) Add a Camera to your scene for camera-based fallback capture."
                    );
                }
                if (!screenCaptureAvailable)
                {
                    McpLog.Warn("[ManageScene] Screen Capture module not enabled. Using camera-based fallback.");
                }
#else
                if (!hasCameraFallback)
                {
                    return new ErrorResponse(
                        "No camera found in the scene. Screenshot capture on Unity versions before 2022.1 requires a Camera in the scene."
                    );
                }
#endif

                if (!Application.isBatchMode) EnsureGameView();

                ScreenshotCaptureResult defaultResult = ScreenshotUtility.CaptureToAssetsFolder(fileName, resolvedSuperSize, ensureUniqueFileName: true);

                if (defaultResult.IsAsync)
                    ScheduleAssetImportWhenFileExists(defaultResult.AssetsRelativePath, defaultResult.FullPath, timeoutSeconds: 30.0);
                else
                    AssetDatabase.ImportAsset(defaultResult.AssetsRelativePath, ImportAssetOptions.ForceSynchronousImport);

                string verb = defaultResult.IsAsync ? "Screenshot requested" : "Screenshot captured";
                return new SuccessResponse(
                    $"{verb} to '{defaultResult.AssetsRelativePath}'.",
                    new
                    {
                        path = defaultResult.AssetsRelativePath,
                        fullPath = defaultResult.FullPath,
                        superSize = defaultResult.SuperSize,
                        isAsync = defaultResult.IsAsync,
                    }
                );
            }
            catch (Exception e)
            {
                return new ErrorResponse($"Error capturing screenshot: {e.Message}");
            }
        }

        /// <summary>
        /// Captures screenshots from 6 angles around scene bounds (or a look_at target) for AI scene understanding.
        /// Does NOT save to disk — returns all images as inline base64 PNGs. Always uses camera-based capture.
        /// </summary>
        private static object CaptureSurroundBatch(SceneCommand cmd)
        {
            try
            {
                int maxRes = cmd.maxResolution ?? 480;

                Vector3 center;
                float radius;

                // If look_at is provided, center on that target instead of scene bounds
                if (cmd.lookAt != null && cmd.lookAt.Type != JTokenType.Null)
                {
                    var lookAtPos = VectorParsing.ParseVector3(cmd.lookAt);
                    if (lookAtPos.HasValue)
                    {
                        center = lookAtPos.Value;
                        radius = 5f;
                    }
                    else
                    {
                        Scene lookAtScene = EditorSceneManager.GetActiveScene();
                        var lookAtGo = ResolveGameObject(cmd.lookAt, lookAtScene);
                        if (lookAtGo == null)
                            return new ErrorResponse($"look_at target '{cmd.lookAt}' not found for batch capture.");

                        Bounds targetBounds = new Bounds(lookAtGo.transform.position, Vector3.zero);
                        foreach (var r in lookAtGo.GetComponentsInChildren<Renderer>())
                        {
                            if (r != null && r.gameObject.activeInHierarchy) targetBounds.Encapsulate(r.bounds);
                        }
                        center = targetBounds.center;
                        radius = targetBounds.extents.magnitude * 1.8f;
                        radius = Mathf.Max(radius, 3f);
                    }
                }
                else
                {
                    // Default: calculate combined bounds of all renderers in the scene
                    Bounds bounds = new Bounds(Vector3.zero, Vector3.zero);
                    bool hasBounds = false;
                    var renderers = UnityEngine.Object.FindObjectsOfType<Renderer>();
                    foreach (var r in renderers)
                    {
                        if (r == null || !r.gameObject.activeInHierarchy) continue;
                        if (!hasBounds)
                        {
                            bounds = r.bounds;
                            hasBounds = true;
                        }
                        else
                        {
                            bounds.Encapsulate(r.bounds);
                        }
                    }

                    if (!hasBounds)
                        return new ErrorResponse("No renderers found in the scene. Cannot determine scene bounds for batch capture.");

                    center = bounds.center;
                    radius = bounds.extents.magnitude * 1.8f;
                    radius = Mathf.Max(radius, 3f);
                }

                // Define 6 viewpoints: front, back, left, right, top, bird's-eye (45° elevated front-right)
                var angles = new[]
                {
                    ("front", new Vector3(center.x, center.y, center.z - radius)),
                    ("back", new Vector3(center.x, center.y, center.z + radius)),
                    ("left", new Vector3(center.x - radius, center.y, center.z)),
                    ("right", new Vector3(center.x + radius, center.y, center.z)),
                    ("top", new Vector3(center.x, center.y + radius, center.z)),
                    ("bird_eye", new Vector3(center.x + radius * 0.7f, center.y + radius * 0.7f, center.z - radius * 0.7f)),
                };

                // Create a temporary camera
                var tempGo = new GameObject("__MCP_MultiAngle_Temp_Camera__");
                Camera tempCam = tempGo.AddComponent<Camera>();
                tempCam.fieldOfView = 60f;
                tempCam.nearClipPlane = 0.1f;
                tempCam.farClipPlane = radius * 4f;
                tempCam.clearFlags = CameraClearFlags.Skybox;

                var tiles = new List<Texture2D>();
                var tileLabels = new List<string>();
                var shotMeta = new List<object>();
                try
                {
                    foreach (var (label, pos) in angles)
                    {
                        tempCam.transform.position = pos;
                        tempCam.transform.LookAt(center);

                        Texture2D tile = ScreenshotUtility.RenderCameraToTexture(tempCam, maxRes);
                        tiles.Add(tile);
                        tileLabels.Add(label);
                        shotMeta.Add(new Dictionary<string, object>
                        {
                            { "angle", label },
                            { "position", new[] { pos.x, pos.y, pos.z } },
                        });
                    }

                    var (compositeB64, compW, compH) = ScreenshotUtility.ComposeContactSheet(tiles, tileLabels);

                    string screenshotsFolder = Path.Combine(Application.dataPath, "Screenshots");
                    return new SuccessResponse(
                        $"Captured {shotMeta.Count} multi-angle screenshots as contact sheet ({compW}x{compH}). Scene bounds center: ({center.x:F1}, {center.y:F1}, {center.z:F1}), radius: {radius:F1}.",
                        new
                        {
                            sceneCenter = new[] { center.x, center.y, center.z },
                            sceneRadius = radius,
                            screenshotsFolder = screenshotsFolder,
                            imageBase64 = compositeB64,
                            imageWidth = compW,
                            imageHeight = compH,
                            shots = shotMeta,
                        }
                    );
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(tempGo);
                }
            }
            catch (Exception e)
            {
                return new ErrorResponse($"Error capturing batch screenshots: {e.Message}");
            }
        }

        /// <summary>
        /// Captures screenshots from a configurable orbit around a target for visual QA.
        /// Supports custom azimuth count, elevation angles, distance, and FOV.
        /// Returns all images as inline base64 PNGs (no files saved to disk).
        /// </summary>
        private static object CaptureOrbitBatch(SceneCommand cmd)
        {
            try
            {
                int maxRes = cmd.maxResolution ?? 480;
                int azimuthCount = Mathf.Clamp(cmd.orbitAngles ?? 8, 1, 36);
                float[] elevations = cmd.orbitElevations ?? new[] { 0f, 30f, -15f };
                float fov = Mathf.Clamp(cmd.orbitFov ?? 60f, 10f, 120f);

                Vector3 center;
                float radius;

                // Resolve center and radius from look_at target or scene bounds
                if (cmd.lookAt != null && cmd.lookAt.Type != JTokenType.Null)
                {
                    var lookAtPos = VectorParsing.ParseVector3(cmd.lookAt);
                    if (lookAtPos.HasValue)
                    {
                        center = lookAtPos.Value;
                        radius = cmd.orbitDistance ?? 5f;
                    }
                    else
                    {
                        Scene lookAtScene = EditorSceneManager.GetActiveScene();
                        var lookAtGo = ResolveGameObject(cmd.lookAt, lookAtScene);
                        if (lookAtGo == null)
                            return new ErrorResponse($"look_at target '{cmd.lookAt}' not found for orbit capture.");

                        Bounds targetBounds = new Bounds(lookAtGo.transform.position, Vector3.zero);
                        foreach (var r in lookAtGo.GetComponentsInChildren<Renderer>())
                        {
                            if (r != null && r.gameObject.activeInHierarchy) targetBounds.Encapsulate(r.bounds);
                        }
                        center = targetBounds.center;
                        radius = cmd.orbitDistance ?? Mathf.Max(targetBounds.extents.magnitude * 2.0f, 3f);
                    }
                }
                else
                {
                    // Default: calculate combined bounds of all renderers in the scene
                    Bounds bounds = new Bounds(Vector3.zero, Vector3.zero);
                    bool hasBounds = false;
                    var renderers = UnityEngine.Object.FindObjectsOfType<Renderer>();
                    foreach (var r in renderers)
                    {
                        if (r == null || !r.gameObject.activeInHierarchy) continue;
                        if (!hasBounds) { bounds = r.bounds; hasBounds = true; }
                        else bounds.Encapsulate(r.bounds);
                    }

                    if (!hasBounds)
                        return new ErrorResponse("No renderers found in the scene. Cannot determine scene bounds for orbit capture.");

                    center = bounds.center;
                    radius = cmd.orbitDistance ?? Mathf.Max(bounds.extents.magnitude * 2.0f, 3f);
                }

                // Create a temporary camera
                var tempGo = new GameObject("__MCP_OrbitCapture_Temp_Camera__");
                Camera tempCam = tempGo.AddComponent<Camera>();
                tempCam.fieldOfView = fov;
                tempCam.nearClipPlane = 0.1f;
                tempCam.farClipPlane = radius * 4f;
                tempCam.clearFlags = CameraClearFlags.Skybox;

                var tiles = new List<Texture2D>();
                var tileLabels = new List<string>();
                var shotMeta = new List<object>();
                try
                {
                    foreach (float elevDeg in elevations)
                    {
                        float elevRad = elevDeg * Mathf.Deg2Rad;
                        float y = Mathf.Sin(elevRad) * radius;
                        float horizontalRadius = Mathf.Cos(elevRad) * radius;

                        for (int i = 0; i < azimuthCount; i++)
                        {
                            float azimuthDeg = i * (360f / azimuthCount);
                            float azimuthRad = azimuthDeg * Mathf.Deg2Rad;

                            float x = Mathf.Sin(azimuthRad) * horizontalRadius;
                            float z = Mathf.Cos(azimuthRad) * horizontalRadius;

                            Vector3 pos = center + new Vector3(x, y, z);
                            tempCam.transform.position = pos;
                            tempCam.transform.LookAt(center);

                            string dirLabel = GetDirectionLabel(azimuthDeg);
                            if (azimuthCount > 8)
                                dirLabel += $"_{azimuthDeg:F0}deg";
                            string elevLabel = elevDeg > 0 ? $"above{elevDeg:F0}"
                                             : elevDeg < 0 ? $"below{Mathf.Abs(elevDeg):F0}"
                                             : "level";
                            string angleLabel = $"{dirLabel}_{elevLabel}";

                            Texture2D tile = ScreenshotUtility.RenderCameraToTexture(tempCam, maxRes);
                            tiles.Add(tile);
                            tileLabels.Add(angleLabel);
                            shotMeta.Add(new Dictionary<string, object>
                            {
                                { "angle", angleLabel },
                                { "azimuth", azimuthDeg },
                                { "elevation", elevDeg },
                                { "position", new[] { pos.x, pos.y, pos.z } },
                            });
                        }
                    }

                    // Compose all tiles into a single contact-sheet grid image
                    var (compositeB64, compW, compH) = ScreenshotUtility.ComposeContactSheet(tiles, tileLabels);

                    string screenshotsFolder = Path.Combine(Application.dataPath, "Screenshots");
                    return new SuccessResponse(
                        $"Captured {shotMeta.Count} orbit screenshots as contact sheet ({compW}x{compH}, {azimuthCount} azimuths x {elevations.Length} elevations). Center: ({center.x:F1}, {center.y:F1}, {center.z:F1}), radius: {radius:F1}.",
                        new
                        {
                            sceneCenter = new[] { center.x, center.y, center.z },
                            orbitRadius = radius,
                            orbitAngles = azimuthCount,
                            orbitElevations = elevations,
                            orbitFov = fov,
                            screenshotsFolder = screenshotsFolder,
                            imageBase64 = compositeB64,
                            imageWidth = compW,
                            imageHeight = compH,
                            shots = shotMeta,
                        }
                    );
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(tempGo);
                }
            }
            catch (Exception e)
            {
                return new ErrorResponse($"Error capturing orbit screenshots: {e.Message}");
            }
        }

        /// <summary>
        /// Captures a single screenshot from a temporary camera placed at view_position and aimed at look_at.
        /// Returns inline base64 PNG (no file saved to disk).
        /// </summary>
        private static object CapturePositionedScreenshot(SceneCommand cmd)
        {
            try
            {
                int maxRes = cmd.maxResolution ?? 640;

                // Resolve where to aim
                Vector3? targetPos = null;
                if (cmd.lookAt != null && cmd.lookAt.Type != JTokenType.Null)
                {
                    var parsedPos = VectorParsing.ParseVector3(cmd.lookAt);
                    if (parsedPos.HasValue)
                    {
                        targetPos = parsedPos.Value;
                    }
                    else
                    {
                        Scene activeScene = EditorSceneManager.GetActiveScene();
                        var lookAtGo = ResolveGameObject(cmd.lookAt, activeScene);
                        if (lookAtGo == null)
                            return new ErrorResponse($"look_at target '{cmd.lookAt}' not found.");
                        targetPos = lookAtGo.transform.position;
                    }
                }

                // Determine camera position
                Vector3 camPos;
                if (cmd.viewPosition.HasValue)
                {
                    camPos = cmd.viewPosition.Value;
                }
                else if (targetPos.HasValue)
                {
                    // Default: offset from look_at target
                    camPos = targetPos.Value + new Vector3(0, 2, -5);
                }
                else
                {
                    return new ErrorResponse("Provide 'look_at' or 'view_position' for a positioned screenshot.");
                }

                // Create temporary camera
                var tempGo = new GameObject("__MCP_PositionedCapture_Temp__");
                Camera tempCam = tempGo.AddComponent<Camera>();
                tempCam.fieldOfView = 60f;
                tempCam.nearClipPlane = 0.1f;
                tempCam.farClipPlane = 1000f;
                tempCam.clearFlags = CameraClearFlags.Skybox;
                tempCam.transform.position = camPos;

                try
                {
                    if (cmd.viewRotation.HasValue)
                        tempCam.transform.rotation = Quaternion.Euler(cmd.viewRotation.Value);
                    else if (targetPos.HasValue)
                        tempCam.transform.LookAt(targetPos.Value);

                    var (b64, w, h) = ScreenshotUtility.RenderCameraToBase64(tempCam, maxRes);

                    string screenshotsFolder = Path.Combine(Application.dataPath, "Screenshots");
                    var data = new Dictionary<string, object>
                    {
                        { "imageBase64", b64 },
                        { "imageWidth", w },
                        { "imageHeight", h },
                        { "viewPosition", new[] { camPos.x, camPos.y, camPos.z } },
                        { "screenshotsFolder", screenshotsFolder },
                    };
                    if (targetPos.HasValue)
                        data["lookAt"] = new[] { targetPos.Value.x, targetPos.Value.y, targetPos.Value.z };

                    return new SuccessResponse(
                        $"Positioned screenshot captured (max {maxRes}px).",
                        data
                    );
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(tempGo);
                }
            }
            catch (Exception e)
            {
                return new ErrorResponse($"Error capturing positioned screenshot: {e.Message}");
            }
        }

        private static string GetDirectionLabel(float azimuthDeg)
        {
            float a = ((azimuthDeg % 360f) + 360f) % 360f;
            if (a < 22.5f || a >= 337.5f) return "front";
            if (a < 67.5f)  return "front_right";
            if (a < 112.5f) return "right";
            if (a < 157.5f) return "back_right";
            if (a < 202.5f) return "back";
            if (a < 247.5f) return "back_left";
            if (a < 292.5f) return "left";
            return "front_left";
        }

        /// <summary>
        /// Resolves a camera by name, path, or instance ID.
        /// </summary>
        private static Camera ResolveCamera(string cameraRef)
        {
            if (string.IsNullOrEmpty(cameraRef)) return null;

            // Try instance ID
            if (int.TryParse(cameraRef, out int id))
            {
                var obj = EditorUtility.InstanceIDToObject(id);
                if (obj is Camera cam) return cam;
                if (obj is GameObject go) return go.GetComponent<Camera>();
            }

            // Search all cameras by name or path
            var allCams = UnityEngine.Object.FindObjectsOfType<Camera>();
            foreach (var cam in allCams)
            {
                if (cam.name == cameraRef) return cam;
                if (cam.gameObject.name == cameraRef) return cam;
            }

            // Try path-based lookup
            if (cameraRef.Contains("/"))
            {
                var ids = GameObjectLookup.SearchGameObjects("by_path", cameraRef, includeInactive: false, maxResults: 1);
                if (ids.Count > 0)
                {
                    var go = GameObjectLookup.FindById(ids[0]);
                    if (go != null) return go.GetComponent<Camera>();
                }
            }

            return null;
        }

        /// <summary>
        /// Frames the Scene View on a target GameObject or the entire scene.
        /// </summary>
        private static object FrameSceneView(SceneCommand cmd)
        {
            try
            {
                var sceneView = SceneView.lastActiveSceneView;
                if (sceneView == null)
                {
                    return new ErrorResponse("No active Scene View found. Open a Scene View window first.");
                }

                if (cmd.sceneViewTarget != null && cmd.sceneViewTarget.Type != JTokenType.Null)
                {
                    Scene activeScene = EditorSceneManager.GetActiveScene();
                    GameObject target = ResolveGameObject(cmd.sceneViewTarget, activeScene);
                    if (target == null)
                    {
                        return new ErrorResponse($"Target GameObject '{cmd.sceneViewTarget}' not found for scene_view_frame.");
                    }

                    // Calculate bounds from renderers, colliders, or transform
                    Bounds bounds = new Bounds(target.transform.position, Vector3.zero);
                    var renderers = target.GetComponentsInChildren<Renderer>();
                    if (renderers.Length > 0)
                    {
                        bounds = renderers[0].bounds;
                        for (int i = 1; i < renderers.Length; i++)
                            bounds.Encapsulate(renderers[i].bounds);
                    }
                    else
                    {
                        var colliders = target.GetComponentsInChildren<Collider>();
                        if (colliders.Length > 0)
                        {
                            bounds = colliders[0].bounds;
                            for (int i = 1; i < colliders.Length; i++)
                                bounds.Encapsulate(colliders[i].bounds);
                        }
                        else
                        {
                            bounds = new Bounds(target.transform.position, Vector3.one);
                        }
                    }

                    sceneView.Frame(bounds, false);
                    return new SuccessResponse($"Scene View framed on '{target.name}'.", new { target = target.name });
                }
                else
                {
                    // Frame entire scene by computing combined bounds of all renderers
                    Bounds allBounds = new Bounds(Vector3.zero, Vector3.zero);
                    bool hasAny = false;
                    foreach (var r in UnityEngine.Object.FindObjectsOfType<Renderer>())
                    {
                        if (r == null || !r.gameObject.activeInHierarchy) continue;
                        if (!hasAny) { allBounds = r.bounds; hasAny = true; }
                        else allBounds.Encapsulate(r.bounds);
                    }
                    if (!hasAny) allBounds = new Bounds(Vector3.zero, Vector3.one * 10f);
                    sceneView.Frame(allBounds, false);
                    return new SuccessResponse("Scene View framed on entire scene.");
                }
            }
            catch (Exception e)
            {
                return new ErrorResponse($"Error framing Scene View: {e.Message}");
            }
        }

        private static void EnsureGameView()
        {
            try
            {
                // Ensure a Game View exists and has a chance to repaint before capture.
                try
                {
                    if (!EditorApplication.ExecuteMenuItem("Window/General/Game"))
                    {
                        // Some Unity versions expose hotkey suffixes in menu paths.
                        EditorApplication.ExecuteMenuItem("Window/General/Game %2");
                    }
                }
                catch (Exception e)
                {
                    try { McpLog.Debug($"[ManageScene] screenshot: failed to open Game View via menu item: {e.Message}"); } catch { }
                }

                try
                {
                    var gameViewType = Type.GetType("UnityEditor.GameView,UnityEditor");
                    if (gameViewType != null)
                    {
                        var window = EditorWindow.GetWindow(gameViewType);
                        window?.Repaint();
                    }
                }
                catch (Exception e)
                {
                    try { McpLog.Debug($"[ManageScene] screenshot: failed to repaint Game View: {e.Message}"); } catch { }
                }

                try { SceneView.RepaintAll(); }
                catch (Exception e)
                {
                    try { McpLog.Debug($"[ManageScene] screenshot: failed to repaint Scene View: {e.Message}"); } catch { }
                }

                try { EditorApplication.QueuePlayerLoopUpdate(); }
                catch (Exception e)
                {
                    try { McpLog.Debug($"[ManageScene] screenshot: failed to queue player loop update: {e.Message}"); } catch { }
                }
            }
            catch (Exception e)
            {
                try { McpLog.Debug($"[ManageScene] screenshot: EnsureGameView failed: {e.Message}"); } catch { }
            }
        }

        private static void ScheduleAssetImportWhenFileExists(string assetsRelativePath, string fullPath, double timeoutSeconds)
        {
            if (string.IsNullOrWhiteSpace(assetsRelativePath) || string.IsNullOrWhiteSpace(fullPath))
            {
                McpLog.Warn("[ManageScene] ScheduleAssetImportWhenFileExists: invalid paths provided, skipping import scheduling.");
                return;
            }

            double start = EditorApplication.timeSinceStartup;
            int failureCount = 0;
            bool hasSeenFile = false;
            const int maxLoggedFailures = 3;
            EditorApplication.CallbackFunction tick = null;
            tick = () =>
            {
                try
                {
                    if (File.Exists(fullPath))
                    {
                        hasSeenFile = true;

                        AssetDatabase.ImportAsset(assetsRelativePath, ImportAssetOptions.ForceSynchronousImport);
                        McpLog.Debug($"[ManageScene] Imported asset at '{assetsRelativePath}'.");
                        EditorApplication.update -= tick;
                        return;
                    }
                }
                catch (Exception e)
                {
                    failureCount++;

                    if (failureCount <= maxLoggedFailures)
                    {
                        McpLog.Warn($"[ManageScene] Exception while importing asset '{assetsRelativePath}' from '{fullPath}' (attempt {failureCount}): {e}");
                    }
                }

                if (EditorApplication.timeSinceStartup - start > timeoutSeconds)
                {
                    if (!hasSeenFile)
                    {
                        McpLog.Warn($"[ManageScene] Timed out waiting for file '{fullPath}' (asset: '{assetsRelativePath}') after {timeoutSeconds:F1} seconds. The asset was not imported.");
                    }
                    else
                    {
                        McpLog.Warn($"[ManageScene] Timed out importing asset '{assetsRelativePath}' from '{fullPath}' after {timeoutSeconds:F1} seconds. The file existed but the asset was not imported.");
                    }

                    EditorApplication.update -= tick;
                }
            };

            EditorApplication.update += tick;
        }

        private static object GetActiveSceneInfo()
        {
            try
            {
                try { McpLog.Info("[ManageScene] get_active: querying EditorSceneManager.GetActiveScene", always: false); } catch { }
                Scene activeScene = EditorSceneManager.GetActiveScene();
                try { McpLog.Info($"[ManageScene] get_active: got scene valid={activeScene.IsValid()} loaded={activeScene.isLoaded} name='{activeScene.name}'", always: false); } catch { }
                if (!activeScene.IsValid())
                {
                    return new ErrorResponse("No active scene found.");
                }

                var sceneInfo = new
                {
                    name = activeScene.name,
                    path = activeScene.path,
                    buildIndex = activeScene.buildIndex, // -1 if not in build settings
                    isDirty = activeScene.isDirty,
                    isLoaded = activeScene.isLoaded,
                    rootCount = activeScene.rootCount,
                };

                return new SuccessResponse("Retrieved active scene information.", sceneInfo);
            }
            catch (Exception e)
            {
                try { McpLog.Error($"[ManageScene] get_active: exception {e.Message}"); } catch { }
                return new ErrorResponse($"Error getting active scene info: {e.Message}");
            }
        }

        private static object GetBuildSettingsScenes()
        {
            try
            {
                var scenes = new List<object>();
                for (int i = 0; i < EditorBuildSettings.scenes.Length; i++)
                {
                    var scene = EditorBuildSettings.scenes[i];
                    scenes.Add(
                        new
                        {
                            path = scene.path,
                            guid = scene.guid.ToString(),
                            enabled = scene.enabled,
                            buildIndex = i, // Actual build index considering only enabled scenes might differ
                        }
                    );
                }
                return new SuccessResponse("Retrieved scenes from Build Settings.", scenes);
            }
            catch (Exception e)
            {
                return new ErrorResponse($"Error getting scenes from Build Settings: {e.Message}");
            }
        }

        private static object GetSceneHierarchyPaged(SceneCommand cmd)
        {
            try
            {
                // Check Prefab Stage first
                var prefabStage = PrefabStageUtility.GetCurrentPrefabStage();
                Scene activeScene;
                
                if (prefabStage != null)
                {
                    activeScene = prefabStage.scene;
                    try { McpLog.Info("[ManageScene] get_hierarchy: using Prefab Stage scene", always: false); } catch { }
                }
                else
                {
                    try { McpLog.Info("[ManageScene] get_hierarchy: querying EditorSceneManager.GetActiveScene", always: false); } catch { }
                    activeScene = EditorSceneManager.GetActiveScene();
                }
                
                try { McpLog.Info($"[ManageScene] get_hierarchy: got scene valid={activeScene.IsValid()} loaded={activeScene.isLoaded} name='{activeScene.name}'", always: false); } catch { }
                if (!activeScene.IsValid() || !activeScene.isLoaded)
                {
                    return new ErrorResponse(
                        "No valid and loaded scene is active to get hierarchy from."
                    );
                }

                // Defaults tuned for safety; callers can override but we clamp to sane maxes.
                // NOTE: pageSize is "items per page", not "number of pages".
                // Keep this conservative to reduce peak response sizes when callers omit page_size.
                int resolvedPageSize = Mathf.Clamp(cmd.pageSize ?? 50, 1, 500);
                int resolvedCursor = Mathf.Max(0, cmd.cursor ?? 0);
                int resolvedMaxNodes = Mathf.Clamp(cmd.maxNodes ?? 1000, 1, 5000);
                int effectiveTake = Mathf.Min(resolvedPageSize, resolvedMaxNodes);
                int resolvedMaxChildrenPerNode = Mathf.Clamp(cmd.maxChildrenPerNode ?? 200, 0, 2000);
                bool includeTransform = cmd.includeTransform ?? false;

                // NOTE: maxDepth is accepted for forward-compatibility, but current paging mode
                // returns a single level (roots or direct children). This keeps payloads bounded.

                List<GameObject> nodes;
                string scope;

                GameObject parentGo = ResolveGameObject(cmd.parent, activeScene);
                if (cmd.parent == null || cmd.parent.Type == JTokenType.Null)
                {
                    try { McpLog.Info("[ManageScene] get_hierarchy: listing root objects (paged summary)", always: false); } catch { }
                    nodes = activeScene.GetRootGameObjects().Where(go => go != null).ToList();
                    scope = "roots";
                }
                else
                {
                    if (parentGo == null)
                    {
                        return new ErrorResponse($"Parent GameObject ('{cmd.parent}') not found.");
                    }
                    try { McpLog.Info($"[ManageScene] get_hierarchy: listing children of '{parentGo.name}' (paged summary)", always: false); } catch { }
                    nodes = new List<GameObject>(parentGo.transform.childCount);
                    foreach (Transform child in parentGo.transform)
                    {
                        if (child != null) nodes.Add(child.gameObject);
                    }
                    scope = "children";
                }

                int total = nodes.Count;
                if (resolvedCursor > total) resolvedCursor = total;
                int end = Mathf.Min(total, resolvedCursor + effectiveTake);

                var items = new List<object>(Mathf.Max(0, end - resolvedCursor));
                for (int i = resolvedCursor; i < end; i++)
                {
                    var go = nodes[i];
                    if (go == null) continue;
                    items.Add(BuildGameObjectSummary(go, includeTransform, resolvedMaxChildrenPerNode));
                }

                bool truncated = end < total;
                string nextCursor = truncated ? end.ToString() : null;

                var payload = new
                {
                    scope = scope,
                    cursor = resolvedCursor,
                    pageSize = effectiveTake,
                    next_cursor = nextCursor,
                    truncated = truncated,
                    total = total,
                    items = items,
                };

                var resp = new SuccessResponse($"Retrieved hierarchy page for scene '{activeScene.name}'.", payload);
                try { McpLog.Info("[ManageScene] get_hierarchy: success", always: false); } catch { }
                return resp;
            }
            catch (Exception e)
            {
                try { McpLog.Error($"[ManageScene] get_hierarchy: exception {e.Message}"); } catch { }
                return new ErrorResponse($"Error getting scene hierarchy: {e.Message}");
            }
        }

        private static GameObject ResolveGameObject(JToken targetToken, Scene activeScene)
        {
            if (targetToken == null || targetToken.Type == JTokenType.Null) return null;

            try
            {
                if (targetToken.Type == JTokenType.Integer || int.TryParse(targetToken.ToString(), out _))
                {
                    if (int.TryParse(targetToken.ToString(), out int id))
                    {
                        var obj = EditorUtility.InstanceIDToObject(id);
                        if (obj is GameObject go) return go;
                        if (obj is Component c) return c.gameObject;
                    }
                }
            }
            catch { }

            string s = targetToken.ToString();
            if (string.IsNullOrEmpty(s)) return null;

            // Path-based find (e.g., "Root/Child/GrandChild")
            if (s.Contains("/"))
            {
                try
                {
                    var ids = GameObjectLookup.SearchGameObjects("by_path", s, includeInactive: true, maxResults: 1);
                    if (ids.Count > 0)
                    {
                        var byPath = GameObjectLookup.FindById(ids[0]);
                        if (byPath != null) return byPath;
                    }
                }
                catch { }
            }

            // Name-based find (first match, includes inactive)
            try
            {
                var all = activeScene.GetRootGameObjects();
                foreach (var root in all)
                {
                    if (root == null) continue;
                    if (root.name == s) return root;
                    var trs = root.GetComponentsInChildren<Transform>(includeInactive: true);
                    foreach (var t in trs)
                    {
                        if (t != null && t.gameObject != null && t.gameObject.name == s) return t.gameObject;
                    }
                }
            }
            catch { }

            return null;
        }

        private static object BuildGameObjectSummary(GameObject go, bool includeTransform, int maxChildrenPerNode)
        {
            if (go == null) return null;

            int childCount = 0;
            try { childCount = go.transform != null ? go.transform.childCount : 0; } catch { }
            bool childrenTruncated = childCount > 0; // We do not inline children in summary mode.

            // Get component type names (lightweight - no full serialization)
            var componentTypes = new List<string>();
            try
            {
                var components = go.GetComponents<Component>();
                if (components != null)
                {
                    foreach (var c in components)
                    {
                        if (c != null)
                        {
                            componentTypes.Add(c.GetType().Name);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                McpLog.Debug($"[ManageScene] Failed to enumerate components for '{go.name}': {ex.Message}");
            }

            var d = new Dictionary<string, object>
            {
                { "name", go.name },
                { "instanceID", go.GetInstanceID() },
                { "activeSelf", go.activeSelf },
                { "activeInHierarchy", go.activeInHierarchy },
                { "tag", go.tag },
                { "layer", go.layer },
                { "isStatic", go.isStatic },
                { "path", GetGameObjectPath(go) },
                { "childCount", childCount },
                { "childrenTruncated", childrenTruncated },
                { "childrenCursor", childCount > 0 ? "0" : null },
                { "childrenPageSizeDefault", maxChildrenPerNode },
                { "componentTypes", componentTypes },
            };

            if (includeTransform && go.transform != null)
            {
                var t = go.transform;
                d["transform"] = new
                {
                    position = new[] { t.localPosition.x, t.localPosition.y, t.localPosition.z },
                    rotation = new[] { t.localRotation.eulerAngles.x, t.localRotation.eulerAngles.y, t.localRotation.eulerAngles.z },
                    scale = new[] { t.localScale.x, t.localScale.y, t.localScale.z },
                };
            }

            return d;
        }

        private static string GetGameObjectPath(GameObject go)
        {
            if (go == null) return string.Empty;
            try
            {
                var names = new Stack<string>();
                Transform t = go.transform;
                while (t != null)
                {
                    names.Push(t.name);
                    t = t.parent;
                }
                return string.Join("/", names);
            }
            catch
            {
                return go.name;
            }
        }

    }
}
