using System;
using System.Collections.Generic;
using System.Reflection;
using MCPForUnity.Editor.Helpers;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace MCPForUnity.Editor.Tools.Cameras
{
    internal static class CameraCreate
    {
        private static readonly Dictionary<string, (string body, string aim)> Presets = new(StringComparer.OrdinalIgnoreCase)
        {
            ["follow"]        = ("CinemachineFollow",              "CinemachineRotationComposer"),
            ["third_person"]  = ("CinemachineThirdPersonFollow",   "CinemachineRotationComposer"),
            ["freelook"]      = ("CinemachineOrbitalFollow",       "CinemachineRotationComposer"),
            ["dolly"]         = ("CinemachineSplineDolly",         "CinemachineRotationComposer"),
            ["static"]        = (null,                              "CinemachineHardLookAt"),
            ["top_down"]      = ("CinemachineFollow",              null),
            ["side_scroller"] = ("CinemachinePositionComposer",    null),
        };

        internal static object CreateBasicCamera(JObject @params)
        {
            var props = CameraHelpers.ExtractProperties(@params) ?? new JObject();
            string name = ParamCoercion.CoerceString(props["name"], null) ?? "Camera";
            float fov = ParamCoercion.CoerceFloat(props["fieldOfView"], 60f);
            float near = ParamCoercion.CoerceFloat(props["nearClipPlane"], 0.3f);
            float far = ParamCoercion.CoerceFloat(props["farClipPlane"], 1000f);

            var go = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(go, $"Create Camera '{name}'");
            var cam = go.AddComponent<UnityEngine.Camera>();
            cam.fieldOfView = fov;
            cam.nearClipPlane = near;
            cam.farClipPlane = far;

            // Position near follow target if provided
            string follow = ParamCoercion.CoerceString(props["follow"], null);
            if (follow != null)
            {
                var target = CameraHelpers.ResolveGameObjectRef(follow);
                if (target != null)
                    go.transform.position = target.transform.position + new Vector3(0, 5, -10);
            }

            // Look at target if provided
            string lookAt = ParamCoercion.CoerceString(props["lookAt"] ?? props["look_at"], null);
            if (lookAt != null)
            {
                var target = CameraHelpers.ResolveGameObjectRef(lookAt);
                if (target != null)
                    go.transform.LookAt(target.transform);
            }

            CameraHelpers.MarkDirty(go);

            return new
            {
                success = true,
                message = $"Created basic Camera '{name}' (Cinemachine not installed — using Unity Camera).",
                data = new
                {
                    instanceID = go.GetInstanceID(),
                    cinemachine = false,
                    hint = "Install com.unity.cinemachine for presets, blending, and virtual camera features."
                }
            };
        }

        internal static object CreateCinemachineCamera(JObject @params)
        {
            var props = CameraHelpers.ExtractProperties(@params) ?? new JObject();
            string name = ParamCoercion.CoerceString(props["name"], null) ?? "CM Camera";
            string preset = ParamCoercion.CoerceString(props["preset"], null) ?? "follow";
            int priority = ParamCoercion.CoerceInt(props["priority"], 10);

            if (!Presets.TryGetValue(preset, out var presetDef))
            {
                return new ErrorResponse(
                    $"Unknown preset '{preset}'. Valid presets: {string.Join(", ", Presets.Keys)}.");
            }

            var go = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(go, $"Create CinemachineCamera '{name}'");

            // Add CinemachineCamera component
            var cmType = CameraHelpers.CinemachineCameraType;
            var cmCamera = go.AddComponent(cmType);

            // PrioritySettings is a struct with Enabled + m_Value — use SerializedProperty
            using (var so = new SerializedObject(cmCamera))
            {
                var priorityProp = so.FindProperty("Priority");
                if (priorityProp != null)
                {
                    var enabledProp = priorityProp.FindPropertyRelative("Enabled");
                    var valueProp = priorityProp.FindPropertyRelative("m_Value");
                    if (enabledProp != null) enabledProp.boolValue = true;
                    if (valueProp != null) valueProp.intValue = priority;
                    so.ApplyModifiedProperties();
                }
                else
                {
                    CameraHelpers.SetReflectionProperty(cmCamera, "Priority", priority);
                }
            }

            // Add Body component
            string bodyName = null;
            if (presetDef.body != null)
            {
                var bodyType = CameraHelpers.ResolveComponentType(presetDef.body);
                if (bodyType != null)
                {
                    go.AddComponent(bodyType);
                    bodyName = presetDef.body;
                }
            }

            // Add Aim component
            string aimName = null;
            if (presetDef.aim != null)
            {
                var aimType = CameraHelpers.ResolveComponentType(presetDef.aim);
                if (aimType != null)
                {
                    go.AddComponent(aimType);
                    aimName = presetDef.aim;
                }
            }

            // Set Follow target
            var followToken = props["follow"];
            if (followToken != null && followToken.Type != JTokenType.Null)
                CameraHelpers.SetTransformTarget(cmCamera, "Follow", followToken);

            // Set LookAt target
            var lookAtToken = props["lookAt"] ?? props["look_at"];
            if (lookAtToken != null && lookAtToken.Type != JTokenType.Null)
                CameraHelpers.SetTransformTarget(cmCamera, "LookAt", lookAtToken);

            CameraHelpers.MarkDirty(go);

            return new
            {
                success = true,
                message = $"Created CinemachineCamera '{name}' with preset '{preset}'.",
                data = new
                {
                    instanceID = go.GetInstanceID(),
                    cinemachine = true,
                    preset,
                    priority,
                    body = bodyName,
                    aim = aimName
                }
            };
        }

        internal static object EnsureBrain(JObject @params)
        {
            var props = CameraHelpers.ExtractProperties(@params) ?? new JObject();

            // Check if Brain already exists
            var existingBrain = CameraHelpers.FindBrain();
            if (existingBrain != null)
            {
                return new
                {
                    success = true,
                    message = $"CinemachineBrain already exists on '{existingBrain.gameObject.name}'.",
                    data = new
                    {
                        instanceID = existingBrain.gameObject.GetInstanceID(),
                        alreadyExisted = true
                    }
                };
            }

            // Find target camera
            string cameraRef = ParamCoercion.CoerceString(props["camera"], null);
            UnityEngine.Camera cam;
            if (cameraRef != null)
            {
                var camGo = CameraHelpers.ResolveGameObjectRef(cameraRef);
                cam = camGo != null ? camGo.GetComponent<UnityEngine.Camera>() : null;
            }
            else
            {
                cam = CameraHelpers.FindMainCamera();
            }

            if (cam == null)
                return new ErrorResponse("No Camera found to add CinemachineBrain to.");

            var brainType = CameraHelpers.CinemachineBrainType;
            Undo.RecordObject(cam.gameObject, "Add CinemachineBrain");
            var brain = cam.gameObject.AddComponent(brainType);

            // Configure default blend if provided
            string blendStyle = ParamCoercion.CoerceString(props["defaultBlendStyle"] ?? props["default_blend_style"], null);
            float blendDuration = ParamCoercion.CoerceFloat(props["defaultBlendDuration"] ?? props["default_blend_duration"], -1f);

            if (blendStyle != null || blendDuration >= 0)
            {
                // Set via SerializedProperty for the DefaultBlend struct
                using var so = new SerializedObject(brain);
                var defaultBlendProp = so.FindProperty("DefaultBlend") ?? so.FindProperty("m_DefaultBlend");
                if (defaultBlendProp != null)
                {
                    if (blendStyle != null)
                    {
                        var styleProp = defaultBlendProp.FindPropertyRelative("Style")
                                     ?? defaultBlendProp.FindPropertyRelative("m_Style");
                        if (styleProp != null)
                            ComponentOps.SetProperty(brain, "DefaultBlend.Style", new JValue(blendStyle), out _);
                    }
                    if (blendDuration >= 0)
                    {
                        var timeProp = defaultBlendProp.FindPropertyRelative("Time")
                                    ?? defaultBlendProp.FindPropertyRelative("m_Time");
                        if (timeProp != null)
                            timeProp.floatValue = blendDuration;
                    }
                    so.ApplyModifiedProperties();
                }
            }

            CameraHelpers.MarkDirty(cam.gameObject);

            return new
            {
                success = true,
                message = $"CinemachineBrain added to '{cam.gameObject.name}'.",
                data = new
                {
                    instanceID = cam.gameObject.GetInstanceID(),
                    alreadyExisted = false
                }
            };
        }
    }
}
