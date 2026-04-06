using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MCPForUnity.Editor.Helpers;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using MCPForUnity.Runtime.Helpers;

namespace MCPForUnity.Editor.Tools
{
    /// <summary>
    /// Tool for managing components on GameObjects.
    /// Actions: add, remove, set_property, get_referenceable, set_reference, batch_wire
    /// 
    /// This is a focused tool for component lifecycle operations.
    /// For reading component data, use the unity://scene/gameobject/{id}/components resource.
    /// </summary>
    [McpForUnityTool("manage_components")]
    public static class ManageComponents
    {
        /// <summary>
        /// Handles the manage_components command.
        /// </summary>
        /// <param name="params">Command parameters</param>
        /// <returns>Result of the component operation</returns>
        public static object HandleCommand(JObject @params)
        {
            if (@params == null)
            {
                return new ErrorResponse("Parameters cannot be null.");
            }

            string action = ParamCoercion.CoerceString(@params["action"], null)?.ToLowerInvariant();
            if (string.IsNullOrEmpty(action))
            {
                return new ErrorResponse("'action' parameter is required (add, remove, set_property, get_referenceable, set_reference, batch_wire).");
            }

            // Target resolution
            JToken targetToken = @params["target"];
            string searchMethod = ParamCoercion.CoerceString(@params["searchMethod"] ?? @params["search_method"], null);

            if (targetToken == null)
            {
                return new ErrorResponse("'target' parameter is required.");
            }

            try
            {
                return action switch
                {
                    "add" => AddComponent(@params, targetToken, searchMethod),
                    "remove" => RemoveComponent(@params, targetToken, searchMethod),
                    "set_property" => SetProperty(@params, targetToken, searchMethod),
                    "get_referenceable" => GetReferenceable(@params, targetToken, searchMethod),
                    "set_reference" => SetReference(@params, targetToken, searchMethod),
                    "batch_wire" => BatchWire(@params, targetToken, searchMethod),
                    _ => new ErrorResponse($"Unknown action: '{action}'. Supported actions: add, remove, set_property, get_referenceable, set_reference, batch_wire")
                };
            }
            catch (Exception e)
            {
                McpLog.Error($"[ManageComponents] Action '{action}' failed: {e}");
                return new ErrorResponse($"Internal error processing action '{action}': {e.Message}");
            }
        }

        #region Action Implementations

        private static object AddComponent(JObject @params, JToken targetToken, string searchMethod)
        {
            GameObject targetGo = FindTarget(targetToken, searchMethod);
            if (targetGo == null)
            {
                return new ErrorResponse($"Target GameObject ('{targetToken}') not found using method '{searchMethod ?? "default"}'.");
            }

            string componentTypeName = ParamCoercion.CoerceString(@params["componentType"] ?? @params["component_type"], null);
            if (string.IsNullOrEmpty(componentTypeName))
            {
                return new ErrorResponse("'componentType' parameter is required for 'add' action.");
            }

            // Resolve component type using unified type resolver
            Type type = UnityTypeResolver.ResolveComponent(componentTypeName);
            if (type == null)
            {
                return new ErrorResponse($"Component type '{componentTypeName}' not found. Use a fully-qualified name if needed.");
            }

            // Use ComponentOps for the actual operation
            Component newComponent = ComponentOps.AddComponent(targetGo, type, out string error);
            if (newComponent == null)
            {
                return new ErrorResponse(error ?? $"Failed to add component '{componentTypeName}'.");
            }

            // When adding VFX-related components (ParticleSystem, LineRenderer, TrailRenderer),
            // ensure the renderer has a material compatible with the active render pipeline.
            // Without this, newly added ParticleSystems in URP/HDRP projects get Unity's default
            // Built-in RP particle material, which renders as magenta.
            EnsureVfxRendererMaterial(targetGo, newComponent);

