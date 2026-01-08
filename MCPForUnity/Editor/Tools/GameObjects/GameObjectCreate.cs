#nullable disable
using System;
using System.Linq;
using MCPForUnity.Editor.Helpers;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditorInternal;
using UnityEngine;

namespace MCPForUnity.Editor.Tools.GameObjects
{
    internal static class GameObjectCreate
    {
        internal static object Handle(JObject @params)
        {
            string name = @params["name"]?.ToString();
            if (string.IsNullOrEmpty(name))
            {
                return new ErrorResponse("'name' parameter is required for 'create' action.");
            }

            // Get prefab creation parameters
            bool saveAsPrefab = @params["saveAsPrefab"]?.ToObject<bool>() ?? false;
            string prefabPath = @params["prefabPath"]?.ToString();
            string tag = @params["tag"]?.ToString();
            string primitiveType = @params["primitiveType"]?.ToString();
            GameObject newGo = null;

            // --- Try Instantiating Prefab First ---
            string originalPrefabPath = prefabPath;
            if (!saveAsPrefab && !string.IsNullOrEmpty(prefabPath))
            {
                string extension = System.IO.Path.GetExtension(prefabPath);

                if (!prefabPath.Contains("/") && (string.IsNullOrEmpty(extension) || extension.Equals(".prefab", StringComparison.OrdinalIgnoreCase)))
                {
                    string prefabNameOnly = prefabPath;
                    McpLog.Info($"[ManageGameObject.Create] Searching for prefab named: '{prefabNameOnly}'");
                    string[] guids = AssetDatabase.FindAssets($"t:Prefab {prefabNameOnly}");
                    if (guids.Length == 0)
                    {
                        return new ErrorResponse($"Prefab named '{prefabNameOnly}' not found anywhere in the project.");
                    }
                    else if (guids.Length > 1)
                    {
                        string foundPaths = string.Join(", ", guids.Select(g => AssetDatabase.GUIDToAssetPath(g)));
                        return new ErrorResponse($"Multiple prefabs found matching name '{prefabNameOnly}': {foundPaths}. Please provide a more specific path.");
                    }
                    else
                    {
                        prefabPath = AssetDatabase.GUIDToAssetPath(guids[0]);
                        McpLog.Info($"[ManageGameObject.Create] Found unique prefab at path: '{prefabPath}'");
                    }
                }
                else if (prefabPath.Contains("/") && string.IsNullOrEmpty(extension))
                {
                    McpLog.Warn($"[ManageGameObject.Create] Provided prefabPath '{prefabPath}' has no extension. Assuming it's a prefab and appending .prefab.");
                    prefabPath += ".prefab";
                }
                else if (!prefabPath.Contains("/") && !string.IsNullOrEmpty(extension) && !extension.Equals(".prefab", StringComparison.OrdinalIgnoreCase))
                {
                    string fileName = prefabPath;
                    string fileNameWithoutExtension = System.IO.Path.GetFileNameWithoutExtension(fileName);
                    McpLog.Info($"[ManageGameObject.Create] Searching for asset file named: '{fileName}'");

                    string[] guids = AssetDatabase.FindAssets(fileNameWithoutExtension);
                    var matches = guids
                        .Select(g => AssetDatabase.GUIDToAssetPath(g))
                        .Where(p => p.EndsWith("/" + fileName, StringComparison.OrdinalIgnoreCase) || p.Equals(fileName, StringComparison.OrdinalIgnoreCase))
                        .ToArray();

                    if (matches.Length == 0)
                    {
                        return new ErrorResponse($"Asset file '{fileName}' not found anywhere in the project.");
                    }
                    else if (matches.Length > 1)
                    {
                        string foundPaths = string.Join(", ", matches);
                        return new ErrorResponse($"Multiple assets found matching file name '{fileName}': {foundPaths}. Please provide a more specific path.");
                    }
                    else
                    {
                        prefabPath = matches[0];
                        McpLog.Info($"[ManageGameObject.Create] Found unique asset at path: '{prefabPath}'");
                    }
                }

                GameObject prefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                if (prefabAsset != null)
                {
                    try
                    {
                        newGo = PrefabUtility.InstantiatePrefab(prefabAsset) as GameObject;

                        if (newGo == null)
                        {
                            McpLog.Error($"[ManageGameObject.Create] Failed to instantiate prefab at '{prefabPath}', asset might be corrupted or not a GameObject.");
                            return new ErrorResponse($"Failed to instantiate prefab at '{prefabPath}'.");
                        }
                        if (!string.IsNullOrEmpty(name))
                        {
                            newGo.name = name;
                        }
                        Undo.RegisterCreatedObjectUndo(newGo, $"Instantiate Prefab '{prefabAsset.name}' as '{newGo.name}'");
                        McpLog.Info($"[ManageGameObject.Create] Instantiated prefab '{prefabAsset.name}' from path '{prefabPath}' as '{newGo.name}'.");
                    }
                    catch (Exception e)
                    {
                        return new ErrorResponse($"Error instantiating prefab '{prefabPath}': {e.Message}");
                    }
                }
                else
                {
                    return new ErrorResponse($"Asset not found or not a GameObject at path: '{prefabPath}'.");
                }
            }

