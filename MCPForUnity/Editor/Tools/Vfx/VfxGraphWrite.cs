using System;
using MCPForUnity.Editor.Helpers;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

#if UNITY_VFX_GRAPH
using UnityEngine.VFX;
#endif

namespace MCPForUnity.Editor.Tools.Vfx
{
    /// <summary>
    /// Parameter setter operations for VFX Graph (VisualEffect component).
    /// Requires com.unity.visualeffectgraph package and UNITY_VFX_GRAPH symbol.
    /// </summary>
    internal static class VfxGraphWrite
    {
#if !UNITY_VFX_GRAPH
        public static object SetParameter<T>(JObject @params, Action<object, string, T> setter)
        {
            return new { success = false, message = "VFX Graph package (com.unity.visualeffectgraph) not installed" };
        }

        public static object SetVector(JObject @params, int dims)
        {
            return new { success = false, message = "VFX Graph package (com.unity.visualeffectgraph) not installed" };
        }

        public static object SetColor(JObject @params)
        {
            return new { success = false, message = "VFX Graph package (com.unity.visualeffectgraph) not installed" };
        }

        public static object SetGradient(JObject @params)
        {
            return new { success = false, message = "VFX Graph package (com.unity.visualeffectgraph) not installed" };
        }

        public static object SetTexture(JObject @params)
        {
            return new { success = false, message = "VFX Graph package (com.unity.visualeffectgraph) not installed" };
        }

        public static object SetMesh(JObject @params)
        {
            return new { success = false, message = "VFX Graph package (com.unity.visualeffectgraph) not installed" };
        }

        public static object SetCurve(JObject @params)
        {
            return new { success = false, message = "VFX Graph package (com.unity.visualeffectgraph) not installed" };
        }

        public static object SendEvent(JObject @params)
        {
            return new { success = false, message = "VFX Graph package (com.unity.visualeffectgraph) not installed" };
        }
#else
        public static object SetParameter<T>(JObject @params, Action<VisualEffect, string, T> setter)
        {
            VisualEffect vfx = VfxGraphCommon.FindVisualEffect(@params);
            if (vfx == null)
            {
                return new { success = false, message = "VisualEffect not found" };
            }

            string param = @params["parameter"]?.ToString();
            if (string.IsNullOrEmpty(param))
            {
                return new { success = false, message = "Parameter name required" };
            }

            JToken valueToken = @params["value"];
            if (valueToken == null)
            {
                return new { success = false, message = "Value required" };
            }

            // Safely deserialize the value
            T value;
            try
            {
                value = valueToken.ToObject<T>();
            }
            catch (JsonException ex)
            {
                return new { success = false, message = $"Invalid value for {param}: {ex.Message}" };
            }
            catch (InvalidCastException ex)
            {
                return new { success = false, message = $"Invalid value type for {param}: {ex.Message}" };
            }

            Undo.RecordObject(vfx, $"Set VFX {param}");
            setter(vfx, param, value);
            EditorUtility.SetDirty(vfx);

            return new { success = true, message = $"Set {param} = {value}" };
        }

        public static object SetVector(JObject @params, int dims)
        {
            VisualEffect vfx = VfxGraphCommon.FindVisualEffect(@params);
            if (vfx == null)
            {
                return new { success = false, message = "VisualEffect not found" };
            }

            string param = @params["parameter"]?.ToString();
            if (string.IsNullOrEmpty(param))
            {
                return new { success = false, message = "Parameter name required" };
            }

            if (dims != 2 && dims != 3 && dims != 4)
            {
                return new { success = false, message = $"Unsupported vector dimension: {dims}. Expected 2, 3, or 4." };
            }

            Vector4 vec = ManageVfxCommon.ParseVector4(@params["value"]);
            Undo.RecordObject(vfx, $"Set VFX {param}");

            switch (dims)
            {
                case 2: vfx.SetVector2(param, new Vector2(vec.x, vec.y)); break;
                case 3: vfx.SetVector3(param, new Vector3(vec.x, vec.y, vec.z)); break;
                case 4: vfx.SetVector4(param, vec); break;
            }

            EditorUtility.SetDirty(vfx);
            return new { success = true, message = $"Set {param}" };
        }

        public static object SetColor(JObject @params)
        {
            VisualEffect vfx = VfxGraphCommon.FindVisualEffect(@params);
            if (vfx == null)
            {
                return new { success = false, message = "VisualEffect not found" };
            }

            string param = @params["parameter"]?.ToString();
            if (string.IsNullOrEmpty(param))
            {
                return new { success = false, message = "Parameter name required" };
            }

            Color color = ManageVfxCommon.ParseColor(@params["value"]);
            Undo.RecordObject(vfx, $"Set VFX Color {param}");
            vfx.SetVector4(param, new Vector4(color.r, color.g, color.b, color.a));
            EditorUtility.SetDirty(vfx);

            return new { success = true, message = $"Set color {param}" };
        }