            // Set properties if provided
            JObject properties = @params["properties"] as JObject ?? @params["componentProperties"] as JObject;
            if (properties != null && properties.HasValues)
            {
                // Record for undo before modifying properties
                Undo.RecordObject(newComponent, "Modify Component Properties");
                SetPropertiesOnComponent(newComponent, properties);
            }

            EditorUtility.SetDirty(targetGo);
            MarkOwningSceneDirty(targetGo);

            return new
            {
                success = true,
                message = $"Component '{componentTypeName}' added to '{targetGo.name}'.",
                data = new
                {
                    instanceID = targetGo.GetInstanceIDCompat(),
                    componentType = type.FullName,
                    componentInstanceID = newComponent.GetInstanceIDCompat()
                }
            };
        }

        private static object RemoveComponent(JObject @params, JToken targetToken, string searchMethod)
        {
            GameObject targetGo = FindTarget(targetToken, searchMethod);
            if (targetGo == null)
            {
                return new ErrorResponse($"Target GameObject ('{targetToken}') not found using method '{searchMethod ?? "default"}'.");
            }

            string componentTypeName = ParamCoercion.CoerceString(@params["componentType"] ?? @params["component_type"], null);
            if (string.IsNullOrEmpty(componentTypeName))
            {
                return new ErrorResponse("'componentType' parameter is required for 'remove' action.");
            }

            // Resolve component type using unified type resolver
            Type type = UnityTypeResolver.ResolveComponent(componentTypeName);
            if (type == null)
            {
                return new ErrorResponse($"Component type '{componentTypeName}' not found.");
            }

            int? componentIndex = ParamCoercion.CoerceIntNullable(@params["componentIndex"] ?? @params["component_index"]);
            if (componentIndex.HasValue)
            {
                var components = targetGo.GetComponents(type);
                if (componentIndex.Value < 0 || componentIndex.Value >= components.Length)
                    return new ErrorResponse($"component_index {componentIndex.Value} out of range. Found {components.Length} '{componentTypeName}' component(s).");
                if (type == typeof(Transform) || type == typeof(RectTransform))
                    return new ErrorResponse("Cannot remove Transform or RectTransform components.");
                Undo.DestroyObjectImmediate(components[componentIndex.Value]);
                EditorUtility.SetDirty(targetGo);
                MarkOwningSceneDirty(targetGo);
                return new
                {
                    success = true,
                    message = $"Component '{componentTypeName}' (index {componentIndex.Value}) removed from '{targetGo.name}'.",
                    data = new { instanceID = targetGo.GetInstanceIDCompat(), componentIndex = componentIndex.Value }
                };
            }

            // Use ComponentOps for the actual operation (removes first instance)
            bool removed = ComponentOps.RemoveComponent(targetGo, type, out string error);
            if (!removed)
            {
                return new ErrorResponse(error ?? $"Failed to remove component '{componentTypeName}'.");
            }

            EditorUtility.SetDirty(targetGo);
            MarkOwningSceneDirty(targetGo);

            return new
            {
                success = true,
                message = $"Component '{componentTypeName}' removed from '{targetGo.name}'.",
                data = new
                {
                    instanceID = targetGo.GetInstanceIDCompat()
                }
            };
        }

        private static object SetProperty(JObject @params, JToken targetToken, string searchMethod)
        {
            GameObject targetGo = FindTarget(targetToken, searchMethod);
            if (targetGo == null)
            {
                return new ErrorResponse($"Target GameObject ('{targetToken}') not found using method '{searchMethod ?? "default"}'.");
            }

            string componentType = ParamCoercion.CoerceString(@params["componentType"] ?? @params["component_type"], null);
            if (string.IsNullOrEmpty(componentType))
            {
                return new ErrorResponse("'componentType' parameter is required for 'set_property' action.");
            }

            // Resolve component type using unified type resolver
            Type type = UnityTypeResolver.ResolveComponent(componentType);
            if (type == null)
            {
                return new ErrorResponse($"Component type '{componentType}' not found.");
            }

