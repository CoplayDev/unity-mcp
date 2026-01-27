#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using MCPForUnity.Editor.Helpers;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditorInternal;
using UnityEngine;

namespace MCPForUnity.Editor.Tools.GameObjects
{
    internal static class GameObjectModify
    {
        internal static object Handle(JObject @params, JToken targetToken, string searchMethod)
        {
            // When setActive=true is specified, we need to search for inactive objects
            // otherwise we can't find an inactive object to activate it
            JObject findParams = null;
            if (@params["setActive"]?.ToObject<bool?>() == true)
            {
                findParams = new JObject { ["searchInactive"] = true };
            }
            
            GameObject targetGo = ManageGameObjectCommon.FindObjectInternal(targetToken, searchMethod, findParams);
            if (targetGo == null)
            {
                return new ErrorResponse($"Target GameObject ('{targetToken}') not found using method '{searchMethod ?? "default"}'.");
            }

            Undo.RecordObject(targetGo.transform, "Modify GameObject Transform");
            Undo.RecordObject(targetGo, "Modify GameObject Properties");

            bool modified = false;

            string name = @params["name"]?.ToString();
            if (!string.IsNullOrEmpty(name) && targetGo.name != name)
            {
                // Check if we're renaming the root object of an open prefab stage
                var prefabStageForRename = PrefabStageUtility.GetCurrentPrefabStage();
                bool isRenamingPrefabRoot = prefabStageForRename != null &&
                                            prefabStageForRename.prefabContentsRoot == targetGo;

                if (isRenamingPrefabRoot)
                {
                    // Rename the prefab asset file to match the new name (avoids Unity dialog)
                    string assetPath = prefabStageForRename.assetPath;
                    string directory = System.IO.Path.GetDirectoryName(assetPath);
                    string newAssetPath = AssetPathUtility.NormalizeSeparators(System.IO.Path.Combine(directory, name + ".prefab"));

                    // Only rename if the path actually changes
                    if (newAssetPath != assetPath)
                    {
                        // Check for collision using GUID comparison
                        string currentGuid = AssetDatabase.AssetPathToGUID(assetPath);
                        string existingGuid = AssetDatabase.AssetPathToGUID(newAssetPath);

                        // Collision only if there's a different asset at the new path
                        if (!string.IsNullOrEmpty(existingGuid) && existingGuid != currentGuid)
                        {
                            return new ErrorResponse($"Cannot rename prefab root to '{name}': a prefab already exists at '{newAssetPath}'.");
                        }

                        // Rename the asset file
                        string renameError = AssetDatabase.RenameAsset(assetPath, name);
                        if (!string.IsNullOrEmpty(renameError))
                        {
                            return new ErrorResponse($"Failed to rename prefab asset: {renameError}");
                        }

                        McpLog.Info($"[GameObjectModify] Renamed prefab asset from '{assetPath}' to '{newAssetPath}'");
                    }
                }

                targetGo.name = name;
                modified = true;
            }

            JToken parentToken = @params["parent"];
            if (parentToken != null)
            {
                GameObject newParentGo = ManageGameObjectCommon.FindObjectInternal(parentToken, "by_id_or_name_or_path");
                if (
                    newParentGo == null
                    && !(parentToken.Type == JTokenType.Null
                         || (parentToken.Type == JTokenType.String && string.IsNullOrEmpty(parentToken.ToString())))
                )
                {
                    return new ErrorResponse($"New parent ('{parentToken}') not found.");
                }
                if (newParentGo != null && newParentGo.transform.IsChildOf(targetGo.transform))
                {
                    return new ErrorResponse($"Cannot parent '{targetGo.name}' to '{newParentGo.name}', as it would create a hierarchy loop.");
                }
                if (targetGo.transform.parent != (newParentGo?.transform))
                {
                    targetGo.transform.SetParent(newParentGo?.transform, true);
                    modified = true;
                }
            }

            bool? setActive = @params["setActive"]?.ToObject<bool?>();
            if (setActive.HasValue && targetGo.activeSelf != setActive.Value)
            {
                targetGo.SetActive(setActive.Value);
                modified = true;
            }