        public static object SetGradient(JObject @params)
        {
            VisualEffect vfx = VfxGraphCommon.FindVisualEffect(@params);
            if (vfx == null)
            {
                return new { success = false, message = "VisualEffect not found" };
            }

            string param = @params["parameter"]?.ToString();
            if (string.IsNullOrEmpty(param))
            {
                return new { success = false, message = "Parameter name required" };
            }

            Gradient gradient = ManageVfxCommon.ParseGradient(@params["gradient"]);
            Undo.RecordObject(vfx, $"Set VFX Gradient {param}");
            vfx.SetGradient(param, gradient);
            EditorUtility.SetDirty(vfx);

            return new { success = true, message = $"Set gradient {param}" };
        }

        public static object SetTexture(JObject @params)
        {
            VisualEffect vfx = VfxGraphCommon.FindVisualEffect(@params);
            if (vfx == null)
            {
                return new { success = false, message = "VisualEffect not found" };
            }

            string param = @params["parameter"]?.ToString();
            string path = @params["texturePath"]?.ToString();
            if (string.IsNullOrEmpty(param) || string.IsNullOrEmpty(path))
            {
                return new { success = false, message = "Parameter and texturePath required" };
            }

            var findInst = new JObject { ["find"] = path };
            Texture tex = ObjectResolver.Resolve(findInst, typeof(Texture)) as Texture;
            if (tex == null)
            {
                return new { success = false, message = $"Texture not found: {path}" };
            }

            Undo.RecordObject(vfx, $"Set VFX Texture {param}");
            vfx.SetTexture(param, tex);
            EditorUtility.SetDirty(vfx);

            return new { success = true, message = $"Set texture {param} = {tex.name}" };
        }

        public static object SetMesh(JObject @params)
        {
            VisualEffect vfx = VfxGraphCommon.FindVisualEffect(@params);
            if (vfx == null)
            {
                return new { success = false, message = "VisualEffect not found" };
            }

            string param = @params["parameter"]?.ToString();
            string path = @params["meshPath"]?.ToString();
            if (string.IsNullOrEmpty(param) || string.IsNullOrEmpty(path))
            {
                return new { success = false, message = "Parameter and meshPath required" };
            }

            var findInst = new JObject { ["find"] = path };
            Mesh mesh = ObjectResolver.Resolve(findInst, typeof(Mesh)) as Mesh;
            if (mesh == null)
            {
                return new { success = false, message = $"Mesh not found: {path}" };
            }

            Undo.RecordObject(vfx, $"Set VFX Mesh {param}");
            vfx.SetMesh(param, mesh);
            EditorUtility.SetDirty(vfx);

            return new { success = true, message = $"Set mesh {param} = {mesh.name}" };
        }

        public static object SetCurve(JObject @params)
        {
            VisualEffect vfx = VfxGraphCommon.FindVisualEffect(@params);
            if (vfx == null)
            {
                return new { success = false, message = "VisualEffect not found" };
            }

            string param = @params["parameter"]?.ToString();
            if (string.IsNullOrEmpty(param))
            {
                return new { success = false, message = "Parameter name required" };
            }

            AnimationCurve curve = ManageVfxCommon.ParseAnimationCurve(@params["curve"], 1f);
            Undo.RecordObject(vfx, $"Set VFX Curve {param}");
            vfx.SetAnimationCurve(param, curve);
            EditorUtility.SetDirty(vfx);

            return new { success = true, message = $"Set curve {param}" };
        }

        public static object SendEvent(JObject @params)
        {
            VisualEffect vfx = VfxGraphCommon.FindVisualEffect(@params);
            if (vfx == null)
            {
                return new { success = false, message = "VisualEffect not found" };
            }

            string eventName = @params["eventName"]?.ToString();
            if (string.IsNullOrEmpty(eventName))
            {
                return new { success = false, message = "Event name required" };
            }

            VFXEventAttribute attr = vfx.CreateVFXEventAttribute();
            if (@params["position"] != null)
            {
                attr.SetVector3("position", ManageVfxCommon.ParseVector3(@params["position"]));
            }
            if (@params["velocity"] != null)
            {
                attr.SetVector3("velocity", ManageVfxCommon.ParseVector3(@params["velocity"]));
            }
            if (@params["color"] != null)
            {
                var c = ManageVfxCommon.ParseColor(@params["color"]);
                attr.SetVector3("color", new Vector3(c.r, c.g, c.b));
            }
            if (@params["size"] != null)
            {
                float? sizeValue = @params["size"].Value<float?>();
                if (sizeValue.HasValue)
                {
                    attr.SetFloat("size", sizeValue.Value);
                }
            }
            if (@params["lifetime"] != null)
            {
                float? lifetimeValue = @params["lifetime"].Value<float?>();
                if (lifetimeValue.HasValue)
                {
                    attr.SetFloat("lifetime", lifetimeValue.Value);
                }
            }

            vfx.SendEvent(eventName, attr);
            return new { success = true, message = $"Sent event '{eventName}'" };
        }
#endif
    }
}