            int? componentIndex = ParamCoercion.CoerceIntNullable(@params["componentIndex"] ?? @params["component_index"]);
            Component component;
            if (componentIndex.HasValue)
            {
                var components = targetGo.GetComponents(type);
                if (componentIndex.Value < 0 || componentIndex.Value >= components.Length)
                    return new ErrorResponse($"component_index {componentIndex.Value} out of range. Found {components.Length} '{componentType}' component(s).");
                component = components[componentIndex.Value];
            }
            else
            {
                component = targetGo.GetComponent(type);
            }
            if (component == null)
            {
                return new ErrorResponse($"Component '{componentType}' not found on '{targetGo.name}'.");
            }

            // Get property and value
            string propertyName = ParamCoercion.CoerceString(@params["property"], null);
            JToken valueToken = @params["value"];

            // Support both single property or properties object
            JObject properties = @params["properties"] as JObject;

            if (string.IsNullOrEmpty(propertyName) && (properties == null || !properties.HasValues))
            {
                return new ErrorResponse("Either 'property'+'value' or 'properties' object is required for 'set_property' action.");
            }

            var errors = new List<string>();

            try
            {
                Undo.RecordObject(component, $"Set property on {componentType}");

                if (!string.IsNullOrEmpty(propertyName) && valueToken != null)
                {
                    // Single property mode
                    var error = TrySetProperty(component, propertyName, valueToken);
                    if (error != null)
                    {
                        errors.Add(error);
                    }
                }

                if (properties != null && properties.HasValues)
                {
                    // Multiple properties mode
                    foreach (var prop in properties.Properties())
                    {
                        var error = TrySetProperty(component, prop.Name, prop.Value);
                        if (error != null)
                        {
                            errors.Add(error);
                        }
                    }
                }

                EditorUtility.SetDirty(component);
                MarkOwningSceneDirty(targetGo);

                if (errors.Count > 0)
                {
                    return new
                    {
                        success = false,
                        message = $"Some properties failed to set on '{componentType}'.",
                        data = new
                        {
                            instanceID = targetGo.GetInstanceIDCompat(),
                            errors = errors
                        }
                    };
                }

                return new
                {
                    success = true,
                    message = $"Properties set on component '{componentType}' on '{targetGo.name}'.",
                    data = new
                    {
                        instanceID = targetGo.GetInstanceIDCompat()
                    }
                };
            }
            catch (Exception e)
            {
                return new ErrorResponse($"Error setting properties on component '{componentType}': {e.Message}");
            }
        }

        private static object GetReferenceable(JObject @params, JToken targetToken, string searchMethod)
        {
            if (!TryGetComponentAndObjectReferenceProperty(@params, targetToken, searchMethod, "get_referenceable",
                out GameObject targetGo, out Component component, out SerializedObject serializedObject, out SerializedProperty property, out Type expectedType, out ErrorResponse error))
            {
                return error;
            }

            bool includeScene = ParamCoercion.CoerceBool(@params["include_scene"] ?? @params["includeScene"], true);
            bool includeAssets = ParamCoercion.CoerceBool(@params["include_assets"] ?? @params["includeAssets"], true);
            int limit = ParamCoercion.CoerceInt(@params["limit"], 0);

            var sceneObjects = includeScene
                ? FindReferenceableSceneObjects(expectedType, limit).ToList()
                : new List<object>();
            var assets = includeAssets
                ? FindReferenceableAssets(expectedType, limit).ToList()
                : new List<object>();

            return new
            {
                success = true,
                message = $"Referenceable targets resolved for property '{property.propertyPath}' on '{component.GetType().Name}'.",
                data = new
                {
                    expected_type = expectedType?.FullName,
                    current_value = DescribeObjectReference(property.objectReferenceValue),
                    scene_objects = sceneObjects,
                    assets = assets
                }
            };
        }

