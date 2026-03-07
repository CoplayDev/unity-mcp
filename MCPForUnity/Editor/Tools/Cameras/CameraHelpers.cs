using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MCPForUnity.Editor.Helpers;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace MCPForUnity.Editor.Tools.Cameras
{
    internal static class CameraHelpers
    {
        private static bool? _hasCinemachine;
        private static Type _cmCameraType;
        private static Type _cmBrainType;

        internal static bool HasCinemachine
        {
            get
            {
                if (_hasCinemachine == null)
                    DetectCinemachine();
                return _hasCinemachine.Value;
            }
        }

        internal static Type CinemachineCameraType
        {
            get
            {
                if (_hasCinemachine == null)
                    DetectCinemachine();
                return _cmCameraType;
            }
        }

        internal static Type CinemachineBrainType
        {
            get
            {
                if (_hasCinemachine == null)
                    DetectCinemachine();
                return _cmBrainType;
            }
        }

        private static void DetectCinemachine()
        {
            _cmCameraType = UnityTypeResolver.ResolveComponent("CinemachineCamera");
            _cmBrainType = UnityTypeResolver.ResolveComponent("CinemachineBrain");
            _hasCinemachine = _cmCameraType != null && _cmBrainType != null;
        }

        internal static string GetCinemachineVersion()
        {
            if (!HasCinemachine || _cmCameraType == null)
                return null;

            try
            {
                var assembly = _cmCameraType.Assembly;
                var version = assembly.GetName().Version;
                return version?.ToString();
            }
            catch
            {
                return "unknown";
            }
        }

        internal static GameObject FindTargetGameObject(JObject @params)
        {
            var targetToken = @params["target"];
            if (targetToken == null)
                return null;

            string searchMethod = ParamCoercion.CoerceString(
                @params["searchMethod"] ?? @params["search_method"], "by_name");

            if (targetToken.Type == JTokenType.Integer)
            {
                int instanceId = targetToken.Value<int>();
                return GameObjectLookup.FindById(instanceId);
            }

            string targetStr = targetToken.ToString();
            if (int.TryParse(targetStr, out int parsedId))
            {
                var byId = GameObjectLookup.FindById(parsedId);
                if (byId != null) return byId;
            }

            return GameObjectLookup.FindByTarget(targetToken, searchMethod, true);
        }

        internal static GameObject ResolveGameObjectRef(object reference)
        {
            if (reference == null) return null;

            if (reference is JToken jt)
            {
                if (jt.Type == JTokenType.Integer)
                    return GameObjectLookup.FindById(jt.Value<int>());
                if (jt.Type == JTokenType.String)
                {
                    string str = jt.ToString();
                    if (int.TryParse(str, out int id))
                    {
                        var byId = GameObjectLookup.FindById(id);
                        if (byId != null) return byId;
                    }
                    return GameObjectLookup.FindByTarget(jt, "by_name", true);
                }
            }

            if (reference is string s)
            {
                if (int.TryParse(s, out int id))
                {
                    var byId = GameObjectLookup.FindById(id);
                    if (byId != null) return byId;
                }
                var ids = GameObjectLookup.SearchGameObjects(
                    GameObjectLookup.SearchMethod.ByName, s, includeInactive: true, maxResults: 1);
                return ids.Count > 0 ? GameObjectLookup.FindById(ids[0]) : null;
            }

            return null;
        }

        internal static Component FindCinemachineCamera(JObject @params)
        {
            if (!HasCinemachine) return null;
            var go = FindTargetGameObject(@params);
            return go != null ? go.GetComponent(CinemachineCameraType) : null;
        }

        internal static Component FindBrain()
        {
            if (!HasCinemachine || _cmBrainType == null)
                return null;

            return UnityEngine.Object.FindFirstObjectByType(_cmBrainType) as Component;
        }

        internal static UnityEngine.Camera FindMainCamera()
        {
            var main = UnityEngine.Camera.main;
            if (main != null) return main;

            var allCams = UnityEngine.Object.FindObjectsByType<UnityEngine.Camera>(FindObjectsSortMode.None);
            return allCams.Length > 0 ? allCams[0] : null;
        }

        internal static JObject ExtractProperties(JObject @params)
        {
            var props = @params["properties"] as JObject;
            if (props != null) return props;

            var propsStr = ParamCoercion.CoerceString(@params["properties"], null);
            if (propsStr != null)
            {
                try { return JObject.Parse(propsStr); }
                catch { return null; }
            }

            return null;
        }

        internal static object GetReflectionProperty(Component component, string propertyName)
        {
            if (component == null) return null;
            var type = component.GetType();
            var prop = type.GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
            return prop?.GetValue(component);
        }

        /// <summary>Read priority int from a CinemachineCamera component via SerializedObject.</summary>
        internal static int ReadCinemachinePriority(Component cmCamera)
        {
            if (cmCamera == null) return 0;
            using var so = new SerializedObject(cmCamera);
            var priorityProp = so.FindProperty("Priority");
            if (priorityProp == null) return 0;
            var enabledProp = priorityProp.FindPropertyRelative("Enabled");
            var valueProp = priorityProp.FindPropertyRelative("m_Value");
            if (enabledProp != null && !enabledProp.boolValue) return 0;
            return valueProp?.intValue ?? 0;
        }

        internal static bool SetReflectionProperty(Component component, string propertyName, object value)
        {
            if (component == null) return false;
            var type = component.GetType();
            var prop = type.GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
            if (prop == null || !prop.CanWrite) return false;
            prop.SetValue(component, value);
            return true;
        }

        internal static void SetTransformTarget(Component cmCamera, string propertyName, JToken targetRef)
        {
            if (cmCamera == null) return;

            if (targetRef == null || targetRef.Type == JTokenType.Null)
            {
                SetReflectionProperty(cmCamera, propertyName, null);
                return;
            }

            var go = ResolveGameObjectRef(targetRef);
            if (go != null)
                SetReflectionProperty(cmCamera, propertyName, go.transform);
        }

        internal static Type ResolveComponentType(string typeName)
        {
            return UnityTypeResolver.ResolveComponent(typeName);
        }

        internal static Component GetPipelineComponent(Component cmCamera, string stageName)
        {
            if (cmCamera == null) return null;
            var type = cmCamera.GetType();

            // CinemachineCamera.GetCinemachineComponent(CinemachineCore.Stage stage)
            var stageEnumType = type.Assembly.GetType("Unity.Cinemachine.CinemachineCore+Stage")
                             ?? type.Assembly.GetType("Unity.Cinemachine.CinemachineCore")?.GetNestedType("Stage");

            if (stageEnumType == null) return null;

            object stageEnum;
            try { stageEnum = Enum.Parse(stageEnumType, stageName, true); }
            catch { return null; }

            var method = type.GetMethod("GetCinemachineComponent",
                BindingFlags.Public | BindingFlags.Instance,
                null, new[] { stageEnumType }, null);

            if (method == null) return null;
            return method.Invoke(cmCamera, new[] { stageEnum }) as Component;
        }

        internal static string GetFallbackSuggestion(string action)
        {
            return action switch
            {
                "set_body" or "set_aim" => "Use 'set_lens' and 'set_target' for basic camera configuration.",
                "set_blend" => "Without Cinemachine, switch cameras by enabling/disabling Camera components.",
                "set_noise" => "Camera shake without Cinemachine requires a custom script.",
                "ensure_brain" => "CinemachineBrain requires the Cinemachine package. Basic Camera does not need a Brain.",
                "get_brain_status" => "No CinemachineBrain available. Cinemachine package not installed.",
                _ => "Install Cinemachine via Window > Package Manager."
            };
        }

        internal static void MarkDirty(GameObject go)
        {
            if (go == null) return;
            EditorUtility.SetDirty(go);
            var prefabStage = UnityEditor.SceneManagement.PrefabStageUtility.GetCurrentPrefabStage();
            if (prefabStage != null)
                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(prefabStage.scene);
            else
                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(go.scene);
        }
    }
}
