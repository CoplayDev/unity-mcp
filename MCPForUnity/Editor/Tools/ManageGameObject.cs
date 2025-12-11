#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MCPForUnity.Editor.Helpers; // For Response class
using MCPForUnity.Runtime.Serialization;
using Newtonsoft.Json; // Added for JsonSerializationException
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.Compilation; // For CompilationPipeline
using UnityEditor.SceneManagement;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MCPForUnity.Editor.Tools
{
    /// <summary>
    /// Handles GameObject manipulation within the current scene (CRUD, find, components).
    /// </summary>
    [McpForUnityTool("manage_gameobject", AutoRegister = false)]
    public static class ManageGameObject
    {
        // Shared JsonSerializer to avoid per-call allocation overhead
        internal static readonly JsonSerializer InputSerializer = JsonSerializer.Create(new JsonSerializerSettings
        {
            Converters = new List<JsonConverter>
            {
                new Vector3Converter(),
                new Vector2Converter(),
                new QuaternionConverter(),
                new ColorConverter(),
                new RectConverter(),
                new BoundsConverter(),
                new UnityEngineObjectConverter()
            }
        });

        // --- Main Handler ---

        public static object HandleCommand(JObject @params)
        {
            if (@params == null)
            {
                return new ErrorResponse("Parameters cannot be null.");
            }

            string action = @params["action"]?.ToString().ToLower();
            if (string.IsNullOrEmpty(action))
            {
                return new ErrorResponse("Action parameter is required.");
            }

            // Parameters used by various actions
            JToken targetToken = @params["target"]; // Can be string (name/path) or int (instanceID)
            string name = @params["name"]?.ToString();

            // --- Usability Improvement: Alias 'name' to 'target' for modification actions ---
            // If 'target' is missing but 'name' is provided, and we aren't creating a new object,
            // assume the user meant "find object by name".
            if (targetToken == null && !string.IsNullOrEmpty(name) && action != "create")
            {
                targetToken = name;
                // We don't update @params["target"] because we use targetToken locally mostly,
                // but some downstream methods might parse @params directly. Let's update @params too for safety.
                @params["target"] = name;
            }
            // -------------------------------------------------------------------------------

            string searchMethod = @params["searchMethod"]?.ToString().ToLower();
            string tag = @params["tag"]?.ToString();
            string layer = @params["layer"]?.ToString();
            JToken parentToken = @params["parent"];

            // --- Add parameter for controlling non-public field inclusion ---
            bool includeNonPublicSerialized = @params["includeNonPublicSerialized"]?.ToObject<bool>() ?? true; // Default to true
            // --- End add parameter ---

            // Coerce string JSON to JObject for 'componentProperties' if provided as a JSON string
            var componentPropsToken = @params["componentProperties"];
            if (componentPropsToken != null && componentPropsToken.Type == JTokenType.String)
            {
                try
                {
                    var parsed = JObject.Parse(componentPropsToken.ToString());
                    @params["componentProperties"] = parsed;
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[ManageGameObject] Could not parse 'componentProperties' JSON string: {e.Message}");
                }
            }

            // --- Prefab Redirection Check ---
            string targetPath =
                targetToken?.Type == JTokenType.String ? targetToken.ToString() : null;
            if (
                !string.IsNullOrEmpty(targetPath)
                && targetPath.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase)
            )
            {
                // Allow 'create' (instantiate), 'find' (?), 'get_components' (?)
                if (action == "modify" || action == "set_component_property")
                {
                    Debug.Log(
                        $"[ManageGameObject->ManageAsset] Redirecting action '{action}' for prefab '{targetPath}' to ManageAsset."
                    );
                    // Prepare params for ManageAsset.ModifyAsset
                    JObject assetParams = new JObject();
                    assetParams["action"] = "modify"; // ManageAsset uses "modify"
                    assetParams["path"] = targetPath;

                    // Extract properties.
                    // For 'set_component_property', combine componentName and componentProperties.
                    // For 'modify', directly use componentProperties.
                    JObject properties = null;
                    if (action == "set_component_property")
                    {
                        string compName = @params["componentName"]?.ToString();
                        JObject compProps = @params["componentProperties"]?[compName] as JObject; // Handle potential nesting
                        if (string.IsNullOrEmpty(compName))
                            return new ErrorResponse(
                                "Missing 'componentName' for 'set_component_property' on prefab."
                            );
                        if (compProps == null)
                            return new ErrorResponse(
                                $"Missing or invalid 'componentProperties' for component '{compName}' for 'set_component_property' on prefab."
                            );

                        properties = new JObject();
                        properties[compName] = compProps;
                    }
                    else // action == "modify"
                    {
                        properties = @params["componentProperties"] as JObject;
                        if (properties == null)
                            return new ErrorResponse(
                                "Missing 'componentProperties' for 'modify' action on prefab."
                            );
                    }

                    assetParams["properties"] = properties;

                    // Call ManageAsset handler
                    return ManageAsset.HandleCommand(assetParams);
                }
                else if (
                    action == "delete"
                    || action == "add_component"
                    || action == "remove_component"
                    || action == "get_components"
                ) // Added get_components here too
                {
                    // Explicitly block other modifications on the prefab asset itself via manage_gameobject
                    return new ErrorResponse(
                        $"Action '{action}' on a prefab asset ('{targetPath}') should be performed using the 'manage_asset' command."
                    );
                }
                // Allow 'create' (instantiation) and 'find' to proceed, although finding a prefab asset by path might be less common via manage_gameobject.
                // No specific handling needed here, the code below will run.
            }
            // --- End Prefab Redirection Check ---

            try
            {
                switch (action)
                {
                    case "create":
                        return CreateGameObject(@params);
                    case "modify":
                        return ModifyGameObject(@params, targetToken, searchMethod);
                    case "delete":
                        return DeleteGameObject(targetToken, searchMethod);
                    case "find":
                        return FindGameObjects(@params, targetToken, searchMethod);
                    case "get_components":
                        string getCompTarget = targetToken?.ToString(); // Expect name, path, or ID string
                        if (getCompTarget == null)
                            return new ErrorResponse(
                                "'target' parameter required for get_components."
                            );
                        // Pass the includeNonPublicSerialized flag here
                        return GetComponentsFromTarget(getCompTarget, searchMethod, includeNonPublicSerialized);
                    case "get_component":
                        string getSingleCompTarget = targetToken?.ToString();
                        if (getSingleCompTarget == null)
                            return new ErrorResponse(
                                "'target' parameter required for get_component."
                            );
                        string componentName = @params["componentName"]?.ToString();
                        if (string.IsNullOrEmpty(componentName))
                            return new ErrorResponse(
                                "'componentName' parameter required for get_component."
                            );
                        return GetSingleComponentFromTarget(getSingleCompTarget, searchMethod, componentName, includeNonPublicSerialized);
                    case "add_component":
                        return AddComponentToTarget(@params, targetToken, searchMethod);
                    case "remove_component":
                        return RemoveComponentFromTarget(@params, targetToken, searchMethod);
                    case "set_component_property":
                        return SetComponentPropertyOnTarget(@params, targetToken, searchMethod);
                    case "duplicate":
                        return DuplicateGameObject(@params, targetToken, searchMethod);
                    case "move_relative":
                        return MoveRelativeToObject(@params, targetToken, searchMethod);

                    default:
                        return new ErrorResponse($"Unknown action: '{action}'.");
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[ManageGameObject] Action '{action}' failed: {e}");
                return new ErrorResponse($"Internal error processing action '{action}': {e.Message}");
            }
        }

        // --- Action Implementations ---

        private static object CreateGameObject(JObject @params)
        {
            string name = @params["name"]?.ToString();
            if (string.IsNullOrEmpty(name))
            {
                return new ErrorResponse("'name' parameter is required for 'create' action.");
            }

            // Get prefab creation parameters
            bool saveAsPrefab = @params["saveAsPrefab"]?.ToObject<bool>() ?? false;
            string prefabPath = @params["prefabPath"]?.ToString();
            string tag = @params["tag"]?.ToString(); // Get tag for creation
            string primitiveType = @params["primitiveType"]?.ToString(); // Keep primitiveType check
            GameObject newGo = null; // Initialize as null

            // --- Try Instantiating Prefab First ---
            string originalPrefabPath = prefabPath; // Keep original for messages
            if (!string.IsNullOrEmpty(prefabPath))
            {
                // If no extension, search for the prefab by name
                if (
                    !prefabPath.Contains("/")
                    && !prefabPath.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase)
                )
                {
                    string prefabNameOnly = prefabPath;
                    Debug.Log(
                        $"[ManageGameObject.Create] Searching for prefab named: '{prefabNameOnly}'"
                    );
                    string[] guids = AssetDatabase.FindAssets($"t:Prefab {prefabNameOnly}");
                    if (guids.Length == 0)
                    {
                        return new ErrorResponse(
                            $"Prefab named '{prefabNameOnly}' not found anywhere in the project."
                        );
                    }
                    else if (guids.Length > 1)
                    {
                        string foundPaths = string.Join(
                            ", ",
                            guids.Select(g => AssetDatabase.GUIDToAssetPath(g))
                        );
                        return new ErrorResponse(
                            $"Multiple prefabs found matching name '{prefabNameOnly}': {foundPaths}. Please provide a more specific path."
                        );
                    }
                    else // Exactly one found
                    {
                        prefabPath = AssetDatabase.GUIDToAssetPath(guids[0]); // Update prefabPath with the full path
                        Debug.Log(
                            $"[ManageGameObject.Create] Found unique prefab at path: '{prefabPath}'"
                        );
                    }
                }
                else if (!prefabPath.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
                {
                    // If it looks like a path but doesn't end with .prefab, assume user forgot it and append it.
                    Debug.LogWarning(
                        $"[ManageGameObject.Create] Provided prefabPath '{prefabPath}' does not end with .prefab. Assuming it's missing and appending."
                    );
                    prefabPath += ".prefab";
                    // Note: This path might still not exist, AssetDatabase.LoadAssetAtPath will handle that.
                }
                // The logic above now handles finding or assuming the .prefab extension.

                GameObject prefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                if (prefabAsset != null)
                {
                    try
                    {
                        // Instantiate the prefab, initially place it at the root
                        // Parent will be set later if specified
                        newGo = PrefabUtility.InstantiatePrefab(prefabAsset) as GameObject;

                        if (newGo == null)
                        {
                            // This might happen if the asset exists but isn't a valid GameObject prefab somehow
                            Debug.LogError(
                                $"[ManageGameObject.Create] Failed to instantiate prefab at '{prefabPath}', asset might be corrupted or not a GameObject."
                            );
                            return new ErrorResponse(
                                $"Failed to instantiate prefab at '{prefabPath}'."
                            );
                        }
                        // Name the instance based on the 'name' parameter, not the prefab's default name
                        if (!string.IsNullOrEmpty(name))
                        {
                            newGo.name = name;
                        }
                        // Register Undo for prefab instantiation
                        Undo.RegisterCreatedObjectUndo(
                            newGo,
                            $"Instantiate Prefab '{prefabAsset.name}' as '{newGo.name}'"
                        );
                        Debug.Log(
                            $"[ManageGameObject.Create] Instantiated prefab '{prefabAsset.name}' from path '{prefabPath}' as '{newGo.name}'."
                        );
                    }
                    catch (Exception e)
                    {
                        return new ErrorResponse(
                            $"Error instantiating prefab '{prefabPath}': {e.Message}"
                        );
                    }
                }
                else
                {
                    // Only return error if prefabPath was specified but not found.
                    // If prefabPath was empty/null, we proceed to create primitive/empty.
                    Debug.LogWarning(
                        $"[ManageGameObject.Create] Prefab asset not found at path: '{prefabPath}'. Will proceed to create new object if specified."
                    );
                    // Do not return error here, allow fallback to primitive/empty creation
                }
            }

            // --- Fallback: Create Primitive or Empty GameObject ---
            bool createdNewObject = false; // Flag to track if we created (not instantiated)
            if (newGo == null) // Only proceed if prefab instantiation didn't happen
            {
                if (!string.IsNullOrEmpty(primitiveType))
                {
                    try
                    {
                        PrimitiveType type = (PrimitiveType)
                            Enum.Parse(typeof(PrimitiveType), primitiveType, true);
                        newGo = GameObject.CreatePrimitive(type);
                        // Set name *after* creation for primitives
                        if (!string.IsNullOrEmpty(name))
                        {
                            newGo.name = name;
                        }
                        else
                        {
                            UnityEngine.Object.DestroyImmediate(newGo); // cleanup leak
                            return new ErrorResponse(
                                "'name' parameter is required when creating a primitive."
                            );
                        }
                        createdNewObject = true;
                    }
                    catch (ArgumentException)
                    {
                        return new ErrorResponse(
                            $"Invalid primitive type: '{primitiveType}'. Valid types: {string.Join(", ", Enum.GetNames(typeof(PrimitiveType)))}"
                        );
                    }
                    catch (Exception e)
                    {
                        return new ErrorResponse(
                            $"Failed to create primitive '{primitiveType}': {e.Message}"
                        );
                    }
                }
                else // Create empty GameObject
                {
                    if (string.IsNullOrEmpty(name))
                    {
                        return new ErrorResponse(
                            "'name' parameter is required for 'create' action when not instantiating a prefab or creating a primitive."
                        );
                    }
                    newGo = new GameObject(name);
                    createdNewObject = true;
                }
                // Record creation for Undo *only* if we created a new object
                if (createdNewObject)
                {
                    Undo.RegisterCreatedObjectUndo(newGo, $"Create GameObject '{newGo.name}'");
                }
            }
            // --- Common Setup (Parent, Transform, Tag, Components) - Applied AFTER object exists ---
            if (newGo == null)
            {
                // Should theoretically not happen if logic above is correct, but safety check.
                return new ErrorResponse("Failed to create or instantiate the GameObject.");
            }

            // Record potential changes to the existing prefab instance or the new GO
            // Record transform separately in case parent changes affect it
            Undo.RecordObject(newGo.transform, "Set GameObject Transform");
            Undo.RecordObject(newGo, "Set GameObject Properties");

            // Set Parent
            JToken parentToken = @params["parent"];
            if (parentToken != null)
            {
                GameObject parentGo = FindObjectInternal(parentToken, "by_id_or_name_or_path"); // Flexible parent finding
                if (parentGo == null)
                {
                    UnityEngine.Object.DestroyImmediate(newGo); // Clean up created object
                    return new ErrorResponse($"Parent specified ('{parentToken}') but not found.");
                }
                newGo.transform.SetParent(parentGo.transform, true); // worldPositionStays = true
            }

            // Set Transform
            Vector3? position = ParseVector3(@params["position"] as JArray);
            Vector3? rotation = ParseVector3(@params["rotation"] as JArray);
            Vector3? scale = ParseVector3(@params["scale"] as JArray);

            if (position.HasValue)
                newGo.transform.localPosition = position.Value;
            if (rotation.HasValue)
                newGo.transform.localEulerAngles = rotation.Value;
            if (scale.HasValue)
                newGo.transform.localScale = scale.Value;

            // Set Tag (added for create action)
            if (!string.IsNullOrEmpty(tag))
            {
                // Similar logic as in ModifyGameObject for setting/creating tags
                string tagToSet = string.IsNullOrEmpty(tag) ? "Untagged" : tag;
                try
                {
                    newGo.tag = tagToSet;
                }
                catch (UnityException ex)
                {
                    if (ex.Message.Contains("is not defined"))
                    {
                        Debug.LogWarning(
                            $"[ManageGameObject.Create] Tag '{tagToSet}' not found. Attempting to create it."
                        );
                        try
                        {
                            InternalEditorUtility.AddTag(tagToSet);
                            newGo.tag = tagToSet; // Retry
                            Debug.Log(
                                $"[ManageGameObject.Create] Tag '{tagToSet}' created and assigned successfully."
                            );
                        }
                        catch (Exception innerEx)
                        {
                            UnityEngine.Object.DestroyImmediate(newGo); // Clean up
                            return new ErrorResponse(
                                $"Failed to create or assign tag '{tagToSet}' during creation: {innerEx.Message}."
                            );
                        }
                    }
                    else
                    {
                        UnityEngine.Object.DestroyImmediate(newGo); // Clean up
                        return new ErrorResponse(
                            $"Failed to set tag to '{tagToSet}' during creation: {ex.Message}."
                        );
                    }
                }
            }

            // Set Layer (new for create action)
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
                    Debug.LogWarning(
                        $"[ManageGameObject.Create] Layer '{layerName}' not found. Using default layer."
                    );
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
                        var addResult = AddComponentInternal(newGo, typeName, properties);
                        if (addResult != null) // Check if AddComponentInternal returned an error object
                        {
                            UnityEngine.Object.DestroyImmediate(newGo); // Clean up
                            return addResult; // Return the error response
                        }
                    }
                    else
                    {
                        Debug.LogWarning(
                            $"[ManageGameObject] Invalid component format in componentsToAdd: {compToken}"
                        );
                    }
                }
            }

            // Save as Prefab ONLY if we *created* a new object AND saveAsPrefab is true
            GameObject finalInstance = newGo; // Use this for selection and return data
            if (createdNewObject && saveAsPrefab)
            {
                string finalPrefabPath = prefabPath; // Use a separate variable for saving path
                // This check should now happen *before* attempting to save
                if (string.IsNullOrEmpty(finalPrefabPath))
                {
                    // Clean up the created object before returning error
                    UnityEngine.Object.DestroyImmediate(newGo);
                    return new ErrorResponse(
                        "'prefabPath' is required when 'saveAsPrefab' is true and creating a new object."
                    );
                }
                // Ensure the *saving* path ends with .prefab
                if (!finalPrefabPath.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
                {
                    Debug.Log(
                        $"[ManageGameObject.Create] Appending .prefab extension to save path: '{finalPrefabPath}' -> '{finalPrefabPath}.prefab'"
                    );
                    finalPrefabPath += ".prefab";
                }

                try
                {
                    // Ensure directory exists using the final saving path
                    string directoryPath = System.IO.Path.GetDirectoryName(finalPrefabPath);
                    if (
                        !string.IsNullOrEmpty(directoryPath)
                        && !System.IO.Directory.Exists(directoryPath)
                    )
                    {
                        System.IO.Directory.CreateDirectory(directoryPath);
                        AssetDatabase.Refresh(); // Refresh asset database to recognize the new folder
                        Debug.Log(
                            $"[ManageGameObject.Create] Created directory for prefab: {directoryPath}"
                        );
                    }
                    // Use SaveAsPrefabAssetAndConnect with the final saving path
                    finalInstance = PrefabUtility.SaveAsPrefabAssetAndConnect(
                        newGo,
                        finalPrefabPath,
                        InteractionMode.UserAction
                    );

                    if (finalInstance == null)
                    {
                        // Destroy the original if saving failed somehow (shouldn't usually happen if path is valid)
                        UnityEngine.Object.DestroyImmediate(newGo);
                        return new ErrorResponse(
                            $"Failed to save GameObject '{name}' as prefab at '{finalPrefabPath}'. Check path and permissions."
                        );
                    }
                    Debug.Log(
                        $"[ManageGameObject.Create] GameObject '{name}' saved as prefab to '{finalPrefabPath}' and instance connected."
                    );
                    // Mark the new prefab asset as dirty? Not usually necessary, SaveAsPrefabAsset handles it.
                    // EditorUtility.SetDirty(finalInstance); // Instance is handled by SaveAsPrefabAssetAndConnect
                }
                catch (Exception e)
                {
                    // Clean up the instance if prefab saving fails
                    UnityEngine.Object.DestroyImmediate(newGo); // Destroy the original attempt
                    return new ErrorResponse($"Error saving prefab '{finalPrefabPath}': {e.Message}");
                }
            }

            // Select the instance in the scene (either prefab instance or newly created/saved one)
            Selection.activeGameObject = finalInstance;

            // Determine appropriate success message using the potentially updated or original path
            string messagePrefabPath =
                finalInstance == null
                    ? originalPrefabPath
                    : AssetDatabase.GetAssetPath(
                        PrefabUtility.GetCorrespondingObjectFromSource(finalInstance)
                            ?? (UnityEngine.Object)finalInstance
                    );
            string successMessage;
            if (!createdNewObject && !string.IsNullOrEmpty(messagePrefabPath)) // Instantiated existing prefab
            {
                successMessage =
                    $"Prefab '{messagePrefabPath}' instantiated successfully as '{finalInstance.name}'.";
            }
            else if (createdNewObject && saveAsPrefab && !string.IsNullOrEmpty(messagePrefabPath)) // Created new and saved as prefab
            {
                successMessage =
                    $"GameObject '{finalInstance.name}' created and saved as prefab to '{messagePrefabPath}'.";
            }
            else // Created new primitive or empty GO, didn't save as prefab
            {
                successMessage =
                    $"GameObject '{finalInstance.name}' created successfully in scene.";
            }

            // Use the new serializer helper
            //return new SuccessResponse(successMessage, GetGameObjectData(finalInstance));
            return new SuccessResponse(successMessage, Helpers.GameObjectSerializer.GetGameObjectData(finalInstance));
        }

        private static object ModifyGameObject(
            JObject @params,
            JToken targetToken,
            string searchMethod
        )
        {
            GameObject targetGo = FindObjectInternal(targetToken, searchMethod);
            if (targetGo == null)
            {
                return new ErrorResponse(
                    $"Target GameObject ('{targetToken}') not found using method '{searchMethod ?? "default"}'."
                );
            }

            // Record state for Undo *before* modifications
            Undo.RecordObject(targetGo.transform, "Modify GameObject Transform");
            Undo.RecordObject(targetGo, "Modify GameObject Properties");

            bool modified = false;

            // Rename (using consolidated 'name' parameter)
            string name = @params["name"]?.ToString();
            if (!string.IsNullOrEmpty(name) && targetGo.name != name)
            {
                targetGo.name = name;
                modified = true;
            }

            // Change Parent (using consolidated 'parent' parameter)
            JToken parentToken = @params["parent"];
            if (parentToken != null)
            {
                GameObject newParentGo = FindObjectInternal(parentToken, "by_id_or_name_or_path");
                // Check for hierarchy loops
                if (
                    newParentGo == null
                    && !(
                        parentToken.Type == JTokenType.Null
                        || (
                            parentToken.Type == JTokenType.String
                            && string.IsNullOrEmpty(parentToken.ToString())
                        )
                    )
                )
                {
                    return new ErrorResponse($"New parent ('{parentToken}') not found.");
                }
                if (newParentGo != null && newParentGo.transform.IsChildOf(targetGo.transform))
                {
                    return new ErrorResponse(
                        $"Cannot parent '{targetGo.name}' to '{newParentGo.name}', as it would create a hierarchy loop."
                    );
                }
                if (targetGo.transform.parent != (newParentGo?.transform))
                {
                    targetGo.transform.SetParent(newParentGo?.transform, true); // worldPositionStays = true
                    modified = true;
                }
            }

            // Set Active State
            bool? setActive = @params["setActive"]?.ToObject<bool?>();
            if (setActive.HasValue && targetGo.activeSelf != setActive.Value)
            {
                targetGo.SetActive(setActive.Value);
                modified = true;
            }

            // Change Tag (using consolidated 'tag' parameter)
            string tag = @params["tag"]?.ToString();
            // Only attempt to change tag if a non-null tag is provided and it's different from the current one.
            // Allow setting an empty string to remove the tag (Unity uses "Untagged").
            if (tag != null && targetGo.tag != tag)
            {
                // Ensure the tag is not empty, if empty, it means "Untagged" implicitly
                string tagToSet = string.IsNullOrEmpty(tag) ? "Untagged" : tag;
                try
                {
                    targetGo.tag = tagToSet;
                    modified = true;
                }
                catch (UnityException ex)
                {
                    // Check if the error is specifically because the tag doesn't exist
                    if (ex.Message.Contains("is not defined"))
                    {
                        Debug.LogWarning(
                            $"[ManageGameObject] Tag '{tagToSet}' not found. Attempting to create it."
                        );
                        try
                        {
                            // Attempt to create the tag using internal utility
                            InternalEditorUtility.AddTag(tagToSet);
                            // Wait a frame maybe? Not strictly necessary but sometimes helps editor updates.
                            // yield return null; // Cannot yield here, editor script limitation

                            // Retry setting the tag immediately after creation
                            targetGo.tag = tagToSet;
                            modified = true;
                            Debug.Log(
                                $"[ManageGameObject] Tag '{tagToSet}' created and assigned successfully."
                            );
                        }
                        catch (Exception innerEx)
                        {
                            // Handle failure during tag creation or the second assignment attempt
                            Debug.LogError(
                                $"[ManageGameObject] Failed to create or assign tag '{tagToSet}' after attempting creation: {innerEx.Message}"
                            );
                            return new ErrorResponse(
                                $"Failed to create or assign tag '{tagToSet}': {innerEx.Message}. Check Tag Manager and permissions."
                            );
                        }
                    }
                    else
                    {
                        // If the exception was for a different reason, return the original error
                        return new ErrorResponse($"Failed to set tag to '{tagToSet}': {ex.Message}.");
                    }
                }
            }

            // Change Layer (using consolidated 'layer' parameter)
            string layerName = @params["layer"]?.ToString();
            if (!string.IsNullOrEmpty(layerName))
            {
                int layerId = LayerMask.NameToLayer(layerName);
                if (layerId == -1 && layerName != "Default")
                {
                    return new ErrorResponse(
                        $"Invalid layer specified: '{layerName}'. Use a valid layer name."
                    );
                }
                if (layerId != -1 && targetGo.layer != layerId)
                {
                    targetGo.layer = layerId;
                    modified = true;
                }
            }

            // Transform Modifications
            Vector3? position = ParseVector3(@params["position"] as JArray);
            Vector3? rotation = ParseVector3(@params["rotation"] as JArray);
            Vector3? scale = ParseVector3(@params["scale"] as JArray);

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

            // --- Component Modifications ---
            // Note: These might need more specific Undo recording per component

            // Remove Components
            if (@params["componentsToRemove"] is JArray componentsToRemoveArray)
            {
                foreach (var compToken in componentsToRemoveArray)
                {
                    // ... (parsing logic as in CreateGameObject) ...
                    string typeName = compToken.ToString();
                    if (!string.IsNullOrEmpty(typeName))
                    {
                        var removeResult = RemoveComponentInternal(targetGo, typeName);
                        if (removeResult != null)
                            return removeResult; // Return error if removal failed
                        modified = true;
                    }
                }
            }

            // Add Components (similar to create)
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
                        var addResult = AddComponentInternal(targetGo, typeName, properties);
                        if (addResult != null)
                            return addResult;
                        modified = true;
                    }
                }
            }

            // Set Component Properties
            var componentErrors = new List<object>();
            if (@params["componentProperties"] is JObject componentPropertiesObj)
            {
                foreach (var prop in componentPropertiesObj.Properties())
                {
                    string compName = prop.Name;
                    JObject propertiesToSet = prop.Value as JObject;
                    if (propertiesToSet != null)
                    {
                        var setResult = SetComponentPropertiesInternal(
                            targetGo,
                            compName,
                            propertiesToSet
                        );
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

            // Return component errors if any occurred (after processing all components)
            if (componentErrors.Count > 0)
            {
                // Aggregate flattened error strings to make tests/API assertions simpler
                var aggregatedErrors = new System.Collections.Generic.List<string>();
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
                    catch { }
                }

                return new ErrorResponse(
                    $"One or more component property operations failed on '{targetGo.name}'.",
                    new { componentErrors = componentErrors, errors = aggregatedErrors }
                );
            }

            if (!modified)
            {
                // Use the new serializer helper
                // return new SuccessResponse(
                //     $"No modifications applied to GameObject '{targetGo.name}'.",
                //     GetGameObjectData(targetGo));

                return new SuccessResponse(
                    $"No modifications applied to GameObject '{targetGo.name}'.",
                    Helpers.GameObjectSerializer.GetGameObjectData(targetGo)
                );
            }

            EditorUtility.SetDirty(targetGo); // Mark scene as dirty
            // Use the new serializer helper
            return new SuccessResponse(
                $"GameObject '{targetGo.name}' modified successfully.",
                Helpers.GameObjectSerializer.GetGameObjectData(targetGo)
            );
            // return new SuccessResponse(
            //     $"GameObject '{targetGo.name}' modified successfully.",
            //     GetGameObjectData(targetGo));

        }

        /// <summary>
        /// Duplicates a GameObject with all its properties, components, and children.
        /// </summary>
        private static object DuplicateGameObject(JObject @params, JToken targetToken, string searchMethod)
        {
            GameObject sourceGo = FindObjectInternal(targetToken, searchMethod);
            if (sourceGo == null)
            {
                return new ErrorResponse(
                    $"Target GameObject ('{targetToken}') not found using method '{searchMethod ?? "default"}'."
                );
            }

            // Optional parameters
            string newName = @params["new_name"]?.ToString();
            Vector3? position = ParseVector3(@params["position"] as JArray);
            Vector3? offset = ParseVector3(@params["offset"] as JArray);
            JToken parentToken = @params["parent"];

            // Duplicate the object
            GameObject duplicatedGo = UnityEngine.Object.Instantiate(sourceGo);
            Undo.RegisterCreatedObjectUndo(duplicatedGo, $"Duplicate {sourceGo.name}");

            // Set name (default: SourceName_Copy or SourceName (1))
            if (!string.IsNullOrEmpty(newName))
            {
                duplicatedGo.name = newName;
            }
            else
            {
                // Remove "(Clone)" suffix added by Instantiate and add "_Copy"
                duplicatedGo.name = sourceGo.name.Replace("(Clone)", "").Trim() + "_Copy";
            }

            // Handle positioning
            if (position.HasValue)
            {
                // Absolute position specified
                duplicatedGo.transform.position = position.Value;
            }
            else if (offset.HasValue)
            {
                // Offset from original
                duplicatedGo.transform.position = sourceGo.transform.position + offset.Value;
            }
            // else: keeps the same position as the original (default Instantiate behavior)

            // Handle parent
            if (parentToken != null)
            {
                if (parentToken.Type == JTokenType.Null || 
                    (parentToken.Type == JTokenType.String && string.IsNullOrEmpty(parentToken.ToString())))
                {
                    // Explicit null parent - move to root
                    duplicatedGo.transform.SetParent(null);
                }
                else
                {
                    GameObject newParent = FindObjectInternal(parentToken, "by_id_or_name_or_path");
                    if (newParent != null)
                    {
                        duplicatedGo.transform.SetParent(newParent.transform, true);
                    }
                    else
                    {
                        Debug.LogWarning($"[ManageGameObject.Duplicate] Parent '{parentToken}' not found. Keeping original parent.");
                    }
                }
            }
            else
            {
                // Default: same parent as source
                duplicatedGo.transform.SetParent(sourceGo.transform.parent, true);
            }

            // Mark scene dirty
            EditorUtility.SetDirty(duplicatedGo);
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());

            Selection.activeGameObject = duplicatedGo;

            return new SuccessResponse(
                $"Duplicated '{sourceGo.name}' as '{duplicatedGo.name}'.",
                new
                {
                    originalName = sourceGo.name,
                    originalId = sourceGo.GetInstanceID(),
                    duplicatedObject = Helpers.GameObjectSerializer.GetGameObjectData(duplicatedGo)
                }
            );
        }

        /// <summary>
        /// Moves a GameObject relative to another reference object.
        /// Supports directional offsets (left, right, up, down, forward, back) and distance.
        /// </summary>
        private static object MoveRelativeToObject(JObject @params, JToken targetToken, string searchMethod)
        {
            GameObject targetGo = FindObjectInternal(targetToken, searchMethod);
            if (targetGo == null)
            {
                return new ErrorResponse(
                    $"Target GameObject ('{targetToken}') not found using method '{searchMethod ?? "default"}'."
                );
            }

            // Get reference object (required for relative movement)
            JToken referenceToken = @params["reference_object"];
            if (referenceToken == null)
            {
                return new ErrorResponse("'reference_object' parameter is required for 'move_relative' action.");
            }

            GameObject referenceGo = FindObjectInternal(referenceToken, "by_id_or_name_or_path");
            if (referenceGo == null)
            {
                return new ErrorResponse($"Reference object '{referenceToken}' not found.");
            }

            // Get movement parameters
            string direction = @params["direction"]?.ToString()?.ToLower();
            float distance = @params["distance"]?.ToObject<float>() ?? 1f;
            Vector3? customOffset = ParseVector3(@params["offset"] as JArray);
            bool useWorldSpace = @params["world_space"]?.ToObject<bool>() ?? true;

            // Record for undo
            Undo.RecordObject(targetGo.transform, $"Move {targetGo.name} relative to {referenceGo.name}");

            Vector3 newPosition;

            if (customOffset.HasValue)
            {
                // Custom offset vector provided
                if (useWorldSpace)
                {
                    newPosition = referenceGo.transform.position + customOffset.Value;
                }
                else
                {
                    // Offset in reference object's local space
                    newPosition = referenceGo.transform.TransformPoint(customOffset.Value);
                }
            }
            else if (!string.IsNullOrEmpty(direction))
            {
                // Directional movement
                Vector3 directionVector = GetDirectionVector(direction, referenceGo.transform, useWorldSpace);
                newPosition = referenceGo.transform.position + directionVector * distance;
            }
            else
            {
                return new ErrorResponse("Either 'direction' or 'offset' parameter is required for 'move_relative' action.");
            }

            targetGo.transform.position = newPosition;

            // Mark scene dirty
            EditorUtility.SetDirty(targetGo);
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());

            return new SuccessResponse(
                $"Moved '{targetGo.name}' relative to '{referenceGo.name}'.",
                new
                {
                    movedObject = targetGo.name,
                    referenceObject = referenceGo.name,
                    newPosition = new[] { targetGo.transform.position.x, targetGo.transform.position.y, targetGo.transform.position.z },
                    direction = direction,
                    distance = distance,
                    gameObject = Helpers.GameObjectSerializer.GetGameObjectData(targetGo)
                }
            );
        }

        /// <summary>
        /// Converts a direction string to a Vector3.
        /// </summary>
        private static Vector3 GetDirectionVector(string direction, Transform referenceTransform, bool useWorldSpace)
        {
            if (useWorldSpace)
            {
                // World space directions
                switch (direction)
                {
                    case "right": return Vector3.right;
                    case "left": return Vector3.left;
                    case "up": return Vector3.up;
                    case "down": return Vector3.down;
                    case "forward": case "front": return Vector3.forward;
                    case "back": case "backward": case "behind": return Vector3.back;
                    default:
                        Debug.LogWarning($"[ManageGameObject.MoveRelative] Unknown direction '{direction}', defaulting to forward.");
                        return Vector3.forward;
                }
            }
            else
            {
                // Reference object's local space directions
                switch (direction)
                {
                    case "right": return referenceTransform.right;
                    case "left": return -referenceTransform.right;
                    case "up": return referenceTransform.up;
                    case "down": return -referenceTransform.up;
                    case "forward": case "front": return referenceTransform.forward;
                    case "back": case "backward": case "behind": return -referenceTransform.forward;
                    default:
                        Debug.LogWarning($"[ManageGameObject.MoveRelative] Unknown direction '{direction}', defaulting to forward.");
                        return referenceTransform.forward;
                }
            }
        }

        private static object DeleteGameObject(JToken targetToken, string searchMethod)
        {
            // Find potentially multiple objects if name/tag search is used without find_all=false implicitly
            List<GameObject> targets = FindObjectsInternal(targetToken, searchMethod, true); // find_all=true for delete safety

            if (targets.Count == 0)
            {
                return new ErrorResponse(
                    $"Target GameObject(s) ('{targetToken}') not found using method '{searchMethod ?? "default"}'."
                );
            }

            List<object> deletedObjects = new List<object>();
            foreach (var targetGo in targets)
            {
                if (targetGo != null)
                {
                    string goName = targetGo.name;
                    int goId = targetGo.GetInstanceID();
                    // Use Undo.DestroyObjectImmediate for undo support
                    Undo.DestroyObjectImmediate(targetGo);
                    deletedObjects.Add(new { name = goName, instanceID = goId });
                }
            }

            if (deletedObjects.Count > 0)
            {
                string message =
                    targets.Count == 1
                        ? $"GameObject '{deletedObjects[0].GetType().GetProperty("name").GetValue(deletedObjects[0])}' deleted successfully."
                        : $"{deletedObjects.Count} GameObjects deleted successfully.";
                return new SuccessResponse(message, deletedObjects);
            }
            else
            {
                // Should not happen if targets.Count > 0 initially, but defensive check
                return new ErrorResponse("Failed to delete target GameObject(s).");
            }
        }

        private static object FindGameObjects(
            JObject @params,
            JToken targetToken,
            string searchMethod
        )
        {
            bool findAll = @params["findAll"]?.ToObject<bool>() ?? false;
            List<GameObject> foundObjects = FindObjectsInternal(
                targetToken,
                searchMethod,
                findAll,
                @params
            );

            if (foundObjects.Count == 0)
            {
                return new SuccessResponse("No matching GameObjects found.", new List<object>());
            }

            // Use the new serializer helper
            //var results = foundObjects.Select(go => GetGameObjectData(go)).ToList();
            var results = foundObjects.Select(go => Helpers.GameObjectSerializer.GetGameObjectData(go)).ToList();
            return new SuccessResponse($"Found {results.Count} GameObject(s).", results);
        }

        private static object GetComponentsFromTarget(string target, string searchMethod, bool includeNonPublicSerialized = true)
        {
            GameObject targetGo = FindObjectInternal(target, searchMethod);
            if (targetGo == null)
            {
                return new ErrorResponse(
                    $"Target GameObject ('{target}') not found using method '{searchMethod ?? "default"}'."
                );
            }

            try
            {
                // --- Get components, immediately copy to list, and null original array --- 
                Component[] originalComponents = targetGo.GetComponents<Component>();
                List<Component> componentsToIterate = new List<Component>(originalComponents ?? Array.Empty<Component>()); // Copy immediately, handle null case
                int componentCount = componentsToIterate.Count;
                originalComponents = null; // Null the original reference
                                           // Debug.Log($"[GetComponentsFromTarget] Found {componentCount} components on {targetGo.name}. Copied to list, nulled original. Starting REVERSE for loop...");
                                           // --- End Copy and Null --- 

                var componentData = new List<object>();

                for (int i = componentCount - 1; i >= 0; i--) // Iterate backwards over the COPY
                {
                    Component c = componentsToIterate[i]; // Use the copy
                    if (c == null)
                    {
                        // Debug.LogWarning($"[GetComponentsFromTarget REVERSE for] Encountered a null component at index {i} on {targetGo.name}. Skipping.");
                        continue; // Safety check
                    }
                    // Debug.Log($"[GetComponentsFromTarget REVERSE for] Processing component: {c.GetType()?.FullName ?? "null"} (ID: {c.GetInstanceID()}) at index {i} on {targetGo.name}");
                    try
                    {
                        var data = Helpers.GameObjectSerializer.GetComponentData(c, includeNonPublicSerialized);
                        if (data != null) // Ensure GetComponentData didn't return null
                        {
                            componentData.Insert(0, data); // Insert at beginning to maintain original order in final list
                        }
                        // else
                        // {
                        //     Debug.LogWarning($"[GetComponentsFromTarget REVERSE for] GetComponentData returned null for component {c.GetType().FullName} (ID: {c.GetInstanceID()}) on {targetGo.name}. Skipping addition.");
                        // }
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"[GetComponentsFromTarget REVERSE for] Error processing component {c.GetType().FullName} (ID: {c.GetInstanceID()}) on {targetGo.name}: {ex.Message}\n{ex.StackTrace}");
                        // Optionally add placeholder data or just skip
                        componentData.Insert(0, new JObject( // Insert error marker at beginning
                            new JProperty("typeName", c.GetType().FullName + " (Serialization Error)"),
                            new JProperty("instanceID", c.GetInstanceID()),
                            new JProperty("error", ex.Message)
                        ));
                    }
                }
                // Debug.Log($"[GetComponentsFromTarget] Finished REVERSE for loop.");

                // Cleanup the list we created
                componentsToIterate.Clear();
                componentsToIterate = null;

                return new SuccessResponse(
                    $"Retrieved {componentData.Count} components from '{targetGo.name}'.",
                    componentData // List was built in original order
                );
            }
            catch (Exception e)
            {
                return new ErrorResponse(
                    $"Error getting components from '{targetGo.name}': {e.Message}"
                );
            }
        }

        private static object GetSingleComponentFromTarget(string target, string searchMethod, string componentName, bool includeNonPublicSerialized = true)
        {
            GameObject targetGo = FindObjectInternal(target, searchMethod);
            if (targetGo == null)
            {
                return new ErrorResponse(
                    $"Target GameObject ('{target}') not found using method '{searchMethod ?? "default"}'."
                );
            }

            try
            {
                // Try to find the component by name
                Component targetComponent = targetGo.GetComponent(componentName);

                // If not found directly, try to find by type name (handle namespaces)
                if (targetComponent == null)
                {
                    Component[] allComponents = targetGo.GetComponents<Component>();
                    foreach (Component comp in allComponents)
                    {
                        if (comp != null)
                        {
                            string typeName = comp.GetType().Name;
                            string fullTypeName = comp.GetType().FullName;

                            if (typeName == componentName || fullTypeName == componentName)
                            {
                                targetComponent = comp;
                                break;
                            }
                        }
                    }
                }

                if (targetComponent == null)
                {
                    return new ErrorResponse(
                        $"Component '{componentName}' not found on GameObject '{targetGo.name}'."
                    );
                }

                var componentData = Helpers.GameObjectSerializer.GetComponentData(targetComponent, includeNonPublicSerialized);

                if (componentData == null)
                {
                    return new ErrorResponse(
                        $"Failed to serialize component '{componentName}' on GameObject '{targetGo.name}'."
                    );
                }

                return new SuccessResponse(
                    $"Retrieved component '{componentName}' from '{targetGo.name}'.",
                    componentData
                );
            }
            catch (Exception e)
            {
                return new ErrorResponse(
                    $"Error getting component '{componentName}' from '{targetGo.name}': {e.Message}"
                );
            }
        }

        private static object AddComponentToTarget(
            JObject @params,
            JToken targetToken,
            string searchMethod
        )
        {
            GameObject targetGo = FindObjectInternal(targetToken, searchMethod);
            if (targetGo == null)
            {
                return new ErrorResponse(
                    $"Target GameObject ('{targetToken}') not found using method '{searchMethod ?? "default"}'."
                );
            }

            string typeName = null;
            JObject properties = null;

            // Allow adding component specified directly or via componentsToAdd array (take first)
            if (@params["componentName"] != null)
            {
                typeName = @params["componentName"]?.ToString();
                properties = @params["componentProperties"]?[typeName] as JObject; // Check if props are nested under name
            }
            else if (
                @params["componentsToAdd"] is JArray componentsToAddArray
                && componentsToAddArray.Count > 0
            )
            {
                var compToken = componentsToAddArray.First;
                if (compToken.Type == JTokenType.String)
                    typeName = compToken.ToString();
                else if (compToken is JObject compObj)
                {
                    typeName = compObj["typeName"]?.ToString();
                    properties = compObj["properties"] as JObject;
                }
            }

            if (string.IsNullOrEmpty(typeName))
            {
                return new ErrorResponse(
                    "Component type name ('componentName' or first element in 'componentsToAdd') is required."
                );
            }

            var addResult = AddComponentInternal(targetGo, typeName, properties);
            if (addResult != null)
                return addResult; // Return error

            EditorUtility.SetDirty(targetGo);
            // Use the new serializer helper
            return new SuccessResponse(
                $"Component '{typeName}' added to '{targetGo.name}'.",
                Helpers.GameObjectSerializer.GetGameObjectData(targetGo)
            ); // Return updated GO data
        }

        private static object RemoveComponentFromTarget(
            JObject @params,
            JToken targetToken,
            string searchMethod
        )
        {
            GameObject targetGo = FindObjectInternal(targetToken, searchMethod);
            if (targetGo == null)
            {
                return new ErrorResponse(
                    $"Target GameObject ('{targetToken}') not found using method '{searchMethod ?? "default"}'."
                );
            }

            string typeName = null;
            // Allow removing component specified directly or via componentsToRemove array (take first)
            if (@params["componentName"] != null)
            {
                typeName = @params["componentName"]?.ToString();
            }
            else if (
                @params["componentsToRemove"] is JArray componentsToRemoveArray
                && componentsToRemoveArray.Count > 0
            )
            {
                typeName = componentsToRemoveArray.First?.ToString();
            }

            if (string.IsNullOrEmpty(typeName))
            {
                return new ErrorResponse(
                    "Component type name ('componentName' or first element in 'componentsToRemove') is required."
                );
            }

            var removeResult = RemoveComponentInternal(targetGo, typeName);
            if (removeResult != null)
                return removeResult; // Return error

            EditorUtility.SetDirty(targetGo);
            // Use the new serializer helper
            return new SuccessResponse(
                $"Component '{typeName}' removed from '{targetGo.name}'.",
                Helpers.GameObjectSerializer.GetGameObjectData(targetGo)
            );
        }

        private static object SetComponentPropertyOnTarget(
            JObject @params,
            JToken targetToken,
            string searchMethod
        )
        {
            GameObject targetGo = FindObjectInternal(targetToken, searchMethod);
            if (targetGo == null)
            {
                return new ErrorResponse(
                    $"Target GameObject ('{targetToken}') not found using method '{searchMethod ?? "default"}'."
                );
            }

            string compName = @params["componentName"]?.ToString();
            JObject propertiesToSet = null;

            if (!string.IsNullOrEmpty(compName))
            {
                // Properties might be directly under componentProperties or nested under the component name
                if (@params["componentProperties"] is JObject compProps)
                {
                    propertiesToSet = compProps[compName] as JObject ?? compProps; // Allow flat or nested structure
                }
            }
            else
            {
                return new ErrorResponse("'componentName' parameter is required.");
            }

            if (propertiesToSet == null || !propertiesToSet.HasValues)
            {
                return new ErrorResponse(
                    "'componentProperties' dictionary for the specified component is required and cannot be empty."
                );
            }

            var setResult = SetComponentPropertiesInternal(targetGo, compName, propertiesToSet);
            if (setResult != null)
                return setResult; // Return error

            EditorUtility.SetDirty(targetGo);
            // Use the new serializer helper
            return new SuccessResponse(
                $"Properties set for component '{compName}' on '{targetGo.name}'.",
                Helpers.GameObjectSerializer.GetGameObjectData(targetGo)
            );
        }

        // --- Internal Helpers ---

        /// <summary>
        /// Parses a JArray like [x, y, z] into a Vector3.
        /// </summary>
        private static Vector3? ParseVector3(JArray array)
        {
            if (array != null && array.Count == 3)
            {
                try
                {
                    return new Vector3(
                        array[0].ToObject<float>(),
                        array[1].ToObject<float>(),
                        array[2].ToObject<float>()
                    );
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"Failed to parse JArray as Vector3: {array}. Error: {ex.Message}");
                }
            }
            return null;
        }

        /// <summary>
        /// Finds a single GameObject based on token (ID, name, path) and search method.
        /// </summary>
        private static GameObject FindObjectInternal(
            JToken targetToken,
            string searchMethod,
            JObject findParams = null
        )
        {
            // If find_all is not explicitly false, we still want only one for most single-target operations.
            bool findAll = findParams?["findAll"]?.ToObject<bool>() ?? false;
            // If a specific target ID is given, always find just that one.
            if (
                targetToken?.Type == JTokenType.Integer
                || (searchMethod == "by_id" && int.TryParse(targetToken?.ToString(), out _))
            )
            {
                findAll = false;
            }
            List<GameObject> results = FindObjectsInternal(
                targetToken,
                searchMethod,
                findAll,
                findParams
            );
            return results.Count > 0 ? results[0] : null;
        }

        /// <summary>
        /// Core logic for finding GameObjects based on various criteria.
        /// </summary>
        private static List<GameObject> FindObjectsInternal(
            JToken targetToken,
            string searchMethod,
            bool findAll,
            JObject findParams = null
        )
        {
            List<GameObject> results = new List<GameObject>();
            string searchTerm = findParams?["searchTerm"]?.ToString() ?? targetToken?.ToString(); // Use searchTerm if provided, else the target itself
            bool searchInChildren = findParams?["searchInChildren"]?.ToObject<bool>() ?? false;
            bool searchInactive = findParams?["searchInactive"]?.ToObject<bool>() ?? false;

            // Default search method if not specified
            if (string.IsNullOrEmpty(searchMethod))
            {
                if (targetToken?.Type == JTokenType.Integer)
                    searchMethod = "by_id";
                else if (!string.IsNullOrEmpty(searchTerm) && searchTerm.Contains('/'))
                    searchMethod = "by_path";
                else
                    searchMethod = "by_name"; // Default fallback
            }

            GameObject rootSearchObject = null;
            // If searching in children, find the initial target first
            if (searchInChildren && targetToken != null)
            {
                rootSearchObject = FindObjectInternal(targetToken, "by_id_or_name_or_path"); // Find the root for child search
                if (rootSearchObject == null)
                {
                    Debug.LogWarning(
                        $"[ManageGameObject.Find] Root object '{targetToken}' for child search not found."
                    );
                    return results; // Return empty if root not found
                }
            }

            switch (searchMethod)
            {
                case "by_id":
                    if (int.TryParse(searchTerm, out int instanceId))
                    {
                        // EditorUtility.InstanceIDToObject is slow, iterate manually if possible
                        // GameObject obj = EditorUtility.InstanceIDToObject(instanceId) as GameObject;
                        var allObjects = GetAllSceneObjects(searchInactive); // More efficient
                        GameObject obj = allObjects.FirstOrDefault(go =>
                            go.GetInstanceID() == instanceId
                        );
                        if (obj != null)
                            results.Add(obj);
                    }
                    break;
                case "by_name":
                    var searchPoolName = rootSearchObject
                        ? rootSearchObject
                            .GetComponentsInChildren<Transform>(searchInactive)
                            .Select(t => t.gameObject)
                        : GetAllSceneObjects(searchInactive);
                    results.AddRange(searchPoolName.Where(go => go.name == searchTerm));
                    break;
                case "by_path":
                    // Path is relative to scene root or rootSearchObject
                    Transform foundTransform = rootSearchObject
                        ? rootSearchObject.transform.Find(searchTerm)
                        : GameObject.Find(searchTerm)?.transform;
                    if (foundTransform != null)
                        results.Add(foundTransform.gameObject);
                    break;
                case "by_tag":
                    var searchPoolTag = rootSearchObject
                        ? rootSearchObject
                            .GetComponentsInChildren<Transform>(searchInactive)
                            .Select(t => t.gameObject)
                        : GetAllSceneObjects(searchInactive);
                    results.AddRange(searchPoolTag.Where(go => go.CompareTag(searchTerm)));
                    break;
                case "by_layer":
                    var searchPoolLayer = rootSearchObject
                        ? rootSearchObject
                            .GetComponentsInChildren<Transform>(searchInactive)
                            .Select(t => t.gameObject)
                        : GetAllSceneObjects(searchInactive);
                    if (int.TryParse(searchTerm, out int layerIndex))
                    {
                        results.AddRange(searchPoolLayer.Where(go => go.layer == layerIndex));
                    }
                    else
                    {
                        int namedLayer = LayerMask.NameToLayer(searchTerm);
                        if (namedLayer != -1)
                            results.AddRange(searchPoolLayer.Where(go => go.layer == namedLayer));
                    }
                    break;
                case "by_component":
                    Type componentType = FindType(searchTerm);
                    if (componentType != null)
                    {
                        // Determine FindObjectsInactive based on the searchInactive flag
                        FindObjectsInactive findInactive = searchInactive
                            ? FindObjectsInactive.Include
                            : FindObjectsInactive.Exclude;
                        // Replace FindObjectsOfType with FindObjectsByType, specifying the sorting mode and inactive state
                        var searchPoolComp = rootSearchObject
                            ? rootSearchObject
                                .GetComponentsInChildren(componentType, searchInactive)
                                .Select(c => (c as Component).gameObject)
                            : UnityEngine
                                .Object.FindObjectsByType(
                                    componentType,
                                    findInactive,
                                    FindObjectsSortMode.None
                                )
                                .Select(c => (c as Component).gameObject);
                        results.AddRange(searchPoolComp.Where(go => go != null)); // Ensure GO is valid
                    }
                    else
                    {
                        Debug.LogWarning(
                            $"[ManageGameObject.Find] Component type not found: {searchTerm}"
                        );
                    }
                    break;
                case "by_id_or_name_or_path": // Helper method used internally
                    if (int.TryParse(searchTerm, out int id))
                    {
                        var allObjectsId = GetAllSceneObjects(true); // Search inactive for internal lookup
                        GameObject objById = allObjectsId.FirstOrDefault(go =>
                            go.GetInstanceID() == id
                        );
                        if (objById != null)
                        {
                            results.Add(objById);
                            break;
                        }
                    }
                    GameObject objByPath = GameObject.Find(searchTerm);
                    if (objByPath != null)
                    {
                        results.Add(objByPath);
                        break;
                    }

                    var allObjectsName = GetAllSceneObjects(true);
                    results.AddRange(allObjectsName.Where(go => go.name == searchTerm));
                    break;
                default:
                    Debug.LogWarning(
                        $"[ManageGameObject.Find] Unknown search method: {searchMethod}"
                    );
                    break;
            }

            // If only one result is needed, return just the first one found.
            if (!findAll && results.Count > 1)
            {
                return new List<GameObject> { results[0] };
            }

            return results.Distinct().ToList(); // Ensure uniqueness
        }

        // Helper to get all scene objects efficiently
        private static IEnumerable<GameObject> GetAllSceneObjects(bool includeInactive)
        {
            // SceneManager.GetActiveScene().GetRootGameObjects() is faster than FindObjectsOfType<GameObject>()
            var rootObjects = SceneManager.GetActiveScene().GetRootGameObjects();
            var allObjects = new List<GameObject>();
            foreach (var root in rootObjects)
            {
                allObjects.AddRange(
                    root.GetComponentsInChildren<Transform>(includeInactive)
                        .Select(t => t.gameObject)
                );
            }
            return allObjects;
        }

        /// <summary>
        /// Adds a component by type name and optionally sets properties.
        /// Returns null on success, or an error response object on failure.
        /// </summary>
        private static object AddComponentInternal(
            GameObject targetGo,
            string typeName,
            JObject properties
        )
        {
            Type componentType = FindType(typeName);
            if (componentType == null)
            {
                return new ErrorResponse(
                    $"Component type '{typeName}' not found or is not a valid Component."
                );
            }
            if (!typeof(Component).IsAssignableFrom(componentType))
            {
                return new ErrorResponse($"Type '{typeName}' is not a Component.");
            }

            // Prevent adding Transform again
            if (componentType == typeof(Transform))
            {
                return new ErrorResponse("Cannot add another Transform component.");
            }

            // Check for 2D/3D physics component conflicts
            bool isAdding2DPhysics =
                typeof(Rigidbody2D).IsAssignableFrom(componentType)
                || typeof(Collider2D).IsAssignableFrom(componentType);
            bool isAdding3DPhysics =
                typeof(Rigidbody).IsAssignableFrom(componentType)
                || typeof(Collider).IsAssignableFrom(componentType);

            if (isAdding2DPhysics)
            {
                // Check if the GameObject already has any 3D Rigidbody or Collider
                if (
                    targetGo.GetComponent<Rigidbody>() != null
                    || targetGo.GetComponent<Collider>() != null
                )
                {
                    return new ErrorResponse(
                        $"Cannot add 2D physics component '{typeName}' because the GameObject '{targetGo.name}' already has a 3D Rigidbody or Collider."
                    );
                }
            }
            else if (isAdding3DPhysics)
            {
                // Check if the GameObject already has any 2D Rigidbody or Collider
                if (
                    targetGo.GetComponent<Rigidbody2D>() != null
                    || targetGo.GetComponent<Collider2D>() != null
                )
                {
                    return new ErrorResponse(
                        $"Cannot add 3D physics component '{typeName}' because the GameObject '{targetGo.name}' already has a 2D Rigidbody or Collider."
                    );
                }
            }

            try
            {
                // Use Undo.AddComponent for undo support
                Component newComponent = Undo.AddComponent(targetGo, componentType);
                if (newComponent == null)
                {
                    return new ErrorResponse(
                        $"Failed to add component '{typeName}' to '{targetGo.name}'. It might be disallowed (e.g., adding script twice)."
                    );
                }

                // Set default values for specific component types
                if (newComponent is Light light)
                {
                    // Default newly added lights to directional
                    light.type = LightType.Directional;
                }

                // Set properties if provided
                if (properties != null)
                {
                    var setResult = SetComponentPropertiesInternal(
                        targetGo,
                        typeName,
                        properties,
                        newComponent
                    ); // Pass the new component instance
                    if (setResult != null)
                    {
                        // If setting properties failed, maybe remove the added component?
                        Undo.DestroyObjectImmediate(newComponent);
                        return setResult; // Return the error from setting properties
                    }
                }

                return null; // Success
            }
            catch (Exception e)
            {
                return new ErrorResponse(
                    $"Error adding component '{typeName}' to '{targetGo.name}': {e.Message}"
                );
            }
        }

        /// <summary>
        /// Removes a component by type name.
        /// Returns null on success, or an error response object on failure.
        /// </summary>
        private static object RemoveComponentInternal(GameObject targetGo, string typeName)
        {
            Type componentType = FindType(typeName);
            if (componentType == null)
            {
                return new ErrorResponse($"Component type '{typeName}' not found for removal.");
            }

            // Prevent removing essential components
            if (componentType == typeof(Transform))
            {
                return new ErrorResponse("Cannot remove the Transform component.");
            }

            Component componentToRemove = targetGo.GetComponent(componentType);
            if (componentToRemove == null)
            {
                return new ErrorResponse(
                    $"Component '{typeName}' not found on '{targetGo.name}' to remove."
                );
            }

            try
            {
                // Use Undo.DestroyObjectImmediate for undo support
                Undo.DestroyObjectImmediate(componentToRemove);
                return null; // Success
            }
            catch (Exception e)
            {
                return new ErrorResponse(
                    $"Error removing component '{typeName}' from '{targetGo.name}': {e.Message}"
                );
            }
        }

        /// <summary>
        /// Sets properties on a component.
        /// Returns null on success, or an error response object on failure.
        /// </summary>
        private static object SetComponentPropertiesInternal(
            GameObject targetGo,
            string compName,
            JObject propertiesToSet,
            Component targetComponentInstance = null
        )
        {
            Component targetComponent = targetComponentInstance;
            if (targetComponent == null)
            {
                if (ComponentResolver.TryResolve(compName, out var compType, out var compError))
                {
                    targetComponent = targetGo.GetComponent(compType);
                }
                else
                {
                    targetComponent = targetGo.GetComponent(compName); // fallback to string-based lookup
                }
            }
            if (targetComponent == null)
            {
                return new ErrorResponse(
                    $"Component '{compName}' not found on '{targetGo.name}' to set properties."
                );
            }

            Undo.RecordObject(targetComponent, "Set Component Properties");

            var failures = new List<string>();
            foreach (var prop in propertiesToSet.Properties())
            {
                string propName = prop.Name;
                JToken propValue = prop.Value;

                try
                {
                    bool setResult = SetProperty(targetComponent, propName, propValue);
                    if (!setResult)
                    {
                        var availableProperties = ComponentResolver.GetAllComponentProperties(targetComponent.GetType());
                        var suggestions = ComponentResolver.GetAIPropertySuggestions(propName, availableProperties);
                        var msg = suggestions.Any()
                            ? $"Property '{propName}' not found. Did you mean: {string.Join(", ", suggestions)}? Available: [{string.Join(", ", availableProperties)}]"
                            : $"Property '{propName}' not found. Available: [{string.Join(", ", availableProperties)}]";
                        Debug.LogWarning($"[ManageGameObject] {msg}");
                        failures.Add(msg);
                    }
                }
                catch (Exception e)
                {
                    Debug.LogError(
                        $"[ManageGameObject] Error setting property '{propName}' on '{compName}': {e.Message}"
                    );
                    failures.Add($"Error setting '{propName}': {e.Message}");
                }
            }
            EditorUtility.SetDirty(targetComponent);
            return failures.Count == 0
                ? null
                : new ErrorResponse($"One or more properties failed on '{compName}'.", new { errors = failures });
        }

        /// <summary>
        /// Helper to set a property or field via reflection, handling basic types.
        /// </summary>
        private static bool SetProperty(object target, string memberName, JToken value)
        {
            Type type = target.GetType();
            BindingFlags flags =
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase;

            // Use shared serializer to avoid per-call allocation
            var inputSerializer = InputSerializer;

            try
            {
                // Handle special case for materials with dot notation (material.property)
                // Examples: material.color, sharedMaterial.color, materials[0].color
                if (memberName.Contains('.') || memberName.Contains('['))
                {
                    // Pass the inputSerializer down for nested conversions
                    return SetNestedProperty(target, memberName, value, inputSerializer);
                }

                PropertyInfo propInfo = type.GetProperty(memberName, flags);
                if (propInfo != null && propInfo.CanWrite)
                {
                    // Use the inputSerializer for conversion
                    object convertedValue = ConvertJTokenToType(value, propInfo.PropertyType, inputSerializer);
                    if (convertedValue != null || value.Type == JTokenType.Null) // Allow setting null
                    {
                        propInfo.SetValue(target, convertedValue);
                        return true;
                    }
                    else
                    {
                        Debug.LogWarning($"[SetProperty] Conversion failed for property '{memberName}' (Type: {propInfo.PropertyType.Name}) from token: {value.ToString(Formatting.None)}");
                    }
                }
                else
                {
                    FieldInfo fieldInfo = type.GetField(memberName, flags);
                    if (fieldInfo != null) // Check if !IsLiteral?
                    {
                        // Use the inputSerializer for conversion
                        object convertedValue = ConvertJTokenToType(value, fieldInfo.FieldType, inputSerializer);
                        if (convertedValue != null || value.Type == JTokenType.Null) // Allow setting null
                        {
                            fieldInfo.SetValue(target, convertedValue);
                            return true;
                        }
                        else
                        {
                            Debug.LogWarning($"[SetProperty] Conversion failed for field '{memberName}' (Type: {fieldInfo.FieldType.Name}) from token: {value.ToString(Formatting.None)}");
                        }
                    }
                    else
                    {
                        // Try NonPublic [SerializeField] fields
                        var npField = type.GetField(memberName, BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.IgnoreCase);
                        if (npField != null && npField.GetCustomAttribute<SerializeField>() != null)
                        {
                            object convertedValue = ConvertJTokenToType(value, npField.FieldType, inputSerializer);
                            if (convertedValue != null || value.Type == JTokenType.Null)
                            {
                                npField.SetValue(target, convertedValue);
                                return true;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError(
                    $"[SetProperty] Failed to set '{memberName}' on {type.Name}: {ex.Message}\nToken: {value.ToString(Formatting.None)}"
                );
            }
            return false;
        }

        /// <summary>
        /// Sets a nested property using dot notation (e.g., "material.color") or array access (e.g., "materials[0]")
        /// </summary>
        // Pass the input serializer for conversions
        //Using the serializer helper
        private static bool SetNestedProperty(object target, string path, JToken value, JsonSerializer inputSerializer)
        {
            try
            {
                // Split the path into parts (handling both dot notation and array indexing)
                string[] pathParts = SplitPropertyPath(path);
                if (pathParts.Length == 0)
                    return false;

                object currentObject = target;
                Type currentType = currentObject.GetType();
                BindingFlags flags =
                    BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase;

                // Traverse the path until we reach the final property
                for (int i = 0; i < pathParts.Length - 1; i++)
                {
                    string part = pathParts[i];
                    bool isArray = false;
                    int arrayIndex = -1;

                    // Check if this part contains array indexing
                    if (part.Contains("["))
                    {
                        int startBracket = part.IndexOf('[');
                        int endBracket = part.IndexOf(']');
                        if (startBracket > 0 && endBracket > startBracket)
                        {
                            string indexStr = part.Substring(
                                startBracket + 1,
                                endBracket - startBracket - 1
                            );
                            if (int.TryParse(indexStr, out arrayIndex))
                            {
                                isArray = true;
                                part = part.Substring(0, startBracket);
                            }
                        }
                    }
                    // Get the property/field
                    PropertyInfo propInfo = currentType.GetProperty(part, flags);
                    FieldInfo fieldInfo = null;
                    if (propInfo == null)
                    {
                        fieldInfo = currentType.GetField(part, flags);
                        if (fieldInfo == null)
                        {
                            Debug.LogWarning(
                                $"[SetNestedProperty] Could not find property or field '{part}' on type '{currentType.Name}'"
                            );
                            return false;
                        }
                    }

                    // Get the value
                    currentObject =
                        propInfo != null
                            ? propInfo.GetValue(currentObject)
                            : fieldInfo.GetValue(currentObject);
                    //Need to stop if current property is null
                    if (currentObject == null)
                    {
                        Debug.LogWarning(
                            $"[SetNestedProperty] Property '{part}' is null, cannot access nested properties."
                        );
                        return false;
                    }
                    // If this part was an array or list, access the specific index
                    if (isArray)
                    {
                        if (currentObject is Material[])
                        {
                            var materials = currentObject as Material[];
                            if (arrayIndex < 0 || arrayIndex >= materials.Length)
                            {
                                Debug.LogWarning(
                                    $"[SetNestedProperty] Material index {arrayIndex} out of range (0-{materials.Length - 1})"
                                );
                                return false;
                            }
                            currentObject = materials[arrayIndex];
                        }
                        else if (currentObject is System.Collections.IList)
                        {
                            var list = currentObject as System.Collections.IList;
                            if (arrayIndex < 0 || arrayIndex >= list.Count)
                            {
                                Debug.LogWarning(
                                    $"[SetNestedProperty] Index {arrayIndex} out of range (0-{list.Count - 1})"
                                );
                                return false;
                            }
                            currentObject = list[arrayIndex];
                        }
                        else
                        {
                            Debug.LogWarning(
                                $"[SetNestedProperty] Property '{part}' is not an array or list, cannot access by index."
                            );
                            return false;
                        }
                    }
                    currentType = currentObject.GetType();
                }

                // Set the final property
                string finalPart = pathParts[pathParts.Length - 1];

                // Special handling for Material properties (shader properties)
                if (currentObject is Material material && finalPart.StartsWith("_"))
                {
                    return MaterialOps.TrySetShaderProperty(material, finalPart, value, inputSerializer);
                }

                // For standard properties (not shader specific)
                PropertyInfo finalPropInfo = currentType.GetProperty(finalPart, flags);
                if (finalPropInfo != null && finalPropInfo.CanWrite)
                {
                    // Use the inputSerializer for conversion
                    object convertedValue = ConvertJTokenToType(value, finalPropInfo.PropertyType, inputSerializer);
                    if (convertedValue != null || value.Type == JTokenType.Null)
                    {
                        finalPropInfo.SetValue(currentObject, convertedValue);
                        return true;
                    }
                    else
                    {
                        Debug.LogWarning($"[SetNestedProperty] Final conversion failed for property '{finalPart}' (Type: {finalPropInfo.PropertyType.Name}) from token: {value.ToString(Formatting.None)}");
                    }
                }
                else
                {
                    FieldInfo finalFieldInfo = currentType.GetField(finalPart, flags);
                    if (finalFieldInfo != null)
                    {
                        // Use the inputSerializer for conversion
                        object convertedValue = ConvertJTokenToType(value, finalFieldInfo.FieldType, inputSerializer);
                        if (convertedValue != null || value.Type == JTokenType.Null)
                        {
                            finalFieldInfo.SetValue(currentObject, convertedValue);
                            return true;
                        }
                        else
                        {
                            Debug.LogWarning($"[SetNestedProperty] Final conversion failed for field '{finalPart}' (Type: {finalFieldInfo.FieldType.Name}) from token: {value.ToString(Formatting.None)}");
                        }
                    }
                    else
                    {
                        Debug.LogWarning(
                            $"[SetNestedProperty] Could not find final writable property or field '{finalPart}' on type '{currentType.Name}'"
                        );
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError(
                    $"[SetNestedProperty] Error setting nested property '{path}': {ex.Message}\nToken: {value.ToString(Formatting.None)}"
                );
            }

            return false;
        }


        /// <summary>
        /// Split a property path into parts, handling both dot notation and array indexers
        /// </summary>
        private static string[] SplitPropertyPath(string path)
        {
            // Handle complex paths with both dots and array indexers
            List<string> parts = new List<string>();
            int startIndex = 0;
            bool inBrackets = false;

            for (int i = 0; i < path.Length; i++)
            {
                char c = path[i];

                if (c == '[')
                {
                    inBrackets = true;
                }
                else if (c == ']')
                {
                    inBrackets = false;
                }
                else if (c == '.' && !inBrackets)
                {
                    // Found a dot separator outside of brackets
                    parts.Add(path.Substring(startIndex, i - startIndex));
                    startIndex = i + 1;
                }
            }
            if (startIndex < path.Length)
            {
                parts.Add(path.Substring(startIndex));
            }
            return parts.ToArray();
        }

        /// <summary>
        /// Simple JToken to Type conversion for common Unity types, using JsonSerializer.
        /// </summary>
         // Pass the input serializer
        private static object ConvertJTokenToType(JToken token, Type targetType, JsonSerializer inputSerializer)
        {
            if (token == null || token.Type == JTokenType.Null)
            {
                if (targetType.IsValueType && Nullable.GetUnderlyingType(targetType) == null)
                {
                    Debug.LogWarning($"Cannot assign null to non-nullable value type {targetType.Name}. Returning default value.");
                    return Activator.CreateInstance(targetType);
                }
                return null;
            }

            try
            {
                // Use the provided serializer instance which includes our custom converters
                return token.ToObject(targetType, inputSerializer);
            }
            catch (JsonSerializationException jsonEx)
            {
                Debug.LogError($"JSON Deserialization Error converting token to {targetType.FullName}: {jsonEx.Message}\nToken: {token.ToString(Formatting.None)}");
                // Optionally re-throw or return null/default
                // return targetType.IsValueType ? Activator.CreateInstance(targetType) : null;
                throw; // Re-throw to indicate failure higher up
            }
            catch (ArgumentException argEx)
            {
                Debug.LogError($"Argument Error converting token to {targetType.FullName}: {argEx.Message}\nToken: {token.ToString(Formatting.None)}");
                throw;
            }
            catch (Exception ex)
            {
                Debug.LogError($"Unexpected error converting token to {targetType.FullName}: {ex}\nToken: {token.ToString(Formatting.None)}");
                throw;
            }
            // If ToObject succeeded, it would have returned. If it threw, we wouldn't reach here.
            // This fallback logic is likely unreachable if ToObject covers all cases or throws on failure.
            // Debug.LogWarning($"Conversion failed for token to {targetType.FullName}. Token: {token.ToString(Formatting.None)}");
            // return targetType.IsValueType ? Activator.CreateInstance(targetType) : null;
        }

        // --- ParseJTokenTo... helpers are likely redundant now with the serializer approach ---
        // Keep them temporarily for reference or if specific fallback logic is ever needed.

        private static Vector3 ParseJTokenToVector3(JToken token)
        {
            // ... (implementation - likely replaced by Vector3Converter) ...
            // Consider removing these if the serializer handles them reliably.
            if (token is JObject obj && obj.ContainsKey("x") && obj.ContainsKey("y") && obj.ContainsKey("z"))
            {
                return new Vector3(obj["x"].ToObject<float>(), obj["y"].ToObject<float>(), obj["z"].ToObject<float>());
            }
            if (token is JArray arr && arr.Count >= 3)
            {
                return new Vector3(arr[0].ToObject<float>(), arr[1].ToObject<float>(), arr[2].ToObject<float>());
            }
            Debug.LogWarning($"Could not parse JToken '{token}' as Vector3 using fallback. Returning Vector3.zero.");
            return Vector3.zero;

        }
        private static Vector2 ParseJTokenToVector2(JToken token)
        {
            // ... (implementation - likely replaced by Vector2Converter) ...
            if (token is JObject obj && obj.ContainsKey("x") && obj.ContainsKey("y"))
            {
                return new Vector2(obj["x"].ToObject<float>(), obj["y"].ToObject<float>());
            }
            if (token is JArray arr && arr.Count >= 2)
            {
                return new Vector2(arr[0].ToObject<float>(), arr[1].ToObject<float>());
            }
            Debug.LogWarning($"Could not parse JToken '{token}' as Vector2 using fallback. Returning Vector2.zero.");
            return Vector2.zero;
        }
        private static Quaternion ParseJTokenToQuaternion(JToken token)
        {
            // ... (implementation - likely replaced by QuaternionConverter) ...
            if (token is JObject obj && obj.ContainsKey("x") && obj.ContainsKey("y") && obj.ContainsKey("z") && obj.ContainsKey("w"))
            {
                return new Quaternion(obj["x"].ToObject<float>(), obj["y"].ToObject<float>(), obj["z"].ToObject<float>(), obj["w"].ToObject<float>());
            }
            if (token is JArray arr && arr.Count >= 4)
            {
                return new Quaternion(arr[0].ToObject<float>(), arr[1].ToObject<float>(), arr[2].ToObject<float>(), arr[3].ToObject<float>());
            }
            Debug.LogWarning($"Could not parse JToken '{token}' as Quaternion using fallback. Returning Quaternion.identity.");
            return Quaternion.identity;
        }
        private static Color ParseJTokenToColor(JToken token)
        {
            // ... (implementation - likely replaced by ColorConverter) ...
            if (token is JObject obj && obj.ContainsKey("r") && obj.ContainsKey("g") && obj.ContainsKey("b") && obj.ContainsKey("a"))
            {
                return new Color(obj["r"].ToObject<float>(), obj["g"].ToObject<float>(), obj["b"].ToObject<float>(), obj["a"].ToObject<float>());
            }
            if (token is JArray arr && arr.Count >= 4)
            {
                return new Color(arr[0].ToObject<float>(), arr[1].ToObject<float>(), arr[2].ToObject<float>(), arr[3].ToObject<float>());
            }
            Debug.LogWarning($"Could not parse JToken '{token}' as Color using fallback. Returning Color.white.");
            return Color.white;
        }
        private static Rect ParseJTokenToRect(JToken token)
        {
            // ... (implementation - likely replaced by RectConverter) ...
            if (token is JObject obj && obj.ContainsKey("x") && obj.ContainsKey("y") && obj.ContainsKey("width") && obj.ContainsKey("height"))
            {
                return new Rect(obj["x"].ToObject<float>(), obj["y"].ToObject<float>(), obj["width"].ToObject<float>(), obj["height"].ToObject<float>());
            }
            if (token is JArray arr && arr.Count >= 4)
            {
                return new Rect(arr[0].ToObject<float>(), arr[1].ToObject<float>(), arr[2].ToObject<float>(), arr[3].ToObject<float>());
            }
            Debug.LogWarning($"Could not parse JToken '{token}' as Rect using fallback. Returning Rect.zero.");
            return Rect.zero;
        }
        private static Bounds ParseJTokenToBounds(JToken token)
        {
            // ... (implementation - likely replaced by BoundsConverter) ...
            if (token is JObject obj && obj.ContainsKey("center") && obj.ContainsKey("size"))
            {
                // Requires Vector3 conversion, which should ideally use the serializer too
                Vector3 center = ParseJTokenToVector3(obj["center"]); // Or use obj["center"].ToObject<Vector3>(inputSerializer)
                Vector3 size = ParseJTokenToVector3(obj["size"]);     // Or use obj["size"].ToObject<Vector3>(inputSerializer)
                return new Bounds(center, size);
            }
            // Array fallback for Bounds is less intuitive, maybe remove?
            // if (token is JArray arr && arr.Count >= 6)
            // {
            //      return new Bounds(new Vector3(arr[0].ToObject<float>(), arr[1].ToObject<float>(), arr[2].ToObject<float>()), new Vector3(arr[3].ToObject<float>(), arr[4].ToObject<float>(), arr[5].ToObject<float>()));
            // }
            Debug.LogWarning($"Could not parse JToken '{token}' as Bounds using fallback. Returning new Bounds(Vector3.zero, Vector3.zero).");
            return new Bounds(Vector3.zero, Vector3.zero);
        }
        // --- End Redundant Parse Helpers ---

        /// <summary>
        /// Finds a specific UnityEngine.Object based on a find instruction JObject.
        /// Primarily used by UnityEngineObjectConverter during deserialization.
        /// </summary>
        // Made public static so UnityEngineObjectConverter can call it. Moved from ConvertJTokenToType.
        public static UnityEngine.Object FindObjectByInstruction(JObject instruction, Type targetType)
        {
            string findTerm = instruction["find"]?.ToString();
            string method = instruction["method"]?.ToString()?.ToLower();
            string componentName = instruction["component"]?.ToString(); // Specific component to get

            if (string.IsNullOrEmpty(findTerm))
            {
                Debug.LogWarning("Find instruction missing 'find' term.");
                return null;
            }

            // Use a flexible default search method if none provided
            string searchMethodToUse = string.IsNullOrEmpty(method) ? "by_id_or_name_or_path" : method;

            // If the target is an asset (Material, Texture, ScriptableObject etc.) try AssetDatabase first
            if (typeof(Material).IsAssignableFrom(targetType) ||
                typeof(Texture).IsAssignableFrom(targetType) ||
                typeof(ScriptableObject).IsAssignableFrom(targetType) ||
                targetType.FullName.StartsWith("UnityEngine.U2D") || // Sprites etc.
                typeof(AudioClip).IsAssignableFrom(targetType) ||
                typeof(AnimationClip).IsAssignableFrom(targetType) ||
                typeof(Font).IsAssignableFrom(targetType) ||
                typeof(Shader).IsAssignableFrom(targetType) ||
                typeof(ComputeShader).IsAssignableFrom(targetType) ||
                typeof(GameObject).IsAssignableFrom(targetType) && findTerm.StartsWith("Assets/")) // Prefab check
            {
                // Try loading directly by path/GUID first
                UnityEngine.Object asset = AssetDatabase.LoadAssetAtPath(findTerm, targetType);
                if (asset != null) return asset;
                asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(findTerm); // Try generic if type specific failed
                if (asset != null && targetType.IsAssignableFrom(asset.GetType())) return asset;


                // If direct path failed, try finding by name/type using FindAssets
                string searchFilter = $"t:{targetType.Name} {System.IO.Path.GetFileNameWithoutExtension(findTerm)}"; // Search by type and name
                string[] guids = AssetDatabase.FindAssets(searchFilter);

                if (guids.Length == 1)
                {
                    asset = AssetDatabase.LoadAssetAtPath(AssetDatabase.GUIDToAssetPath(guids[0]), targetType);
                    if (asset != null) return asset;
                }
                else if (guids.Length > 1)
                {
                    Debug.LogWarning($"[FindObjectByInstruction] Ambiguous asset find: Found {guids.Length} assets matching filter '{searchFilter}'. Provide a full path or unique name.");
                    // Optionally return the first one? Or null? Returning null is safer.
                    return null;
                }
                // If still not found, fall through to scene search (though unlikely for assets)
            }


            // --- Scene Object Search ---
            // Find the GameObject using the internal finder
            GameObject foundGo = FindObjectInternal(new JValue(findTerm), searchMethodToUse);

            if (foundGo == null)
            {
                // Don't warn yet, could still be an asset not found above
                // Debug.LogWarning($"Could not find GameObject using instruction: {instruction}");
                return null;
            }

            // Now, get the target object/component from the found GameObject
            if (targetType == typeof(GameObject))
            {
                return foundGo; // We were looking for a GameObject
            }
            else if (typeof(Component).IsAssignableFrom(targetType))
            {
                Type componentToGetType = targetType;
                if (!string.IsNullOrEmpty(componentName))
                {
                    Type specificCompType = FindType(componentName);
                    if (specificCompType != null && typeof(Component).IsAssignableFrom(specificCompType))
                    {
                        componentToGetType = specificCompType;
                    }
                    else
                    {
                        Debug.LogWarning($"Could not find component type '{componentName}' specified in find instruction. Falling back to target type '{targetType.Name}'.");
                    }
                }

                Component foundComp = foundGo.GetComponent(componentToGetType);
                if (foundComp == null)
                {
                    Debug.LogWarning($"Found GameObject '{foundGo.name}' but could not find component of type '{componentToGetType.Name}'.");
                }
                return foundComp;
            }
            else
            {
                Debug.LogWarning($"Find instruction handling not implemented for target type: {targetType.Name}");
                return null;
            }
        }


        /// <summary>
        /// Robust component resolver that avoids Assembly.LoadFrom and works with asmdefs.
        /// Searches already-loaded assemblies, prioritizing runtime script assemblies.
        /// </summary>
        private static Type FindType(string typeName)
        {
            if (ComponentResolver.TryResolve(typeName, out Type resolvedType, out string error))
            {
                return resolvedType;
            }

            // Log the resolver error if type wasn't found
            if (!string.IsNullOrEmpty(error))
            {
                Debug.LogWarning($"[FindType] {error}");
            }

            return null;
        }
    }

    /// <summary>
    /// Robust component resolver that avoids Assembly.LoadFrom and supports assembly definitions.
    /// Prioritizes runtime (Player) assemblies over Editor assemblies.
    /// </summary>
    internal static class ComponentResolver
    {
        private static readonly Dictionary<string, Type> CacheByFqn = new(StringComparer.Ordinal);
        private static readonly Dictionary<string, Type> CacheByName = new(StringComparer.Ordinal);

        /// <summary>
        /// Resolve a Component/MonoBehaviour type by short or fully-qualified name.
        /// Prefers runtime (Player) script assemblies; falls back to Editor assemblies.
        /// Never uses Assembly.LoadFrom.
        /// </summary>
        public static bool TryResolve(string nameOrFullName, out Type type, out string error)
        {
            error = string.Empty;
            type = null!;

            // Handle null/empty input
            if (string.IsNullOrWhiteSpace(nameOrFullName))
            {
                error = "Component name cannot be null or empty";
                return false;
            }

            // 1) Exact cache hits
            if (CacheByFqn.TryGetValue(nameOrFullName, out type)) return true;
            if (!nameOrFullName.Contains(".") && CacheByName.TryGetValue(nameOrFullName, out type)) return true;
            type = Type.GetType(nameOrFullName, throwOnError: false);
            if (IsValidComponent(type)) { Cache(type); return true; }

            // 2) Search loaded assemblies (prefer Player assemblies)
            var candidates = FindCandidates(nameOrFullName);
            if (candidates.Count == 1) { type = candidates[0]; Cache(type); return true; }
            if (candidates.Count > 1) { error = Ambiguity(nameOrFullName, candidates); type = null!; return false; }

#if UNITY_EDITOR
            // 3) Last resort: Editor-only TypeCache (fast index)
            var tc = TypeCache.GetTypesDerivedFrom<Component>()
                              .Where(t => NamesMatch(t, nameOrFullName));
            candidates = PreferPlayer(tc).ToList();
            if (candidates.Count == 1) { type = candidates[0]; Cache(type); return true; }
            if (candidates.Count > 1) { error = Ambiguity(nameOrFullName, candidates); type = null!; return false; }
#endif

            error = $"Component type '{nameOrFullName}' not found in loaded runtime assemblies. " +
                    "Use a fully-qualified name (Namespace.TypeName) and ensure the script compiled.";
            type = null!;
            return false;
        }

        private static bool NamesMatch(Type t, string q) =>
            t.Name.Equals(q, StringComparison.Ordinal) ||
            (t.FullName?.Equals(q, StringComparison.Ordinal) ?? false);

        private static bool IsValidComponent(Type t) =>
            t != null && typeof(Component).IsAssignableFrom(t);

        private static void Cache(Type t)
        {
            if (t.FullName != null) CacheByFqn[t.FullName] = t;
            CacheByName[t.Name] = t;
        }

        private static List<Type> FindCandidates(string query)
        {
            bool isShort = !query.Contains('.');
            var loaded = AppDomain.CurrentDomain.GetAssemblies();

#if UNITY_EDITOR
            // Names of Player (runtime) script assemblies (asmdefs + Assembly-CSharp)
            var playerAsmNames = new HashSet<string>(
                UnityEditor.Compilation.CompilationPipeline.GetAssemblies(UnityEditor.Compilation.AssembliesType.Player).Select(a => a.name),
                StringComparer.Ordinal);

            IEnumerable<System.Reflection.Assembly> playerAsms = loaded.Where(a => playerAsmNames.Contains(a.GetName().Name));
            IEnumerable<System.Reflection.Assembly> editorAsms = loaded.Except(playerAsms);
#else
            IEnumerable<System.Reflection.Assembly> playerAsms = loaded;
            IEnumerable<System.Reflection.Assembly> editorAsms = Array.Empty<System.Reflection.Assembly>();
#endif
            static IEnumerable<Type> SafeGetTypes(System.Reflection.Assembly a)
            {
                try { return a.GetTypes(); }
                catch (ReflectionTypeLoadException rtle) { return rtle.Types.Where(t => t != null)!; }
            }

            Func<Type, bool> match = isShort
                ? (t => t.Name.Equals(query, StringComparison.Ordinal))
                : (t => t.FullName!.Equals(query, StringComparison.Ordinal));

            var fromPlayer = playerAsms.SelectMany(SafeGetTypes)
                                       .Where(IsValidComponent)
                                       .Where(match);
            var fromEditor = editorAsms.SelectMany(SafeGetTypes)
                                       .Where(IsValidComponent)
                                       .Where(match);

            var list = new List<Type>(fromPlayer);
            if (list.Count == 0) list.AddRange(fromEditor);
            return list;
        }

#if UNITY_EDITOR
        private static IEnumerable<Type> PreferPlayer(IEnumerable<Type> seq)
        {
            var player = new HashSet<string>(
                UnityEditor.Compilation.CompilationPipeline.GetAssemblies(UnityEditor.Compilation.AssembliesType.Player).Select(a => a.name),
                StringComparer.Ordinal);

            return seq.OrderBy(t => player.Contains(t.Assembly.GetName().Name) ? 0 : 1);
        }
#endif

        private static string Ambiguity(string query, IEnumerable<Type> cands)
        {
            var lines = cands.Select(t => $"{t.FullName} (assembly {t.Assembly.GetName().Name})");
            return $"Multiple component types matched '{query}':\n - " + string.Join("\n - ", lines) +
                   "\nProvide a fully qualified type name to disambiguate.";
        }

        /// <summary>
        /// Gets all accessible property and field names from a component type.
        /// </summary>
        public static List<string> GetAllComponentProperties(Type componentType)
        {
            if (componentType == null) return new List<string>();

            var properties = componentType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                                         .Where(p => p.CanRead && p.CanWrite)
                                         .Select(p => p.Name);

            var fields = componentType.GetFields(BindingFlags.Public | BindingFlags.Instance)
                                     .Where(f => !f.IsInitOnly && !f.IsLiteral)
                                     .Select(f => f.Name);

            // Also include SerializeField private fields (common in Unity)
            var serializeFields = componentType.GetFields(BindingFlags.NonPublic | BindingFlags.Instance)
                                              .Where(f => f.GetCustomAttribute<SerializeField>() != null)
                                              .Select(f => f.Name);

            return properties.Concat(fields).Concat(serializeFields).Distinct().OrderBy(x => x).ToList();
        }

        /// <summary>
        /// Uses AI to suggest the most likely property matches for a user's input.
        /// </summary>
        public static List<string> GetAIPropertySuggestions(string userInput, List<string> availableProperties)
        {
            if (string.IsNullOrWhiteSpace(userInput) || !availableProperties.Any())
                return new List<string>();

            // Simple caching to avoid repeated AI calls for the same input
            var cacheKey = $"{userInput.ToLowerInvariant()}:{string.Join(",", availableProperties)}";
            if (PropertySuggestionCache.TryGetValue(cacheKey, out var cached))
                return cached;

            try
            {
                var prompt = $"A Unity developer is trying to set a component property but used an incorrect name.\n\n" +
                             $"User requested: \"{userInput}\"\n" +
                             $"Available properties: [{string.Join(", ", availableProperties)}]\n\n" +
                             $"Find 1-3 most likely matches considering:\n" +
                             $"- Unity Inspector display names vs actual field names (e.g., \"Max Reach Distance\" → \"maxReachDistance\")\n" +
                             $"- camelCase vs PascalCase vs spaces\n" +
                             $"- Similar meaning/semantics\n" +
                             $"- Common Unity naming patterns\n\n" +
                             $"Return ONLY the matching property names, comma-separated, no quotes or explanation.\n" +
                             $"If confidence is low (<70%), return empty string.\n\n" +
                             $"Examples:\n" +
                             $"- \"Max Reach Distance\" → \"maxReachDistance\"\n" +
                             $"- \"Health Points\" → \"healthPoints, hp\"\n" +
                             $"- \"Move Speed\" → \"moveSpeed, movementSpeed\"";

                // For now, we'll use a simple rule-based approach that mimics AI behavior
                // This can be replaced with actual AI calls later
                var suggestions = GetRuleBasedSuggestions(userInput, availableProperties);

                PropertySuggestionCache[cacheKey] = suggestions;
                return suggestions;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[AI Property Matching] Error getting suggestions for '{userInput}': {ex.Message}");
                return new List<string>();
            }
        }

        private static readonly Dictionary<string, List<string>> PropertySuggestionCache = new();

        /// <summary>
        /// Rule-based suggestions that mimic AI behavior for property matching.
        /// This provides immediate value while we could add real AI integration later.
        /// </summary>
        private static List<string> GetRuleBasedSuggestions(string userInput, List<string> availableProperties)
        {
            var suggestions = new List<string>();
            var cleanedInput = userInput.ToLowerInvariant().Replace(" ", "").Replace("-", "").Replace("_", "");

            foreach (var property in availableProperties)
            {
                var cleanedProperty = property.ToLowerInvariant().Replace(" ", "").Replace("-", "").Replace("_", "");

                // Exact match after cleaning
                if (cleanedProperty == cleanedInput)
                {
                    suggestions.Add(property);
                    continue;
                }

                // Check if property contains all words from input
                var inputWords = userInput.ToLowerInvariant().Split(new[] { ' ', '-', '_' }, StringSplitOptions.RemoveEmptyEntries);
                if (inputWords.All(word => cleanedProperty.Contains(word.ToLowerInvariant())))
                {
                    suggestions.Add(property);
                    continue;
                }

                // Levenshtein distance for close matches
                if (LevenshteinDistance(cleanedInput, cleanedProperty) <= Math.Max(2, cleanedInput.Length / 4))
                {
                    suggestions.Add(property);
                }
            }

            // Prioritize exact matches, then by similarity
            return suggestions.OrderBy(s => LevenshteinDistance(cleanedInput, s.ToLowerInvariant().Replace(" ", "")))
                             .Take(3)
                             .ToList();
        }

        /// <summary>
        /// Calculates Levenshtein distance between two strings for similarity matching.
        /// </summary>
        private static int LevenshteinDistance(string s1, string s2)
        {
            if (string.IsNullOrEmpty(s1)) return s2?.Length ?? 0;
            if (string.IsNullOrEmpty(s2)) return s1.Length;

            var matrix = new int[s1.Length + 1, s2.Length + 1];

            for (int i = 0; i <= s1.Length; i++) matrix[i, 0] = i;
            for (int j = 0; j <= s2.Length; j++) matrix[0, j] = j;

            for (int i = 1; i <= s1.Length; i++)
            {
                for (int j = 1; j <= s2.Length; j++)
                {
                    int cost = (s2[j - 1] == s1[i - 1]) ? 0 : 1;
                    matrix[i, j] = Math.Min(Math.Min(
                        matrix[i - 1, j] + 1,      // deletion
                        matrix[i, j - 1] + 1),     // insertion
                        matrix[i - 1, j - 1] + cost); // substitution
                }
            }

            return matrix[s1.Length, s2.Length];
        }

        // Removed duplicate ParseVector3 - using the one at line 1114

        // Removed GetGameObjectData, GetComponentData, and related private helpers/caching/serializer setup.
        // They are now in Helpers.GameObjectSerializer
    }
}