        private static object SetReference(JObject @params, JToken targetToken, string searchMethod)
        {
            if (!TryGetComponentAndObjectReferenceProperty(@params, targetToken, searchMethod, "set_reference",
                out GameObject targetGo, out Component component, out SerializedObject serializedObject, out SerializedProperty property, out Type expectedType, out ErrorResponse error))
            {
                return error;
            }

            var validation = ValidateReferenceAssignment(component, property.propertyPath, expectedType, @params);
            if (!validation.Success)
            {
                return new ErrorResponse(validation.Error);
            }

            var previousValue = DescribeObjectReference(property.objectReferenceValue);

            Undo.RecordObject(component, $"Set reference {property.propertyPath}");
            property.objectReferenceValue = validation.ResolvedObject;
            serializedObject.ApplyModifiedProperties();
            EditorUtility.SetDirty(component);
            MarkOwningSceneDirty(targetGo);

            return new
            {
                success = true,
                message = $"Reference '{property.propertyPath}' updated on component '{component.GetType().Name}' on '{targetGo.name}'.",
                data = new
                {
                    property = property.propertyPath,
                    previous_value = previousValue,
                    new_value = DescribeObjectReference(validation.ResolvedObject)
                }
            };
        }

        private static object BatchWire(JObject @params, JToken targetToken, string searchMethod)
        {
            GameObject targetGo = FindTarget(targetToken, searchMethod);
            if (targetGo == null)
            {
                return new ErrorResponse($"Target GameObject ('{targetToken}') not found using method '{searchMethod ?? "default"}'.");
            }

            string componentType = ParamCoercion.CoerceString(@params["componentType"] ?? @params["component_type"], null);
            if (string.IsNullOrEmpty(componentType))
            {
                return new ErrorResponse("'componentType' parameter is required for 'batch_wire' action.");
            }

            Type type = UnityTypeResolver.ResolveComponent(componentType);
            if (type == null)
            {
                return new ErrorResponse($"Component type '{componentType}' not found.");
            }

            Component component = targetGo.GetComponent(type);
            if (component == null)
            {
                return new ErrorResponse($"Component '{componentType}' not found on '{targetGo.name}'.");
            }

            JArray references = @params["references"] as JArray;
            if (references == null || !references.HasValues)
            {
                return new ErrorResponse("'references' array is required for 'batch_wire' action.");
            }

            bool atomic = ParamCoercion.CoerceBool(@params["atomic"], true);
            var validations = new List<ReferenceAssignmentValidation>();
            var results = new List<BatchWireResult>();

            foreach (JToken referenceToken in references)
            {
                JObject referenceParams = referenceToken as JObject;
                if (referenceParams == null)
                {
                    results.Add(new BatchWireResult
                    {
                        Success = false,
                        Error = "Each reference entry must be an object."
                    });
                    continue;
                }

                string propertyName = ParamCoercion.CoerceString(referenceParams["property_name"] ?? referenceParams["property"], null);
                if (string.IsNullOrEmpty(propertyName))
                {
                    results.Add(new BatchWireResult
                    {
                        Success = false,
                        Error = "Each reference entry requires 'property_name'."
                    });
                    continue;
                }

                Type expectedType = GetFieldType(component, propertyName);
                if (expectedType == null)
                {
                    results.Add(new BatchWireResult
                    {
                        Property = propertyName,
                        Success = false,
                        Error = $"Property '{propertyName}' was not found or is not an object reference."
                    });
                    continue;
                }

                var validation = ValidateReferenceAssignment(component, propertyName, expectedType, referenceParams);
                validations.Add(validation);
                results.Add(new BatchWireResult
                {
                    Property = propertyName,
                    Success = validation.Success,
                    Error = validation.Error,
                    NewValue = validation.Success ? DescribeObjectReference(validation.ResolvedObject) : null
                });
            }

