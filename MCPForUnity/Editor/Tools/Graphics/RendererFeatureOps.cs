using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MCPForUnity.Editor.Helpers;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace MCPForUnity.Editor.Tools.Graphics
{
    internal static class RendererFeatureOps
    {
        // Cached URP types (resolved via reflection to avoid hard dependency)
        private static Type _scriptableRendererDataType;
        private static Type _scriptableRendererFeatureType;
        private static Type _universalRenderPipelineAssetType;
        private static bool _typesResolved;

        private static void EnsureTypes()
        {
            if (_typesResolved) return;
            _typesResolved = true;

            _scriptableRendererDataType = Type.GetType(
                "UnityEngine.Rendering.Universal.ScriptableRendererData, Unity.RenderPipelines.Universal.Runtime");
            _scriptableRendererFeatureType = Type.GetType(
                "UnityEngine.Rendering.Universal.ScriptableRendererFeature, Unity.RenderPipelines.Universal.Runtime");
            _universalRenderPipelineAssetType = Type.GetType(
                "UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset, Unity.RenderPipelines.Universal.Runtime");
        }

        // === feature_list ===
        internal static object ListFeatures(JObject @params)
        {
            var rendererData = GetRendererData(@params);
            if (rendererData == null)
                return new ErrorResponse("Could not find URP ScriptableRendererData. Ensure URP is active.");

            var featuresProp = rendererData.GetType().GetProperty("rendererFeatures",
                BindingFlags.Public | BindingFlags.Instance);
            if (featuresProp == null)
                return new ErrorResponse("rendererFeatures property not found on renderer data.");

            var featuresList = featuresProp.GetValue(rendererData) as System.Collections.IList;
            if (featuresList == null)
                return new { success = true, message = "No renderer features.", data = new { features = new object[0] } };

            var features = new List<object>();
            for (int i = 0; i < featuresList.Count; i++)
            {
                var feature = featuresList[i] as ScriptableObject;
                if (feature == null) continue;

                var isActiveProp = feature.GetType().GetProperty("isActive",
                    BindingFlags.Public | BindingFlags.Instance);

                features.Add(new
                {
                    index = i,
                    name = feature.name,
                    type = feature.GetType().Name,
                    isActive = isActiveProp != null ? (bool)isActiveProp.GetValue(feature) : true,
                    properties = GetFeatureProperties(feature)
                });
            }

            return new
            {
                success = true,
                message = $"Found {features.Count} renderer feature(s).",
                data = new
                {
                    rendererDataName = (rendererData as ScriptableObject)?.name,
                    features
                }
            };
        }

        // === feature_add ===
        internal static object AddFeature(JObject @params)
        {
            var p = new ToolParams(@params);
            string typeName = p.Get("type");
            if (string.IsNullOrEmpty(typeName))
                return new ErrorResponse("'type' parameter required (e.g., 'FullScreenPassRendererFeature', 'RenderObjects').");

            var rendererData = GetRendererData(@params);
            if (rendererData == null)
                return new ErrorResponse("Could not find URP ScriptableRendererData.");

            EnsureTypes();
            if (_scriptableRendererFeatureType == null)
                return new ErrorResponse("ScriptableRendererFeature type not found. Is URP installed?");

            // Resolve the feature type
            var featureType = ResolveFeatureType(typeName);
            if (featureType == null)
            {
                var available = GetAvailableFeatureTypes();
                return new ErrorResponse(
                    $"Feature type '{typeName}' not found. Available: {string.Join(", ", available.Select(t => t.Name))}");
            }

            // Create the feature instance
            var feature = ScriptableObject.CreateInstance(featureType);
            if (feature == null)
                return new ErrorResponse($"Failed to create instance of '{featureType.Name}'.");

            string displayName = p.Get("name") ?? featureType.Name;
            feature.name = displayName;

            // Add to the renderer data asset
            Undo.RecordObject(rendererData as UnityEngine.Object, "Add Renderer Feature");
            AssetDatabase.AddObjectToAsset(feature, rendererData as UnityEngine.Object);

            // Add to the features list via SerializedObject
            using (var so = new SerializedObject(rendererData as UnityEngine.Object))
            {
                var rendererFeaturesProp = so.FindProperty("m_RendererFeatures");
                if (rendererFeaturesProp != null)
                {
                    rendererFeaturesProp.arraySize++;
                    var element = rendererFeaturesProp.GetArrayElementAtIndex(rendererFeaturesProp.arraySize - 1);
                    element.objectReferenceValue = feature;
                    so.ApplyModifiedProperties();
                }

                // Also update the map (m_RendererFeatureMap) if it exists
                var mapProp = so.FindProperty("m_RendererFeatureMap");
                if (mapProp != null)
                {
                    mapProp.arraySize++;
                    var mapElement = mapProp.GetArrayElementAtIndex(mapProp.arraySize - 1);
                    mapElement.longValue = feature.GetInstanceID();
                    so.ApplyModifiedProperties();
                }
            }

            // Configure initial properties if provided
            var propertiesToken = p.GetRaw("properties") as JObject;
            if (propertiesToken != null)
                ApplyFeatureProperties(feature, propertiesToken);

            // Set material if provided (common for FullScreenPass)
            string materialPath = p.Get("material");
            if (!string.IsNullOrEmpty(materialPath))
                TrySetMaterial(feature, materialPath);

            EditorUtility.SetDirty(rendererData as UnityEngine.Object);
            AssetDatabase.SaveAssets();

            return new
            {
                success = true,
                message = $"Added renderer feature '{displayName}' ({featureType.Name}).",
                data = new
                {
                    name = displayName,
                    type = featureType.Name,
                    instanceId = feature.GetInstanceID()
                }
            };
        }

        // === feature_remove ===
        internal static object RemoveFeature(JObject @params)
        {
            var p = new ToolParams(@params);
            int? index = p.GetInt("index");
            string name = p.Get("name");

            var rendererData = GetRendererData(@params);
            if (rendererData == null)
                return new ErrorResponse("Could not find URP ScriptableRendererData.");

            var featuresProp = rendererData.GetType().GetProperty("rendererFeatures",
                BindingFlags.Public | BindingFlags.Instance);
            if (featuresProp == null)
                return new ErrorResponse("rendererFeatures property not found.");

            var featuresList = featuresProp.GetValue(rendererData) as System.Collections.IList;
            if (featuresList == null || featuresList.Count == 0)
                return new ErrorResponse("No renderer features to remove.");

            int targetIndex = ResolveFeatureIndex(featuresList, index, name);
            if (targetIndex < 0)
                return new ErrorResponse($"Feature not found. Specify 'index' (0-{featuresList.Count - 1}) or 'name'.");

            var feature = featuresList[targetIndex] as ScriptableObject;
            string featureName = feature?.name ?? "Unknown";

            Undo.RecordObject(rendererData as UnityEngine.Object, "Remove Renderer Feature");

            // Remove from the list via SerializedObject
            using (var so = new SerializedObject(rendererData as UnityEngine.Object))
            {
                var rendererFeaturesPropSo = so.FindProperty("m_RendererFeatures");
                if (rendererFeaturesPropSo != null)
                {
                    rendererFeaturesPropSo.DeleteArrayElementAtIndex(targetIndex);
                    // SerializedProperty.DeleteArrayElementAtIndex sets to null first for ObjectReference
                    if (rendererFeaturesPropSo.arraySize > targetIndex)
                    {
                        var element = rendererFeaturesPropSo.GetArrayElementAtIndex(targetIndex);
                        if (element.objectReferenceValue == null)
                            rendererFeaturesPropSo.DeleteArrayElementAtIndex(targetIndex);
                    }
                    so.ApplyModifiedProperties();
                }

                // Clean up the map
                var mapProp = so.FindProperty("m_RendererFeatureMap");
                if (mapProp != null && targetIndex < mapProp.arraySize)
                {
                    mapProp.DeleteArrayElementAtIndex(targetIndex);
                    so.ApplyModifiedProperties();
                }
            }

            // Remove the sub-asset
            if (feature != null)
                AssetDatabase.RemoveObjectFromAsset(feature);

            EditorUtility.SetDirty(rendererData as UnityEngine.Object);
            AssetDatabase.SaveAssets();

            return new
            {
                success = true,
                message = $"Removed renderer feature '{featureName}' at index {targetIndex}."
            };
        }

        // === feature_configure ===
        internal static object ConfigureFeature(JObject @params)
        {
            var p = new ToolParams(@params);
            int? index = p.GetInt("index");
            string name = p.Get("name");
            var propertiesToken = (p.GetRaw("properties") ?? p.GetRaw("settings")) as JObject;

            if (propertiesToken == null)
                return new ErrorResponse("'properties' (or 'settings') dict is required.");

            var rendererData = GetRendererData(@params);
            if (rendererData == null)
                return new ErrorResponse("Could not find URP ScriptableRendererData.");

            var featuresProp = rendererData.GetType().GetProperty("rendererFeatures",
                BindingFlags.Public | BindingFlags.Instance);
            var featuresList = featuresProp?.GetValue(rendererData) as System.Collections.IList;
            if (featuresList == null || featuresList.Count == 0)
                return new ErrorResponse("No renderer features to configure.");

            int targetIndex = ResolveFeatureIndex(featuresList, index, name);
            if (targetIndex < 0)
                return new ErrorResponse($"Feature not found. Specify 'index' (0-{featuresList.Count - 1}) or 'name'.");

            var feature = featuresList[targetIndex] as ScriptableObject;
            if (feature == null)
                return new ErrorResponse($"Feature at index {targetIndex} is null.");

            Undo.RecordObject(feature, "Configure Renderer Feature");
            var result = ApplyFeatureProperties(feature, propertiesToken);
            EditorUtility.SetDirty(feature);
            EditorUtility.SetDirty(rendererData as UnityEngine.Object);
            AssetDatabase.SaveAssets();

            return new
            {
                success = true,
                message = $"Configured '{feature.name}': {result.changed.Count} set, {result.failed.Count} failed.",
                data = new { result.changed, result.failed }
            };
        }

        // === feature_toggle ===
        internal static object ToggleFeature(JObject @params)
        {
            var p = new ToolParams(@params);
            int? index = p.GetInt("index");
            string name = p.Get("name");
            bool? active = p.GetBool("active");

            var rendererData = GetRendererData(@params);
            if (rendererData == null)
                return new ErrorResponse("Could not find URP ScriptableRendererData.");

            var featuresProp = rendererData.GetType().GetProperty("rendererFeatures",
                BindingFlags.Public | BindingFlags.Instance);
            var featuresList = featuresProp?.GetValue(rendererData) as System.Collections.IList;
            if (featuresList == null || featuresList.Count == 0)
                return new ErrorResponse("No renderer features.");

            int targetIndex = ResolveFeatureIndex(featuresList, index, name);
            if (targetIndex < 0)
                return new ErrorResponse($"Feature not found. Specify 'index' or 'name'.");

            var feature = featuresList[targetIndex] as ScriptableObject;
            if (feature == null)
                return new ErrorResponse($"Feature at index {targetIndex} is null.");

            // ScriptableRendererFeature.SetActive(bool) is public
            var setActiveMethod = feature.GetType().GetMethod("SetActive",
                BindingFlags.Public | BindingFlags.Instance);
            if (setActiveMethod == null)
                return new ErrorResponse("SetActive method not found on feature.");

            bool newState = active ?? true;
            Undo.RecordObject(feature, "Toggle Renderer Feature");
            setActiveMethod.Invoke(feature, new object[] { newState });
            EditorUtility.SetDirty(feature);
            EditorUtility.SetDirty(rendererData as UnityEngine.Object);
            AssetDatabase.SaveAssets();

            return new
            {
                success = true,
                message = $"Feature '{feature.name}' {(newState ? "enabled" : "disabled")}."
            };
        }

        // === feature_reorder ===
        internal static object ReorderFeatures(JObject @params)
        {
            var p = new ToolParams(@params);
            var orderToken = p.GetRaw("order") as JArray;
            if (orderToken == null)
                return new ErrorResponse("'order' parameter required (array of indices, e.g. [2, 0, 1]).");

            var rendererData = GetRendererData(@params);
            if (rendererData == null)
                return new ErrorResponse("Could not find URP ScriptableRendererData.");

            var featuresProp = rendererData.GetType().GetProperty("rendererFeatures",
                BindingFlags.Public | BindingFlags.Instance);
            var featuresList = featuresProp?.GetValue(rendererData) as System.Collections.IList;
            if (featuresList == null || featuresList.Count == 0)
                return new ErrorResponse("No renderer features to reorder.");

            var newOrder = orderToken.Select(t => (int)t).ToList();
            if (newOrder.Count != featuresList.Count)
                return new ErrorResponse(
                    $"Order array length ({newOrder.Count}) must match feature count ({featuresList.Count}).");

            // Validate all indices are present
            var sorted = newOrder.OrderBy(x => x).ToList();
            for (int i = 0; i < sorted.Count; i++)
            {
                if (sorted[i] != i)
                    return new ErrorResponse("Order array must contain each index exactly once (0 to N-1).");
            }

            Undo.RecordObject(rendererData as UnityEngine.Object, "Reorder Renderer Features");

            using (var so = new SerializedObject(rendererData as UnityEngine.Object))
            {
                var rendererFeaturesPropSo = so.FindProperty("m_RendererFeatures");
                if (rendererFeaturesPropSo == null)
                    return new ErrorResponse("m_RendererFeatures property not found.");

                // Read current features
                var current = new UnityEngine.Object[featuresList.Count];
                for (int i = 0; i < featuresList.Count; i++)
                    current[i] = rendererFeaturesPropSo.GetArrayElementAtIndex(i).objectReferenceValue;

                // Apply new order
                for (int i = 0; i < newOrder.Count; i++)
                    rendererFeaturesPropSo.GetArrayElementAtIndex(i).objectReferenceValue = current[newOrder[i]];

                // Also reorder the feature map to keep it in sync
                var mapProp = so.FindProperty("m_RendererFeatureMap");
                if (mapProp != null && mapProp.arraySize == featuresList.Count)
                {
                    var currentMap = new long[featuresList.Count];
                    for (int i = 0; i < featuresList.Count; i++)
                        currentMap[i] = mapProp.GetArrayElementAtIndex(i).longValue;
                    for (int i = 0; i < newOrder.Count; i++)
                        mapProp.GetArrayElementAtIndex(i).longValue = currentMap[newOrder[i]];
                }

                so.ApplyModifiedProperties();
            }

            EditorUtility.SetDirty(rendererData as UnityEngine.Object);
            AssetDatabase.SaveAssets();

            return new
            {
                success = true,
                message = $"Reordered {featuresList.Count} renderer features."
            };
        }

        // ==================== Helpers ====================

        private static object GetRendererData(JObject @params)
        {
            EnsureTypes();
            if (_universalRenderPipelineAssetType == null || _scriptableRendererDataType == null)
                return null;

            var pipelineAsset = GraphicsSettings.currentRenderPipeline;
            if (pipelineAsset == null || !_universalRenderPipelineAssetType.IsInstanceOfType(pipelineAsset))
                return null;

            var p = new ToolParams(@params);
            int rendererIndex = p.GetInt("renderer_index") ?? -1;

            // Get renderer data from the URP asset
            // Try scriptableRendererData property or GetRenderer method
            if (rendererIndex >= 0)
            {
                // Use SerializedObject to get specific renderer
                using (var so = new SerializedObject(pipelineAsset))
                {
                    var renderersProp = so.FindProperty("m_RendererDataList");
                    if (renderersProp == null || rendererIndex >= renderersProp.arraySize)
                        return null;

                    var element = renderersProp.GetArrayElementAtIndex(rendererIndex);
                    return element.objectReferenceValue;
                }
            }

            // Default: get the active renderer (index from m_DefaultRendererIndex)
            using (var so = new SerializedObject(pipelineAsset))
            {
                var defaultIndex = so.FindProperty("m_DefaultRendererIndex");
                int idx = defaultIndex != null ? defaultIndex.intValue : 0;

                var renderersProp = so.FindProperty("m_RendererDataList");
                if (renderersProp != null && idx < renderersProp.arraySize)
                {
                    var element = renderersProp.GetArrayElementAtIndex(idx);
                    return element.objectReferenceValue;
                }
            }

            return null;
        }

        private static Type ResolveFeatureType(string typeName)
        {
            EnsureTypes();
            if (_scriptableRendererFeatureType == null) return null;

            var derivedTypes = TypeCache.GetTypesDerivedFrom(_scriptableRendererFeatureType);
            foreach (var t in derivedTypes)
            {
                if (t.IsAbstract) continue;
                if (string.Equals(t.Name, typeName, StringComparison.OrdinalIgnoreCase))
                    return t;
            }

            // Try partial match (e.g., "FullScreenPass" matches "FullScreenPassRendererFeature")
            foreach (var t in derivedTypes)
            {
                if (t.IsAbstract) continue;
                if (t.Name.StartsWith(typeName, StringComparison.OrdinalIgnoreCase))
                    return t;
            }

            return null;
        }

        private static List<Type> GetAvailableFeatureTypes()
        {
            EnsureTypes();
            if (_scriptableRendererFeatureType == null) return new List<Type>();

            return TypeCache.GetTypesDerivedFrom(_scriptableRendererFeatureType)
                .Where(t => !t.IsAbstract && !t.IsGenericType)
                .OrderBy(t => t.Name)
                .ToList();
        }

        private static int ResolveFeatureIndex(System.Collections.IList featuresList, int? index, string name)
        {
            if (index.HasValue && index.Value >= 0 && index.Value < featuresList.Count)
                return index.Value;

            if (!string.IsNullOrEmpty(name))
            {
                for (int i = 0; i < featuresList.Count; i++)
                {
                    var feature = featuresList[i] as ScriptableObject;
                    if (feature == null) continue;
                    if (string.Equals(feature.name, name, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(feature.GetType().Name, name, StringComparison.OrdinalIgnoreCase))
                        return i;
                }
            }

            return -1;
        }

        private static Dictionary<string, object> GetFeatureProperties(ScriptableObject feature)
        {
            var props = new Dictionary<string, object>();
            using (var so = new SerializedObject(feature))
            {
                var iterator = so.GetIterator();
                if (iterator.NextVisible(true)) // Enter children
                {
                    do
                    {
                        // Skip Unity internal properties
                        if (iterator.name == "m_Script" || iterator.name == "m_ObjectHideFlags" || iterator.name == "m_Name")
                            continue;

                        props[iterator.name] = GraphicsHelpers.ReadSerializedValue(iterator);
                    } while (iterator.NextVisible(false));
                }
            }
            return props;
        }

        private static (List<string> changed, List<string> failed) ApplyFeatureProperties(
            ScriptableObject feature, JObject propertiesToken)
        {
            var changed = new List<string>();
            var failed = new List<string>();

            using (var so = new SerializedObject(feature))
            {
                foreach (var prop in propertiesToken.Properties())
                {
                    var sProp = so.FindProperty(prop.Name);
                    if (sProp != null)
                    {
                        if (GraphicsHelpers.SetSerializedValue(sProp, prop.Value))
                            changed.Add(prop.Name);
                        else
                            failed.Add(prop.Name);
                    }
                    else
                    {
                        // Try nested: "settings.fieldName"
                        string nested = $"settings.{prop.Name}";
                        sProp = so.FindProperty(nested);
                        if (sProp != null && GraphicsHelpers.SetSerializedValue(sProp, prop.Value))
                            changed.Add(prop.Name);
                        else
                            failed.Add(prop.Name);
                    }
                }
                so.ApplyModifiedProperties();
            }

            return (changed, failed);
        }

        private static void TrySetMaterial(ScriptableObject feature, string materialPath)
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (mat == null) return;

            using (var so = new SerializedObject(feature))
            {
                // FullScreenPassRendererFeature uses "m_PassMaterial" or "passMaterial"
                var matProp = so.FindProperty("m_PassMaterial") ?? so.FindProperty("passMaterial");
                if (matProp != null && matProp.propertyType == SerializedPropertyType.ObjectReference)
                {
                    matProp.objectReferenceValue = mat;
                    so.ApplyModifiedProperties();
                }
            }
        }
    }
}
