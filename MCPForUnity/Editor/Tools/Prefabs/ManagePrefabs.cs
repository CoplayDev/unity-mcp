using System;
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
        private const string SupportedActions = "open_stage, close_stage, save_open_stage, create_from_gameobject";

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
                AssetDatabase.Refresh();
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