            if (atomic && results.Any(r => !r.Success))
            {
                return new
                {
                    success = false,
                    message = "Batch wire validation failed. No references were applied.",
                    data = new
                    {
                        total = references.Count,
                        succeeded = 0,
                        failed = results.Count(r => !r.Success),
                        results = results.Select(r => r.ToResponse())
                    }
                };
            }

            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName($"Batch wire references on {component.GetType().Name}");
            int succeeded = 0;
            int failed = 0;

            try
            {
                foreach (var validation in validations)
                {
                    if (!validation.Success)
                    {
                        failed++;
                        continue;
                    }

                    var serializedObject = new SerializedObject(component);
                    var property = serializedObject.FindProperty(validation.PropertyName);
                    if (property == null || property.propertyType != SerializedPropertyType.ObjectReference)
                    {
                        var result = results.FirstOrDefault(r => r.Property == validation.PropertyName);
                        if (result != null)
                        {
                            result.Success = false;
                            result.Error = $"Property '{validation.PropertyName}' was not found or is not an object reference during apply.";
                            result.NewValue = null;
                        }
                        failed++;
                        continue;
                    }

                    Undo.RecordObject(component, $"Set reference {validation.PropertyName}");
                    property.objectReferenceValue = validation.ResolvedObject;
                    serializedObject.ApplyModifiedProperties();
                    var successResult = results.FirstOrDefault(r => r.Property == validation.PropertyName);
                    if (successResult != null)
                    {
                        successResult.Success = true;
                        successResult.Error = null;
                        successResult.NewValue = DescribeObjectReference(property.objectReferenceValue);
                    }
                    succeeded++;
                }

                if (succeeded > 0)
                {
                    EditorUtility.SetDirty(component);
                    MarkOwningSceneDirty(targetGo);
                }
            }
            finally
            {
                Undo.CollapseUndoOperations(undoGroup);
            }

            if (!atomic)
            {
                failed = results.Count(r => !r.Success);
            }

            return new
            {
                success = failed == 0,
                message = failed == 0
                    ? $"Batch wired {succeeded} reference(s) on component '{component.GetType().Name}'."
                    : $"Batch wire completed with {failed} failure(s) on component '{component.GetType().Name}'.",
                data = new
                {
                    total = references.Count,
                    succeeded = succeeded,
                    failed = failed,
                    results = results.Select(r => r.ToResponse())
                }
            };
        }

        #endregion

        #region Helpers

        /// <summary>
        /// When a VFX-capable component is added (ParticleSystem, LineRenderer, TrailRenderer),
        /// ensures its renderer material is valid for the active render pipeline.
        /// This prevents magenta rendering in URP/HDRP projects where the default built-in
        /// particle/line materials use incompatible shaders.
        /// </summary>
        private static void EnsureVfxRendererMaterial(GameObject go, Component addedComponent)
        {
            Renderer renderer = null;

            if (addedComponent is ParticleSystem ps)
            {
                renderer = go.GetComponent<ParticleSystemRenderer>();

                // Apply sensible defaults so newly added ParticleSystems aren't oversized.
                // These are overridden by any subsequent particle_set_* calls.
                RendererHelpers.SetSensibleParticleDefaults(ps);
            }
            else if (addedComponent is Renderer r)
            {
                // Covers LineRenderer, TrailRenderer, and any other Renderer subclass
                renderer = r;
            }

            if (renderer != null)
            {
                var result = RendererHelpers.EnsureMaterial(renderer);
                if (result.MaterialReplaced)
                {
                    McpLog.Info($"[ManageComponents] Auto-assigned pipeline-compatible material to {renderer.GetType().Name} on '{go.name}' (reason: {result.ReplacementReason}).");
                }
            }
        }

