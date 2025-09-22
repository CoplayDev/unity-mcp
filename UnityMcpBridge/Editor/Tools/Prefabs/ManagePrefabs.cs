using System;
using MCPForUnity.Editor.Helpers;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace MCPForUnity.Editor.Tools.Prefabs
{
    public static class ManagePrefabs
    {
        private const string SupportedActions = "open_stage, close_stage, save_open_stage, apply_instance_overrides, revert_instance_overrides";

        public static object HandleCommand(JObject @params)
        {
            if (@params == null)
            {
                return Response.Error("Parameters cannot be null.");
            }

            string action = @params["action"]?.ToString()?.ToLowerInvariant();
            if (string.IsNullOrEmpty(action))
            {
                return Response.Error($"Action parameter is required. Valid actions are: {SupportedActions}.");
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
                    case "apply_instance_overrides":
                        return ApplyInstanceOverrides(@params);
                    case "revert_instance_overrides":
                        return RevertInstanceOverrides(@params);
                    default:
                        return Response.Error($"Unknown action: '{action}'. Valid actions are: {SupportedActions}.");
                }
            }
            catch (Exception e)
            {
                McpLog.Error($"[ManagePrefabs] Action '{action}' failed: {e}");
                return Response.Error($"Internal error: {e.Message}");
            }
        }

        private static object OpenStage(JObject @params)
        {
            string path = @params["path"]?.ToString();
            if (string.IsNullOrEmpty(path))
            {
                return Response.Error("'path' parameter is required for open_stage.");
            }

            string sanitizedPath = AssetPathUtility.SanitizeAssetPath(path);
            GameObject prefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(sanitizedPath);
            if (prefabAsset == null)
            {
                return Response.Error($"No prefab asset found at path '{sanitizedPath}'.");
            }

            string modeValue = @params["mode"]?.ToString();
            if (!string.IsNullOrEmpty(modeValue) && !modeValue.Equals(PrefabStage.Mode.InIsolation.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                return Response.Error("Only PrefabStage mode 'InIsolation' is supported at this time.");
            }

            PrefabStage stage = PrefabStageUtility.OpenPrefab(sanitizedPath);
            if (stage == null)
            {
                return Response.Error($"Failed to open prefab stage for '{sanitizedPath}'.");
            }

            return Response.Success($"Opened prefab stage for '{sanitizedPath}'.", SerializeStage(stage));
        }

        private static object CloseStage(JObject @params)
        {
            PrefabStage stage = PrefabStageUtility.GetCurrentPrefabStage();
            if (stage == null)
            {
                return Response.Success("No prefab stage was open.");
            }

            bool saveBeforeClose = @params["saveBeforeClose"]?.ToObject<bool>() ?? false;
            if (saveBeforeClose && stage.scene.isDirty)
            {
                SaveStagePrefab(stage);
                AssetDatabase.SaveAssets();
            }

            StageUtility.GoToMainStage();
            return Response.Success($"Closed prefab stage for '{stage.assetPath}'.");
        }

        private static object SaveOpenStage()
        {
            PrefabStage stage = PrefabStageUtility.GetCurrentPrefabStage();
            if (stage == null)
            {
                return Response.Error("No prefab stage is currently open.");
            }

            SaveStagePrefab(stage);
            AssetDatabase.SaveAssets();
            return Response.Success($"Saved prefab stage for '{stage.assetPath}'.", SerializeStage(stage));
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

        private static object ApplyInstanceOverrides(JObject @params)
        {
            if (!TryGetPrefabInstance(@params, out GameObject instanceRoot, out string error))
            {
                return Response.Error(error);
            }

            PrefabUtility.ApplyPrefabInstance(instanceRoot, InteractionMode.AutomatedAction);
            string prefabAssetPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(instanceRoot);

            return Response.Success(
                $"Applied overrides on prefab instance '{instanceRoot.name}'.",
                new
                {
                    prefabAssetPath,
                    instanceId = instanceRoot.GetInstanceID()
                }
            );
        }

        private static object RevertInstanceOverrides(JObject @params)
        {
            if (!TryGetPrefabInstance(@params, out GameObject instanceRoot, out string error))
            {
                return Response.Error(error);
            }

            PrefabUtility.RevertPrefabInstance(instanceRoot, InteractionMode.AutomatedAction);

            return Response.Success(
                $"Reverted overrides on prefab instance '{instanceRoot.name}'.",
                new
                {
                    instanceId = instanceRoot.GetInstanceID()
                }
            );
        }

        private static bool TryGetPrefabInstance(JObject @params, out GameObject instanceRoot, out string error)
        {
            instanceRoot = null;
            error = null;

            JToken instanceIdToken = @params["instanceId"] ?? @params["instanceID"];
            if (instanceIdToken != null && instanceIdToken.Type == JTokenType.Integer)
            {
                int instanceId = instanceIdToken.Value<int>();
                if (!TryResolveInstance(instanceId, out instanceRoot, out error))
                {
                    return false;
                }
                return true;
            }

            string targetName = @params["target"]?.ToString();
            if (!string.IsNullOrEmpty(targetName))
            {
                GameObject target = GameObject.Find(targetName);
                if (target == null)
                {
                    error = $"GameObject '{targetName}' not found in the current scene.";
                    return false;
                }

                instanceRoot = GetPrefabInstanceRoot(target, out error);
                return instanceRoot != null;
            }

            error = "Parameter 'instanceId' (or 'target') is required for this action.";
            return false;
        }

        private static bool TryResolveInstance(int instanceId, out GameObject instanceRoot, out string error)
        {
            instanceRoot = null;
            error = null;

            GameObject obj = EditorUtility.InstanceIDToObject(instanceId) as GameObject;
            if (obj == null)
            {
                error = $"No GameObject found for instanceId {instanceId}.";
                return false;
            }

            instanceRoot = GetPrefabInstanceRoot(obj, out error);
            return instanceRoot != null;
        }

        private static GameObject GetPrefabInstanceRoot(GameObject obj, out string error)
        {
            error = null;

            if (!PrefabUtility.IsPartOfPrefabInstance(obj))
            {
                error = $"GameObject '{obj.name}' is not part of a prefab instance.";
                return null;
            }

            GameObject root = PrefabUtility.GetOutermostPrefabInstanceRoot(obj);
            if (root == null)
            {
                error = $"Failed to resolve prefab instance root for '{obj.name}'.";
                return null;
            }

            PrefabInstanceStatus status = PrefabUtility.GetPrefabInstanceStatus(root);
            if (status == PrefabInstanceStatus.NotAPrefab)
            {
                error = $"GameObject '{obj.name}' is not recognised as a prefab instance.";
                return null;
            }

            return root;
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
