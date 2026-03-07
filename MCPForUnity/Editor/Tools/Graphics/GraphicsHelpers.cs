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
                var allVolumes = UnityEngine.Object.FindObjectsOfType(VolumeType);
                return allVolumes.Length > 0 ? allVolumes[0] as Component : null;
            }

            if (int.TryParse(target, out int instanceId))
            {
                var byId = EditorUtility.InstanceIDToObject(instanceId) as GameObject;
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