            // --- Fallback: Create Primitive or Empty GameObject ---
            bool createdNewObject = false;
            if (newGo == null)
            {
                if (!string.IsNullOrEmpty(primitiveType))
                {
                    try
                    {
                        PrimitiveType type = (PrimitiveType)Enum.Parse(typeof(PrimitiveType), primitiveType, true);
                        newGo = GameObject.CreatePrimitive(type);
                        if (!string.IsNullOrEmpty(name))
                        {
                            newGo.name = name;
                        }
                        else
                        {
                            UnityEngine.Object.DestroyImmediate(newGo);
                            return new ErrorResponse("'name' parameter is required when creating a primitive.");
                        }
                        createdNewObject = true;
                    }
                    catch (ArgumentException)
                    {
                        return new ErrorResponse($"Invalid primitive type: '{primitiveType}'. Valid types: {string.Join(", ", Enum.GetNames(typeof(PrimitiveType)))}");
                    }
                    catch (Exception e)
                    {
                        return new ErrorResponse($"Failed to create primitive '{primitiveType}': {e.Message}");
                    }
                }
                else
                {
                    if (string.IsNullOrEmpty(name))
                    {
                        return new ErrorResponse("'name' parameter is required for 'create' action when not instantiating a prefab or creating a primitive.");
                    }
                    newGo = new GameObject(name);
                    createdNewObject = true;
                }

                if (createdNewObject)
                {
                    Undo.RegisterCreatedObjectUndo(newGo, $"Create GameObject '{newGo.name}'");
                }
            }

            if (newGo == null)
            {
                return new ErrorResponse("Failed to create or instantiate the GameObject.");
            }

            Undo.RecordObject(newGo.transform, "Set GameObject Transform");
            Undo.RecordObject(newGo, "Set GameObject Properties");

            // Set Parent
            JToken parentToken = @params["parent"];
            if (parentToken != null)
            {
                GameObject parentGo = ManageGameObjectCommon.FindObjectInternal(parentToken, "by_id_or_name_or_path");
                if (parentGo == null)
                {
                    UnityEngine.Object.DestroyImmediate(newGo);
                    return new ErrorResponse($"Parent specified ('{parentToken}') but not found.");
                }
                newGo.transform.SetParent(parentGo.transform, true);
            }

            // Set Transform
            Vector3? position = VectorParsing.ParseVector3(@params["position"]);
            Vector3? rotation = VectorParsing.ParseVector3(@params["rotation"]);
            Vector3? scale = VectorParsing.ParseVector3(@params["scale"]);

            if (position.HasValue) newGo.transform.localPosition = position.Value;
            if (rotation.HasValue) newGo.transform.localEulerAngles = rotation.Value;
            if (scale.HasValue) newGo.transform.localScale = scale.Value;

            // Set Tag
            if (!string.IsNullOrEmpty(tag))
            {
                if (tag != "Untagged" && !System.Linq.Enumerable.Contains(InternalEditorUtility.tags, tag))
                {
                    McpLog.Info($"[ManageGameObject.Create] Tag '{tag}' not found. Creating it.");
                    try
                    {
                        InternalEditorUtility.AddTag(tag);
                    }
                    catch (Exception ex)
                    {
                        UnityEngine.Object.DestroyImmediate(newGo);
                        return new ErrorResponse($"Failed to create tag '{tag}': {ex.Message}.");
                    }
                }

                try
                {
                    newGo.tag = tag;
                }
                catch (Exception ex)
                {
                    UnityEngine.Object.DestroyImmediate(newGo);
                    return new ErrorResponse($"Failed to set tag to '{tag}' during creation: {ex.Message}.");
                }
            }

