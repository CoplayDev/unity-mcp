using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MCPForUnity.Editor.Helpers;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace MCPForUnity.Editor.Tools.Graphics
{
    internal static class GraphicsHelpers
    {
        private static bool? _hasVolumeSystem;
        private static bool? _hasURP;
        private static bool? _hasHDRP;
        private static Type _volumeType;
        private static Type _volumeProfileType;
        private static Type _volumeComponentType;
        private static Type _volumeParameterType;

        internal static bool HasVolumeSystem
        {
            get
            {
                if (_hasVolumeSystem == null) DetectPackages();
                return _hasVolumeSystem.Value;
            }
        }

        internal static bool HasURP
        {
            get
            {
                if (_hasURP == null) DetectPackages();
                return _hasURP.Value;
            }
        }

        internal static bool HasHDRP
        {
            get
            {
                if (_hasHDRP == null) DetectPackages();
                return _hasHDRP.Value;
            }
        }

        internal static Type VolumeType
        {
            get
            {
                if (_hasVolumeSystem == null) DetectPackages();
                return _volumeType;
            }
        }

        internal static Type VolumeProfileType
        {
            get
            {
                if (_hasVolumeSystem == null) DetectPackages();
                return _volumeProfileType;
            }
        }

        internal static Type VolumeComponentType
        {
            get
            {
                if (_hasVolumeSystem == null) DetectPackages();
                return _volumeComponentType;
            }
        }

        internal static Type VolumeParameterType
        {
            get
            {
                if (_hasVolumeSystem == null) DetectPackages();
                return _volumeParameterType;
            }
        }

        private static void DetectPackages()
        {
            _volumeType = Type.GetType("UnityEngine.Rendering.Volume, Unity.RenderPipelines.Core.Runtime");
            _volumeProfileType = Type.GetType("UnityEngine.Rendering.VolumeProfile, Unity.RenderPipelines.Core.Runtime");
            _volumeComponentType = Type.GetType("UnityEngine.Rendering.VolumeComponent, Unity.RenderPipelines.Core.Runtime");
            _volumeParameterType = Type.GetType("UnityEngine.Rendering.VolumeParameter, Unity.RenderPipelines.Core.Runtime");
            _hasVolumeSystem = _volumeType != null && _volumeProfileType != null;

            var pipeline = RenderPipelineUtility.GetActivePipeline();
            _hasURP = pipeline == RenderPipelineUtility.PipelineKind.Universal;
            _hasHDRP = pipeline == RenderPipelineUtility.PipelineKind.HighDefinition;
        }

        internal static Type ResolveVolumeComponentType(string effectName)
        {
            if (string.IsNullOrEmpty(effectName) || VolumeComponentType == null)
                return null;

            var derivedTypes = TypeCache.GetTypesDerivedFrom(VolumeComponentType);
            foreach (var t in derivedTypes)
            {
                if (t.IsAbstract) continue;
                if (string.Equals(t.Name, effectName, StringComparison.OrdinalIgnoreCase))
                    return t;
            }
            return null;
        }

        internal static List<Type> GetAvailableEffectTypes()
        {
            if (VolumeComponentType == null)
                return new List<Type>();
            var derivedTypes = TypeCache.GetTypesDerivedFrom(VolumeComponentType);
            return derivedTypes
                .Where(t => !t.IsAbstract && !t.IsGenericType)
                .OrderBy(t => t.Name)
                .ToList();
        }

        internal static Component FindVolume(JObject @params)
        {
            var p = new ToolParams(@params);
            string target = p.Get("target");
            if (string.IsNullOrEmpty(target))
            {
#if UNITY_2022_2_OR_NEWER
                var allVolumes = UnityEngine.Object.FindObjectsByType(VolumeType, FindObjectsSortMode.None);
#else
                var allVolumes = UnityEngine.Object.FindObjectsOfType(VolumeType);
#endif
                return allVolumes.Length > 0 ? allVolumes[0] as Component : null;
            }

            if (int.TryParse(target, out int instanceId))
            {
                var byId = GameObjectLookup.ResolveInstanceID(instanceId) as GameObject;
                if (byId != null) return byId.GetComponent(VolumeType);
            }

            var go = GameObject.Find(target);
            if (go != null) return go.GetComponent(VolumeType);

            return null;
        }

        internal static string GetPipelineName()
        {
            return RenderPipelineUtility.GetActivePipeline() switch
            {
                RenderPipelineUtility.PipelineKind.Universal => "Universal (URP)",
                RenderPipelineUtility.PipelineKind.HighDefinition => "High Definition (HDRP)",
                RenderPipelineUtility.PipelineKind.BuiltIn => "Built-in",
                RenderPipelineUtility.PipelineKind.Custom => "Custom",
                _ => "Unknown"
            };
        }

        internal static object ReadSerializedValue(SerializedProperty prop)
        {
            return prop.propertyType switch
            {
                SerializedPropertyType.Boolean => prop.boolValue,
                SerializedPropertyType.Integer => prop.intValue,
                SerializedPropertyType.Float => prop.floatValue,
                SerializedPropertyType.String => prop.stringValue,
                SerializedPropertyType.Enum => prop.enumValueIndex < prop.enumNames.Length
                    ? prop.enumNames[prop.enumValueIndex]
                    : (object)prop.enumValueIndex,
                SerializedPropertyType.ObjectReference => prop.objectReferenceValue != null
                    ? prop.objectReferenceValue.name
                    : null,
                SerializedPropertyType.Color => $"({prop.colorValue.r:F2}, {prop.colorValue.g:F2}, {prop.colorValue.b:F2}, {prop.colorValue.a:F2})",
                SerializedPropertyType.Vector2 => new { x = prop.vector2Value.x, y = prop.vector2Value.y },
                SerializedPropertyType.Vector3 => new { x = prop.vector3Value.x, y = prop.vector3Value.y, z = prop.vector3Value.z },
                SerializedPropertyType.LayerMask => prop.intValue,
                _ => prop.propertyType.ToString()
            };
        }

        internal static bool SetSerializedValue(SerializedProperty prop, JToken value)
        {
            try
            {
                switch (prop.propertyType)
                {
                    case SerializedPropertyType.Boolean:
                        prop.boolValue = ParamCoercion.CoerceBool(value, false);
                        return true;
                    case SerializedPropertyType.Integer:
                        prop.intValue = ParamCoercion.CoerceInt(value, 0);
                        return true;
                    case SerializedPropertyType.Float:
                        prop.floatValue = ParamCoercion.CoerceFloat(value, 0f);
                        return true;
                    case SerializedPropertyType.String:
                        prop.stringValue = value.ToString();
                        return true;
                    case SerializedPropertyType.Enum:
                        if (value.Type == JTokenType.String)
                        {
                            for (int i = 0; i < prop.enumNames.Length; i++)
                            {
                                if (string.Equals(prop.enumNames[i], value.ToString(), StringComparison.OrdinalIgnoreCase))
                                { prop.enumValueIndex = i; return true; }
                            }
                        }
                        prop.enumValueIndex = ParamCoercion.CoerceInt(value, 0);
                        return true;
                    case SerializedPropertyType.ObjectReference:
                        if (value.Type == JTokenType.String)
                        {
                            string path = value.ToString();
                            var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path);
                            if (asset != null) { prop.objectReferenceValue = asset; return true; }
                        }
                        return false;
                    case SerializedPropertyType.Color:
                        if (value is JArray colorArr && colorArr.Count >= 3)
                        {
                            prop.colorValue = new Color(
                                (float)colorArr[0], (float)colorArr[1], (float)colorArr[2],
                                colorArr.Count >= 4 ? (float)colorArr[3] : 1f);
                            return true;
                        }
                        return false;
                    case SerializedPropertyType.LayerMask:
                        prop.intValue = ParamCoercion.CoerceInt(value, 0);
                        return true;
                    default:
                        return false;
                }
            }
            catch { return false; }
        }

        internal static void MarkDirty(UnityEngine.Object obj)
        {
            if (obj == null) return;
            EditorUtility.SetDirty(obj);
            if (obj is Component comp)
            {
                var prefabStage = UnityEditor.SceneManagement.PrefabStageUtility.GetCurrentPrefabStage();
                if (prefabStage != null)
                    UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(prefabStage.scene);
                else
                    UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(comp.gameObject.scene);
            }
        }
    }
}
