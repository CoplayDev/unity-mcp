using System;
using System.Reflection;
using MCPForUnity.Editor.Helpers;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace MCPForUnity.Editor.Tools.Cameras
{
    internal static class CameraConfigure
    {
        #region Tier 1 — Basic Camera

        internal static object SetBasicCameraTarget(JObject @params)
        {
            var go = CameraHelpers.FindTargetGameObject(@params);
            if (go == null) return new ErrorResponse("Target Camera not found.");

            var cam = go.GetComponent<UnityEngine.Camera>();
            if (cam == null) return new ErrorResponse($"No Camera component on '{go.name}'.");

            var props = CameraHelpers.ExtractProperties(@params) ?? new JObject();
            var lookAtToken = props["lookAt"] ?? props["look_at"] ?? props["follow"];
            if (lookAtToken == null)
                return new ErrorResponse("'follow' or 'lookAt' property is required.");

            var target = CameraHelpers.ResolveGameObjectRef(lookAtToken);
            if (target == null)
                return new ErrorResponse($"Target '{lookAtToken}' not found.");

            Undo.RecordObject(go.transform, "Set Camera Target");
            go.transform.LookAt(target.transform);
            CameraHelpers.MarkDirty(go);

            return new
            {
                success = true,
                message = $"Camera '{go.name}' now looking at '{target.name}'.",
                data = new { instanceID = go.GetInstanceID() }
            };
        }

        internal static object SetBasicCameraLens(JObject @params)
        {
            var go = CameraHelpers.FindTargetGameObject(@params);
            if (go == null) return new ErrorResponse("Target Camera not found.");

            var cam = go.GetComponent<UnityEngine.Camera>();
            if (cam == null) return new ErrorResponse($"No Camera component on '{go.name}'.");

            var props = CameraHelpers.ExtractProperties(@params) ?? new JObject();
            Undo.RecordObject(cam, "Set Camera Lens");

            if (props["fieldOfView"] != null)
                cam.fieldOfView = ParamCoercion.CoerceFloat(props["fieldOfView"], cam.fieldOfView);
            if (props["nearClipPlane"] != null)
                cam.nearClipPlane = ParamCoercion.CoerceFloat(props["nearClipPlane"], cam.nearClipPlane);
            if (props["farClipPlane"] != null)
                cam.farClipPlane = ParamCoercion.CoerceFloat(props["farClipPlane"], cam.farClipPlane);
            if (props["orthographicSize"] != null)
                cam.orthographicSize = ParamCoercion.CoerceFloat(props["orthographicSize"], cam.orthographicSize);

            CameraHelpers.MarkDirty(go);
            return new
            {
                success = true,
                message = $"Lens properties set on Camera '{go.name}'.",
                data = new { instanceID = go.GetInstanceID() }
            };
        }

        internal static object SetBasicCameraPriority(JObject @params)
        {
            var go = CameraHelpers.FindTargetGameObject(@params);
            if (go == null) return new ErrorResponse("Target Camera not found.");

            var cam = go.GetComponent<UnityEngine.Camera>();
            if (cam == null) return new ErrorResponse($"No Camera component on '{go.name}'.");

            var props = CameraHelpers.ExtractProperties(@params) ?? new JObject();
            float depth = ParamCoercion.CoerceFloat(props["priority"], cam.depth);

            Undo.RecordObject(cam, "Set Camera Depth");
            cam.depth = depth;
            CameraHelpers.MarkDirty(go);

            return new
            {
                success = true,
                message = $"Camera '{go.name}' depth set to {depth}.",
                data = new { instanceID = go.GetInstanceID(), depth }
            };
        }

        #endregion

        #region Tier 2 — Cinemachine

        internal static object SetCinemachineTarget(JObject @params)
        {
            var cmCamera = CameraHelpers.FindCinemachineCamera(@params);
            if (cmCamera == null) return new ErrorResponse("Target CinemachineCamera not found.");

            var props = CameraHelpers.ExtractProperties(@params) ?? new JObject();

            Undo.RecordObject(cmCamera, "Set Cinemachine Target");

            if (props.ContainsKey("follow"))
                CameraHelpers.SetTransformTarget(cmCamera, "Follow", props["follow"]);
            if (props.ContainsKey("lookAt") || props.ContainsKey("look_at"))
                CameraHelpers.SetTransformTarget(cmCamera, "LookAt", props["lookAt"] ?? props["look_at"]);

            CameraHelpers.MarkDirty(cmCamera.gameObject);

            return new
            {
                success = true,
                message = $"Targets set on CinemachineCamera '{cmCamera.gameObject.name}'.",
                data = new { instanceID = cmCamera.gameObject.GetInstanceID() }
            };
        }

        internal static object SetCinemachineLens(JObject @params)
        {
            var cmCamera = CameraHelpers.FindCinemachineCamera(@params);
            if (cmCamera == null) return new ErrorResponse("Target CinemachineCamera not found.");

            var props = CameraHelpers.ExtractProperties(@params) ?? new JObject();
            Undo.RecordObject(cmCamera, "Set Cinemachine Lens");

            // Lens is a struct field — use SerializedProperty for reliable setting
            using var so = new SerializedObject(cmCamera);
            var lensProp = so.FindProperty("Lens") ?? so.FindProperty("m_Lens");
            if (lensProp == null)
                return new ErrorResponse("Could not find Lens property on CinemachineCamera.");

            SetFloatSubProp(lensProp, "FieldOfView", props["fieldOfView"]);
            SetFloatSubProp(lensProp, "NearClipPlane", props["nearClipPlane"]);
            SetFloatSubProp(lensProp, "FarClipPlane", props["farClipPlane"]);
            SetFloatSubProp(lensProp, "OrthographicSize", props["orthographicSize"]);
            SetFloatSubProp(lensProp, "Dutch", props["dutch"]);

            so.ApplyModifiedProperties();
            CameraHelpers.MarkDirty(cmCamera.gameObject);

            return new
            {
                success = true,
                message = $"Lens properties set on CinemachineCamera '{cmCamera.gameObject.name}'.",
                data = new { instanceID = cmCamera.gameObject.GetInstanceID() }
            };
        }

        internal static object SetCinemachinePriority(JObject @params)
        {
            var cmCamera = CameraHelpers.FindCinemachineCamera(@params);
            if (cmCamera == null) return new ErrorResponse("Target CinemachineCamera not found.");

            var props = CameraHelpers.ExtractProperties(@params) ?? new JObject();
            int priority = ParamCoercion.CoerceInt(props["priority"], 10);

            Undo.RecordObject(cmCamera, "Set Cinemachine Priority");
            CameraHelpers.SetReflectionProperty(cmCamera, "Priority", priority);
            CameraHelpers.MarkDirty(cmCamera.gameObject);

            return new
            {
                success = true,
                message = $"Priority set to {priority} on CinemachineCamera '{cmCamera.gameObject.name}'.",
                data = new { instanceID = cmCamera.gameObject.GetInstanceID(), priority }
            };
        }

        internal static object SetBody(JObject @params)
        {
            var cmCamera = CameraHelpers.FindCinemachineCamera(@params);
            if (cmCamera == null) return new ErrorResponse("Target CinemachineCamera not found.");

            var props = CameraHelpers.ExtractProperties(@params) ?? new JObject();
            var go = cmCamera.gameObject;

            // Optionally swap body component
            string bodyTypeName = ParamCoercion.CoerceString(props["bodyType"] ?? props["body_type"], null);
            Component bodyComponent;

            if (bodyTypeName != null)
            {
                bodyComponent = SwapPipelineComponent(go, "Body", bodyTypeName);
                if (bodyComponent == null)
                    return new ErrorResponse($"Could not resolve body component type '{bodyTypeName}'.");
            }
            else
            {
                bodyComponent = CameraHelpers.GetPipelineComponent(cmCamera, "Body");
                if (bodyComponent == null)
                    return new ErrorResponse("No Body component found on this CinemachineCamera. Provide 'bodyType' to add one.");
            }

            // Set properties on body component
            SetComponentProperties(bodyComponent, props, new[] { "bodyType", "body_type" });
            CameraHelpers.MarkDirty(go);

            return new
            {
                success = true,
                message = $"Body configured on CinemachineCamera '{go.name}'.",
                data = new { instanceID = go.GetInstanceID(), body = bodyComponent.GetType().Name }
            };
        }

        internal static object SetAim(JObject @params)
        {
            var cmCamera = CameraHelpers.FindCinemachineCamera(@params);
            if (cmCamera == null) return new ErrorResponse("Target CinemachineCamera not found.");

            var props = CameraHelpers.ExtractProperties(@params) ?? new JObject();
            var go = cmCamera.gameObject;

            string aimTypeName = ParamCoercion.CoerceString(props["aimType"] ?? props["aim_type"], null);
            Component aimComponent;

            if (aimTypeName != null)
            {
                aimComponent = SwapPipelineComponent(go, "Aim", aimTypeName);
                if (aimComponent == null)
                    return new ErrorResponse($"Could not resolve aim component type '{aimTypeName}'.");
            }
            else
            {
                aimComponent = CameraHelpers.GetPipelineComponent(cmCamera, "Aim");
                if (aimComponent == null)
                    return new ErrorResponse("No Aim component found. Provide 'aimType' to add one.");
            }

            SetComponentProperties(aimComponent, props, new[] { "aimType", "aim_type" });
            CameraHelpers.MarkDirty(go);

            return new
            {
                success = true,
                message = $"Aim configured on CinemachineCamera '{go.name}'.",
                data = new { instanceID = go.GetInstanceID(), aim = aimComponent.GetType().Name }
            };
        }

        internal static object SetNoise(JObject @params)
        {
            var cmCamera = CameraHelpers.FindCinemachineCamera(@params);
            if (cmCamera == null) return new ErrorResponse("Target CinemachineCamera not found.");

            var props = CameraHelpers.ExtractProperties(@params) ?? new JObject();
            var go = cmCamera.gameObject;

            // Get or add noise component
            var noiseType = CameraHelpers.ResolveComponentType("CinemachineBasicMultiChannelPerlin");
            if (noiseType == null)
                return new ErrorResponse("CinemachineBasicMultiChannelPerlin type not found.");

            var noiseComponent = go.GetComponent(noiseType);
            bool added = false;
            if (noiseComponent == null)
            {
                noiseComponent = Undo.AddComponent(go, noiseType);
                added = true;
            }

            Undo.RecordObject(noiseComponent, "Set Cinemachine Noise");
            SetComponentProperties(noiseComponent, props, Array.Empty<string>());
            CameraHelpers.MarkDirty(go);

            return new
            {
                success = true,
                message = added
                    ? $"Added noise to CinemachineCamera '{go.name}'."
                    : $"Noise configured on CinemachineCamera '{go.name}'.",
                data = new { instanceID = go.GetInstanceID(), added }
            };
        }

        internal static object AddExtension(JObject @params)
        {
            var cmCamera = CameraHelpers.FindCinemachineCamera(@params);
            if (cmCamera == null) return new ErrorResponse("Target CinemachineCamera not found.");

            var props = CameraHelpers.ExtractProperties(@params) ?? new JObject();
            string extTypeName = ParamCoercion.CoerceString(
                props["extensionType"] ?? props["extension_type"], null);
            if (string.IsNullOrEmpty(extTypeName))
                return new ErrorResponse("'extensionType' property is required.");

            var extType = CameraHelpers.ResolveComponentType(extTypeName);
            if (extType == null)
                return new ErrorResponse($"Extension type '{extTypeName}' not found.");

            var go = cmCamera.gameObject;
            var existing = go.GetComponent(extType);
            if (existing != null)
                return new { success = true, message = $"Extension '{extTypeName}' already exists on '{go.name}'." };

            var ext = Undo.AddComponent(go, extType);
            SetComponentProperties(ext, props, new[] { "extensionType", "extension_type" });
            CameraHelpers.MarkDirty(go);

            return new
            {
                success = true,
                message = $"Extension '{extTypeName}' added to CinemachineCamera '{go.name}'.",
                data = new { instanceID = go.GetInstanceID(), extensionType = extTypeName }
            };
        }

        internal static object RemoveExtension(JObject @params)
        {
            var cmCamera = CameraHelpers.FindCinemachineCamera(@params);
            if (cmCamera == null) return new ErrorResponse("Target CinemachineCamera not found.");

            var props = CameraHelpers.ExtractProperties(@params) ?? new JObject();
            string extTypeName = ParamCoercion.CoerceString(
                props["extensionType"] ?? props["extension_type"], null);
            if (string.IsNullOrEmpty(extTypeName))
                return new ErrorResponse("'extensionType' property is required.");

            var extType = CameraHelpers.ResolveComponentType(extTypeName);
            if (extType == null)
                return new ErrorResponse($"Extension type '{extTypeName}' not found.");

            var go = cmCamera.gameObject;
            var ext = go.GetComponent(extType);
            if (ext == null)
                return new ErrorResponse($"Extension '{extTypeName}' not found on '{go.name}'.");

            Undo.DestroyObjectImmediate(ext);
            CameraHelpers.MarkDirty(go);

            return new
            {
                success = true,
                message = $"Extension '{extTypeName}' removed from CinemachineCamera '{go.name}'.",
                data = new { instanceID = go.GetInstanceID() }
            };
        }

        #endregion

        #region Helpers

        private static void SetFloatSubProp(SerializedProperty parent, string subPropName, JToken value)
        {
            if (value == null || value.Type == JTokenType.Null) return;
            var sub = parent.FindPropertyRelative(subPropName)
                   ?? parent.FindPropertyRelative("m_" + subPropName);
            if (sub != null && sub.propertyType == SerializedPropertyType.Float)
                sub.floatValue = ParamCoercion.CoerceFloat(value, sub.floatValue);
        }

        private static Component SwapPipelineComponent(GameObject go, string stage, string newTypeName)
        {
            var newType = CameraHelpers.ResolveComponentType(newTypeName);
            if (newType == null) return null;

            // Remove existing component of same pipeline stage
            var cmCamera = go.GetComponent(CameraHelpers.CinemachineCameraType);
            if (cmCamera != null)
            {
                var existing = CameraHelpers.GetPipelineComponent(cmCamera, stage);
                if (existing != null && existing.GetType() != newType)
                    Undo.DestroyObjectImmediate(existing);
            }

            // Add new component if not already present
            var comp = go.GetComponent(newType);
            if (comp == null)
                comp = Undo.AddComponent(go, newType);

            return comp;
        }

        private static void SetComponentProperties(Component component, JObject props, string[] skipKeys)
        {
            if (component == null || props == null) return;

            var skipSet = new System.Collections.Generic.HashSet<string>(
                skipKeys, StringComparer.OrdinalIgnoreCase);

            Undo.RecordObject(component, $"Configure {component.GetType().Name}");

            foreach (var kv in props)
            {
                if (skipSet.Contains(kv.Key)) continue;
                ComponentOps.SetProperty(component, kv.Key, kv.Value, out _);
            }
        }

        #endregion
    }
}