        /// <summary>
        /// Marks the appropriate scene as dirty for the given GameObject.
        /// Handles both regular scenes and prefab stages.
        /// </summary>
        private static void MarkOwningSceneDirty(GameObject targetGo)
        {
            var prefabStage = PrefabStageUtility.GetCurrentPrefabStage();
            if (prefabStage != null)
            {
                EditorSceneManager.MarkSceneDirty(prefabStage.scene);
            }
            else
            {
                EditorSceneManager.MarkSceneDirty(targetGo.scene);
            }
        }

        private static GameObject FindTarget(JToken targetToken, string searchMethod)
        {
            if (targetToken == null)
                return null;

            // Try instance ID first
            if (targetToken.Type == JTokenType.Integer)
            {
                int instanceId = targetToken.Value<int>();
                return GameObjectLookup.FindById(instanceId);
            }

            string targetStr = targetToken.ToString();

            // Try parsing as instance ID
            if (int.TryParse(targetStr, out int parsedId))
            {
                var byId = GameObjectLookup.FindById(parsedId);
                if (byId != null)
                    return byId;
            }

            // Use GameObjectLookup for search
            return GameObjectLookup.FindByTarget(targetToken, searchMethod ?? "by_name", true);
        }

        private static void SetPropertiesOnComponent(Component component, JObject properties)
        {
            if (component == null || properties == null)
                return;

            var errors = new List<string>();
            foreach (var prop in properties.Properties())
            {
                var error = TrySetProperty(component, prop.Name, prop.Value);
                if (error != null)
                    errors.Add(error);
            }
            
            if (errors.Count > 0)
            {
                McpLog.Warn($"[ManageComponents] Some properties failed to set on {component.GetType().Name}: {string.Join(", ", errors)}");
            }
        }

        /// <summary>
        /// Attempts to set a property or field on a component.
        /// Delegates to ComponentOps.SetProperty for unified implementation.
        /// </summary>
        private static string TrySetProperty(Component component, string propertyName, JToken value)
        {
            if (component == null || string.IsNullOrEmpty(propertyName))
                return "Invalid component or property name";

            if (ComponentOps.SetProperty(component, propertyName, value, out string error))
            {
                return null; // Success
            }

            McpLog.Warn($"[ManageComponents] {error}");
            return error;
        }

        private static bool TryGetComponentAndObjectReferenceProperty(JObject @params, JToken targetToken, string searchMethod, string actionName,
            out GameObject targetGo, out Component component, out SerializedObject serializedObject, out SerializedProperty property, out Type expectedType, out ErrorResponse error)
        {
            targetGo = null;
            component = null;
            serializedObject = null;
            property = null;
            expectedType = null;
            error = null;

            targetGo = FindTarget(targetToken, searchMethod);
            if (targetGo == null)
            {
                error = new ErrorResponse($"Target GameObject ('{targetToken}') not found using method '{searchMethod ?? "default"}'.");
                return false;
            }

            string componentType = ParamCoercion.CoerceString(@params["componentType"] ?? @params["component_type"], null);
            if (string.IsNullOrEmpty(componentType))
            {
                error = new ErrorResponse($"'componentType' parameter is required for '{actionName}' action.");
                return false;
            }

            Type type = UnityTypeResolver.ResolveComponent(componentType);
            if (type == null)
            {
                error = new ErrorResponse($"Component type '{componentType}' not found.");
                return false;
            }

            component = targetGo.GetComponent(type);
            if (component == null)
            {
                error = new ErrorResponse($"Component '{componentType}' not found on '{targetGo.name}'.");
                return false;
            }

            string propertyName = ParamCoercion.CoerceString(@params["property"] ?? @params["property_name"], null);
            if (string.IsNullOrEmpty(propertyName))
            {
                error = new ErrorResponse($"'property' parameter is required for '{actionName}' action.");
                return false;
            }

            serializedObject = new SerializedObject(component);
            property = serializedObject.FindProperty(propertyName);
            if (property == null)
            {
                error = new ErrorResponse($"Property '{propertyName}' not found on component '{component.GetType().Name}'.");
                return false;
            }

            if (property.propertyType != SerializedPropertyType.ObjectReference)
            {
                error = new ErrorResponse($"Property '{propertyName}' on component '{component.GetType().Name}' is not an object reference.");
                return false;
            }

            expectedType = GetFieldType(component, propertyName);
            if (expectedType == null)
            {
                error = new ErrorResponse($"Unable to determine reference type for property '{propertyName}' on component '{component.GetType().Name}'.");
                return false;
            }

            return true;
        }