            // Set Layer
            string layerName = @params["layer"]?.ToString();
            if (!string.IsNullOrEmpty(layerName))
            {
                int layerId = LayerMask.NameToLayer(layerName);
                if (layerId != -1)
                {
                    newGo.layer = layerId;
                }
                else
                {
                    McpLog.Warn($"[ManageGameObject.Create] Layer '{layerName}' not found. Using default layer.");
                }
            }

            // Add Components
            if (@params["componentsToAdd"] is JArray componentsToAddArray)
            {
                foreach (var compToken in componentsToAddArray)
                {
                    string typeName = null;
                    JObject properties = null;

                    if (compToken.Type == JTokenType.String)
                    {
                        typeName = compToken.ToString();
                    }
                    else if (compToken is JObject compObj)
                    {
                        typeName = compObj["typeName"]?.ToString();
                        properties = compObj["properties"] as JObject;
                    }

                    if (!string.IsNullOrEmpty(typeName))
                    {
                        var addResult = GameObjectComponentHelpers.AddComponentInternal(newGo, typeName, properties);
                        if (addResult != null)
                        {
                            UnityEngine.Object.DestroyImmediate(newGo);
                            return addResult;
                        }
                    }
                    else
                    {
                        McpLog.Warn($"[ManageGameObject] Invalid component format in componentsToAdd: {compToken}");
                    }
                }
            }

            // Save as Prefab ONLY if we *created* a new object AND saveAsPrefab is true
            GameObject finalInstance = newGo;
            if (createdNewObject && saveAsPrefab)
            {
                string finalPrefabPath = prefabPath;
                if (string.IsNullOrEmpty(finalPrefabPath))
                {
                    UnityEngine.Object.DestroyImmediate(newGo);
                    return new ErrorResponse("'prefabPath' is required when 'saveAsPrefab' is true and creating a new object.");
                }
                if (!finalPrefabPath.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
                {
                    McpLog.Info($"[ManageGameObject.Create] Appending .prefab extension to save path: '{finalPrefabPath}' -> '{finalPrefabPath}.prefab'");
                    finalPrefabPath += ".prefab";
                }

                try
                {
                    string directoryPath = System.IO.Path.GetDirectoryName(finalPrefabPath);
                    if (!string.IsNullOrEmpty(directoryPath) && !System.IO.Directory.Exists(directoryPath))
                    {
                        System.IO.Directory.CreateDirectory(directoryPath);
                        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                        McpLog.Info($"[ManageGameObject.Create] Created directory for prefab: {directoryPath}");
                    }

                    finalInstance = PrefabUtility.SaveAsPrefabAssetAndConnect(newGo, finalPrefabPath, InteractionMode.UserAction);

                    if (finalInstance == null)
                    {
                        UnityEngine.Object.DestroyImmediate(newGo);
                        return new ErrorResponse($"Failed to save GameObject '{name}' as prefab at '{finalPrefabPath}'. Check path and permissions.");
                    }
                    McpLog.Info($"[ManageGameObject.Create] GameObject '{name}' saved as prefab to '{finalPrefabPath}' and instance connected.");
                }
                catch (Exception e)
                {
                    UnityEngine.Object.DestroyImmediate(newGo);
                    return new ErrorResponse($"Error saving prefab '{finalPrefabPath}': {e.Message}");
                }
            }

            Selection.activeGameObject = finalInstance;

            string messagePrefabPath =
                finalInstance == null
                    ? originalPrefabPath
                    : AssetDatabase.GetAssetPath(PrefabUtility.GetCorrespondingObjectFromSource(finalInstance) ?? (UnityEngine.Object)finalInstance);

            string successMessage;
            if (!createdNewObject && !string.IsNullOrEmpty(messagePrefabPath))
            {
                successMessage = $"Prefab '{messagePrefabPath}' instantiated successfully as '{finalInstance.name}'.";
            }
            else if (createdNewObject && saveAsPrefab && !string.IsNullOrEmpty(messagePrefabPath))
            {
                successMessage = $"GameObject '{finalInstance.name}' created and saved as prefab to '{messagePrefabPath}'.";
            }
            else
            {
                successMessage = $"GameObject '{finalInstance.name}' created successfully in scene.";
            }

            return new SuccessResponse(successMessage, Helpers.GameObjectSerializer.GetGameObjectData(finalInstance));
        }
    }
}