            string tag = @params["tag"]?.ToString();
            if (tag != null && targetGo.tag != tag)
            {
                string tagToSet = string.IsNullOrEmpty(tag) ? "Untagged" : tag;

                if (tagToSet != "Untagged" && !System.Linq.Enumerable.Contains(InternalEditorUtility.tags, tagToSet))
                {
                    McpLog.Info($"[ManageGameObject] Tag '{tagToSet}' not found. Creating it.");
                    try
                    {
                        InternalEditorUtility.AddTag(tagToSet);
                    }
                    catch (Exception ex)
                    {
                        return new ErrorResponse($"Failed to create tag '{tagToSet}': {ex.Message}.");
                    }
                }

                try
                {
                    targetGo.tag = tagToSet;
                    modified = true;
                }
                catch (Exception ex)
                {
                    return new ErrorResponse($"Failed to set tag to '{tagToSet}': {ex.Message}.");
                }
            }

            string layerName = @params["layer"]?.ToString();
            if (!string.IsNullOrEmpty(layerName))
            {
                int layerId = LayerMask.NameToLayer(layerName);
                if (layerId == -1)
                {
                    return new ErrorResponse($"Invalid layer specified: '{layerName}'. Use a valid layer name.");
                }
                if (layerId != -1 && targetGo.layer != layerId)
                {
                    targetGo.layer = layerId;
                    modified = true;
                }
            }

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

            if (@params["componentsToRemove"] is JArray componentsToRemoveArray)
            {
                foreach (var compToken in componentsToRemoveArray)
                {
                    string typeName = compToken.ToString();
                    if (!string.IsNullOrEmpty(typeName))
                    {
                        var removeResult = GameObjectComponentHelpers.RemoveComponentInternal(targetGo, typeName);
                        if (removeResult != null)
                            return removeResult;
                        modified = true;
                    }
                }
            }

            if (@params["componentsToAdd"] is JArray componentsToAddArrayModify)
            {
                foreach (var compToken in componentsToAddArrayModify)
                {
                    string typeName = null;
                    JObject properties = null;
                    if (compToken.Type == JTokenType.String)
                        typeName = compToken.ToString();
                    else if (compToken is JObject compObj)
                    {
                        typeName = compObj["typeName"]?.ToString();
                        properties = compObj["properties"] as JObject;
                    }

                    if (!string.IsNullOrEmpty(typeName))
                    {
                        var addResult = GameObjectComponentHelpers.AddComponentInternal(targetGo, typeName, properties);
                        if (addResult != null)
                            return addResult;
                        modified = true;
                    }
                }
            }

            var componentErrors = new List<object>();
            if (@params["componentProperties"] is JObject componentPropertiesObj)
            {
                foreach (var prop in componentPropertiesObj.Properties())
                {
                    string compName = prop.Name;
                    JObject propertiesToSet = prop.Value as JObject;
                    if (propertiesToSet != null)
                    {
                        var setResult = GameObjectComponentHelpers.SetComponentPropertiesInternal(targetGo, compName, propertiesToSet);
                        if (setResult != null)
                        {
                            componentErrors.Add(setResult);
                        }
                        else
                        {
                            modified = true;
                        }
                    }
                }
            }

            if (componentErrors.Count > 0)
            {
                var aggregatedErrors = new List<string>();
                foreach (var errorObj in componentErrors)
                {
                    try
                    {
                        var dataProp = errorObj?.GetType().GetProperty("data");
                        var dataVal = dataProp?.GetValue(errorObj);
                        if (dataVal != null)
                        {
                            var errorsProp = dataVal.GetType().GetProperty("errors");
                            var errorsEnum = errorsProp?.GetValue(dataVal) as System.Collections.IEnumerable;
                            if (errorsEnum != null)
                            {
                                foreach (var item in errorsEnum)
                                {
                                    var s = item?.ToString();
                                    if (!string.IsNullOrEmpty(s)) aggregatedErrors.Add(s);
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        McpLog.Warn($"[GameObjectModify] Error aggregating component errors: {ex.Message}");
                    }
                }

                return new ErrorResponse(
                    $"One or more component property operations failed on '{targetGo.name}'.",
                    new { componentErrors = componentErrors, errors = aggregatedErrors }
                );
            }

            if (!modified)
            {
                return new SuccessResponse(
                    $"No modifications applied to GameObject '{targetGo.name}'.",
                    Helpers.GameObjectSerializer.GetGameObjectData(targetGo)
                );
            }

            EditorUtility.SetDirty(targetGo);

            // Mark the appropriate scene as dirty (handles both regular scenes and prefab stages)
            var prefabStage = PrefabStageUtility.GetCurrentPrefabStage();
            if (prefabStage != null)
            {
                EditorSceneManager.MarkSceneDirty(prefabStage.scene);
            }
            else
            {
                EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            }

            return new SuccessResponse(
                $"GameObject '{targetGo.name}' modified successfully.",
                Helpers.GameObjectSerializer.GetGameObjectData(targetGo)
            );
        }
    }
}