        private static Type GetFieldType(Component component, string propertyName)
        {
            var so = new SerializedObject(component);
            var prop = so.FindProperty(propertyName);
            if (prop == null || prop.propertyType != SerializedPropertyType.ObjectReference)
                return null;

            BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.IgnoreCase;
            string normalizedName = ParamCoercion.NormalizePropertyName(propertyName);
            var field = component.GetType().GetField(propertyName, flags)
                        ?? component.GetType().GetField(normalizedName, flags);
            return field?.FieldType;
        }

        private static UnityEngine.Object ResolveReference(JObject refParams)
        {
            var instanceId = refParams["reference_instance_id"]?.Value<int>();
            if (instanceId.HasValue)
                return GameObjectLookup.ResolveInstanceID(instanceId.Value);

            var assetPath = refParams["reference_asset_path"]?.Value<string>();
            if (!string.IsNullOrEmpty(assetPath))
                return AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath);

            var refPath = refParams["reference_path"]?.Value<string>();
            if (!string.IsNullOrEmpty(refPath))
                return GameObjectLookup.FindByTarget(new JValue(refPath), "by_path", true) ?? GameObject.Find(refPath);

            return null;
        }

        private static IEnumerable<object> FindReferenceableSceneObjects(Type expectedType, int limit)
        {
            int count = 0;
            foreach (var gameObject in GameObjectLookup.GetAllSceneObjects(true))
            {
                var candidate = ConvertResolvedObject(gameObject, expectedType);
                if (candidate == null)
                    continue;

                yield return DescribeObjectReference(candidate);
                count++;
                if (limit > 0 && count >= limit)
                    yield break;
            }
        }

