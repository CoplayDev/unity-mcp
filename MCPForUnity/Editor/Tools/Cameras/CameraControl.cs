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
    internal static class CameraControl
    {
        internal static object ListCameras(JObject @params)
        {
            var unityCameras = UnityEngine.Object.FindObjectsOfType<UnityEngine.Camera>();
            var cameraList = new List<object>();
            var unityCamList = new List<object>();

            // Cinemachine cameras
            if (CameraHelpers.HasCinemachine)
            {
                var cmType = CameraHelpers.CinemachineCameraType;
                var allCm = UnityEngine.Object.FindObjectsOfType(cmType);
                foreach (Component cm in allCm)
                {
                    var follow = CameraHelpers.GetReflectionProperty(cm, "Follow") as Transform;
                    var lookAt = CameraHelpers.GetReflectionProperty(cm, "LookAt") as Transform;
                    var isLive = CameraHelpers.GetReflectionProperty(cm, "IsLive");
                    var priority = CameraHelpers.ReadCinemachinePriority(cm);

                    var body = CameraHelpers.GetPipelineComponent(cm, "Body");
                    var aim = CameraHelpers.GetPipelineComponent(cm, "Aim");
                    var noise = CameraHelpers.GetPipelineComponent(cm, "Noise");

                    // Collect extensions
                    var extensions = new List<string>();
                    var cmExtBaseType = cm.GetType().Assembly.GetType("Unity.Cinemachine.CinemachineExtension");
                    if (cmExtBaseType != null)
                    {
                        foreach (var comp in cm.gameObject.GetComponents(cmExtBaseType))
                        {
                            if (comp != null)
                                extensions.Add(comp.GetType().Name);
                        }
                    }

                    cameraList.Add(new
                    {
                        instanceID = cm.gameObject.GetInstanceID(),
                        name = cm.gameObject.name,
                        isLive = isLive is bool b && b,
                        priority,
                        follow = follow != null ? new { name = follow.gameObject.name, instanceID = follow.gameObject.GetInstanceID() } : null,
                        lookAt = lookAt != null ? new { name = lookAt.gameObject.name, instanceID = lookAt.gameObject.GetInstanceID() } : null,
                        body = body?.GetType().Name,
                        aim = aim?.GetType().Name,
                        noise = noise?.GetType().Name,
                        extensions
                    });
                }
            }

            // Unity cameras
            foreach (var cam in unityCameras)
            {
                bool hasBrain = CameraHelpers.HasCinemachine &&
                    cam.gameObject.GetComponent(CameraHelpers.CinemachineBrainType) != null;
                unityCamList.Add(new
                {
                    instanceID = cam.gameObject.GetInstanceID(),
                    name = cam.gameObject.name,
                    depth = cam.depth,
                    fieldOfView = cam.fieldOfView,
                    hasBrain
                });
            }

            // Brain info
            object brainInfo = null;
            if (CameraHelpers.HasCinemachine)
            {
                var brain = CameraHelpers.FindBrain();
                if (brain != null)
                {
                    var activeCam = CameraHelpers.GetReflectionProperty(brain, "ActiveVirtualCamera");
                    var isBlending = CameraHelpers.GetReflectionProperty(brain, "IsBlending");

                    string activeName = null;
                    int? activeID = null;
                    if (activeCam != null)
                    {
                        var nameProp = activeCam.GetType().GetProperty("Name");
                        activeName = nameProp?.GetValue(activeCam) as string;

                        if (activeCam is Component activeComp)
                            activeID = activeComp.gameObject.GetInstanceID();
                    }

                    brainInfo = new
                    {
                        exists = true,
                        gameObject = brain.gameObject.name,
                        instanceID = brain.gameObject.GetInstanceID(),
                        activeCameraName = activeName,
                        activeCameraID = activeID,
                        isBlending = isBlending is bool bl && bl
                    };
                }
            }

            return new
            {
                success = true,
                data = new
                {
                    brain = brainInfo,
                    cinemachineCameras = cameraList,
                    unityCameras = unityCamList,
                    cinemachineInstalled = CameraHelpers.HasCinemachine
                }
            };
        }

        internal static object GetBrainStatus(JObject @params)
        {
            var brain = CameraHelpers.FindBrain();
            if (brain == null)
                return new ErrorResponse("No CinemachineBrain found in the scene.");

            var activeCam = CameraHelpers.GetReflectionProperty(brain, "ActiveVirtualCamera");
            var isBlending = CameraHelpers.GetReflectionProperty(brain, "IsBlending");
            var activeBlend = CameraHelpers.GetReflectionProperty(brain, "ActiveBlend");

            string activeName = null;
            int? activeID = null;
            if (activeCam != null)
            {
                var nameProp = activeCam.GetType().GetProperty("Name");
                activeName = nameProp?.GetValue(activeCam) as string;
                if (activeCam is Component comp)
                    activeID = comp.gameObject.GetInstanceID();
            }

            string blendDesc = null;
            if (activeBlend != null)
            {
                var descProp = activeBlend.GetType().GetProperty("Description");
                blendDesc = descProp?.GetValue(activeBlend) as string;
            }

            return new
            {
                success = true,
                data = new
                {
                    gameObject = brain.gameObject.name,
                    instanceID = brain.gameObject.GetInstanceID(),
                    activeCameraName = activeName,
                    activeCameraID = activeID,
                    isBlending = isBlending is bool b && b,
                    blendDescription = blendDesc
                }
            };
        }

        internal static object SetBlend(JObject @params)
        {
            var brain = CameraHelpers.FindBrain();
            if (brain == null)
                return new ErrorResponse("No CinemachineBrain found. Use 'ensure_brain' first.");

            var props = CameraHelpers.ExtractProperties(@params) ?? new JObject();
            Undo.RecordObject(brain, "Set Camera Blend");

            using var so = new SerializedObject(brain);
            var defaultBlendProp = so.FindProperty("DefaultBlend") ?? so.FindProperty("m_DefaultBlend");
            if (defaultBlendProp == null)
                return new ErrorResponse("Could not find DefaultBlend property on CinemachineBrain.");

            string style = ParamCoercion.CoerceString(props["style"], null);
            if (style != null)
            {
                var styleProp = defaultBlendProp.FindPropertyRelative("Style")
                             ?? defaultBlendProp.FindPropertyRelative("m_Style");
                if (styleProp != null && styleProp.propertyType == SerializedPropertyType.Enum)
                {
                    // Try to parse the style enum
                    var enumNames = styleProp.enumNames;
                    int idx = Array.FindIndex(enumNames, n => n.Equals(style, StringComparison.OrdinalIgnoreCase));
                    if (idx >= 0)
                        styleProp.enumValueIndex = idx;
                }
            }

            float duration = ParamCoercion.CoerceFloat(props["duration"], -1f);
            if (duration >= 0)
            {
                var timeProp = defaultBlendProp.FindPropertyRelative("Time")
                            ?? defaultBlendProp.FindPropertyRelative("m_Time");
                if (timeProp != null)
                    timeProp.floatValue = duration;
            }

            so.ApplyModifiedProperties();
            CameraHelpers.MarkDirty(brain.gameObject);

            return new
            {
                success = true,
                message = "Default blend configured on CinemachineBrain.",
                data = new { instanceID = brain.gameObject.GetInstanceID() }
            };
        }

        private static int _overrideId = -1;

        internal static object ForceCamera(JObject @params)
        {
            var brain = CameraHelpers.FindBrain();
            if (brain == null)
                return new ErrorResponse("No CinemachineBrain found. Use 'ensure_brain' first.");

            var cmCamera = CameraHelpers.FindCinemachineCamera(@params);
            if (cmCamera == null)
                return new ErrorResponse("Target CinemachineCamera not found.");

            // Use SetCameraOverride via reflection
            var brainType = brain.GetType();
            var method = brainType.GetMethod("SetCameraOverride",
                BindingFlags.Public | BindingFlags.Instance);

            if (method == null)
            {
                // Fallback: just set high priority
                CameraHelpers.SetReflectionProperty(cmCamera, "Priority", 999);
                return new
                {
                    success = true,
                    message = $"Set high priority on '{cmCamera.gameObject.name}' (SetCameraOverride not available).",
                    data = new { instanceID = cmCamera.gameObject.GetInstanceID(), method = "priority" }
                };
            }

            try
            {
                // CM3 signature: SetCameraOverride(int overrideId, int priority,
                //   ICinemachineCamera camA, ICinemachineCamera camB, float weightB, float deltaTime)
                // -1 for overrideId creates a new override; same cam for A+B with weight=1 = instant switch
                _overrideId = (int)method.Invoke(brain, new object[]
                {
                    _overrideId >= 0 ? _overrideId : -1,
                    999,      // high priority to win over all others
                    cmCamera, // camA (at weight=0)
                    cmCamera, // camB (at weight=1) — same camera = no blend
                    1f,       // weightB = fully on camB
                    -1f       // deltaTime = use default
                });
            }
            catch (Exception ex)
            {
                // Fallback
                CameraHelpers.SetReflectionProperty(cmCamera, "Priority", 999);
                return new
                {
                    success = true,
                    message = $"Forced via priority (override failed: {ex.Message}).",
                    data = new { instanceID = cmCamera.gameObject.GetInstanceID(), method = "priority" }
                };
            }

            return new
            {
                success = true,
                message = $"Camera overridden to '{cmCamera.gameObject.name}'.",
                data = new
                {
                    instanceID = cmCamera.gameObject.GetInstanceID(),
                    overrideId = _overrideId,
                    method = "override"
                }
            };
        }

        internal static object ReleaseOverride(JObject @params)
        {
            var brain = CameraHelpers.FindBrain();
            if (brain == null)
                return new ErrorResponse("No CinemachineBrain found.");

            if (_overrideId < 0)
                return new { success = true, message = "No active camera override to release." };

            var method = brain.GetType().GetMethod("ReleaseCameraOverride",
                BindingFlags.Public | BindingFlags.Instance);

            if (method != null)
            {
                method.Invoke(brain, new object[] { _overrideId });
                int releasedId = _overrideId;
                _overrideId = -1;
                return new
                {
                    success = true,
                    message = "Camera override released.",
                    data = new { releasedOverrideId = releasedId }
                };
            }

            _overrideId = -1;
            return new { success = true, message = "Override state cleared." };
        }
    }
}