        private static IEnumerable<object> FindReferenceableAssets(Type expectedType, int limit)
        {
            int count = 0;
            var seen = new HashSet<int>();

            foreach (string guid in AssetDatabase.FindAssets(BuildAssetSearchFilter(expectedType)))
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                foreach (var candidate in LoadReferenceableAssetsAtPath(assetPath, expectedType))
                {
                    if (candidate == null)
                        continue;

                    int instanceId = candidate.GetInstanceID();
                    if (!seen.Add(instanceId))
                        continue;

                    yield return DescribeObjectReference(candidate);
                    count++;
                    if (limit > 0 && count >= limit)
                        yield break;
                }
            }
        }

        private static string BuildAssetSearchFilter(Type expectedType)
        {
            if (expectedType == null)
                return string.Empty;

            if (typeof(Component).IsAssignableFrom(expectedType))
                return "t:Prefab";

            return $"t:{expectedType.Name}";
        }

        private static IEnumerable<UnityEngine.Object> LoadReferenceableAssetsAtPath(string assetPath, Type expectedType)
        {
            if (typeof(Component).IsAssignableFrom(expectedType))
            {
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
                if (prefab == null)
                    yield break;

                Component component = prefab.GetComponent(expectedType);
                if (component != null)
                    yield return component;
                yield break;
            }

            foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(assetPath))
            {
                if (asset != null && expectedType.IsAssignableFrom(asset.GetType()))
                    yield return asset;
            }
        }

        private static ReferenceAssignmentValidation ValidateReferenceAssignment(Component component, string propertyName, Type expectedType, JObject refParams)
        {
            var serializedObject = new SerializedObject(component);
            var property = serializedObject.FindProperty(propertyName);
            if (property == null || property.propertyType != SerializedPropertyType.ObjectReference)
            {
                return ReferenceAssignmentValidation.Fail(propertyName, $"Property '{propertyName}' was not found or is not an object reference.");
            }

            bool clear = ParamCoercion.CoerceBool(refParams["clear"], false);
            if (clear)
            {
                return ReferenceAssignmentValidation.Ok(propertyName, null);
            }

            if (!HasReferenceSelector(refParams))
            {
                return ReferenceAssignmentValidation.Fail(propertyName, $"Property '{propertyName}' requires one of: reference_path, reference_asset_path, reference_instance_id, or clear.");
            }

            UnityEngine.Object resolved = ResolveReference(refParams);
            if (resolved == null)
            {
                return ReferenceAssignmentValidation.Fail(propertyName, $"Failed to resolve reference target for property '{propertyName}'.");
            }

            UnityEngine.Object converted = ConvertResolvedObject(resolved, expectedType);
            if (converted == null)
            {
                return ReferenceAssignmentValidation.Fail(propertyName,
                    $"Resolved reference '{resolved.name}' is not assignable to '{expectedType.FullName}' for property '{propertyName}'.");
            }

            return ReferenceAssignmentValidation.Ok(propertyName, converted);
        }

        private static bool HasReferenceSelector(JObject refParams)
        {
            return refParams["reference_path"] != null
                   || refParams["reference_asset_path"] != null
                   || refParams["reference_instance_id"] != null
                   || ParamCoercion.CoerceBool(refParams["clear"], false);
        }

        private static UnityEngine.Object ConvertResolvedObject(UnityEngine.Object resolved, Type expectedType)
        {
            if (resolved == null || expectedType == null)
                return null;

            if (expectedType.IsAssignableFrom(resolved.GetType()))
                return resolved;

            if (resolved is GameObject gameObject)
            {
                if (expectedType == typeof(GameObject))
                    return gameObject;

                if (typeof(Component).IsAssignableFrom(expectedType))
                    return gameObject.GetComponent(expectedType);
            }

            if (resolved is Component component)
            {
                if (expectedType == typeof(GameObject))
                    return component.gameObject;

                if (expectedType.IsAssignableFrom(component.GetType()))
                    return component;
            }

            return null;
        }

        private static object DescribeObjectReference(UnityEngine.Object obj)
        {
            if (obj == null)
                return null;

            string assetPath = AssetDatabase.GetAssetPath(obj);
            GameObject gameObject = obj as GameObject;
            if (obj is Component component)
            {
                gameObject = component.gameObject;
            }

            return new
            {
                instance_id = obj.GetInstanceID(),
                name = obj.name,
                type = obj.GetType().FullName,
                path = gameObject != null ? GameObjectLookup.GetGameObjectPath(gameObject) : null,
                asset_path = string.IsNullOrEmpty(assetPath) ? null : assetPath
            };
        }

        private sealed class ReferenceAssignmentValidation
        {
            public string PropertyName { get; private set; }
            public bool Success { get; private set; }
            public string Error { get; private set; }
            public UnityEngine.Object ResolvedObject { get; private set; }

            public static ReferenceAssignmentValidation Ok(string propertyName, UnityEngine.Object resolvedObject)
            {
                return new ReferenceAssignmentValidation
                {
                    PropertyName = propertyName,
                    Success = true,
                    ResolvedObject = resolvedObject
                };
            }

            public static ReferenceAssignmentValidation Fail(string propertyName, string error)
            {
                return new ReferenceAssignmentValidation
                {
                    PropertyName = propertyName,
                    Success = false,
                    Error = error
                };
            }
        }

        private sealed class BatchWireResult
        {
            public string Property { get; set; }
            public bool Success { get; set; }
            public string Error { get; set; }
            public object NewValue { get; set; }

            public object ToResponse()
            {
                return new
                {
                    property = Property,
                    success = Success,
                    error = Error,
                    new_value = NewValue
                };
            }
        }

        #endregion
    }
}
