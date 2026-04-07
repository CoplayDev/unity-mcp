using Newtonsoft.Json.Linq;
using MCPForUnity.Editor.Helpers;
using UnityEngine;

namespace MCPForUnity.Editor.Tools.Vfx
{
    internal static class ManageVfxCommon
    {
        public static Color ParseColor(JToken token) => VectorParsing.ParseColorOrDefault(token);
        public static Vector3 ParseVector3(JToken token) => VectorParsing.ParseVector3OrDefault(token);
        public static Vector4 ParseVector4(JToken token) => VectorParsing.ParseVector4OrDefault(token);
        public static Gradient ParseGradient(JToken token) => VectorParsing.ParseGradientOrDefault(token);
        public static AnimationCurve ParseAnimationCurve(JToken token, float defaultValue = 1f)
            => VectorParsing.ParseAnimationCurveOrDefault(token, defaultValue);

        public static GameObject FindTargetGameObject(JObject @params)
            => ObjectResolver.ResolveGameObject(@params["target"], @params["searchMethod"]?.ToString());

        public static Material FindMaterialByPath(string path)
            => ObjectResolver.ResolveMaterial(path);

        public static T FindComponent<T>(JObject @params) where T : Component
        {
            GameObject go = FindTargetGameObject(@params);
            if (go == null) return null;
            int? idx = ParamCoercion.CoerceIntNullable(@params["componentIndex"] ?? @params["component_index"]);
            if (idx.HasValue)
            {
                var all = go.GetComponents<T>();
                return (idx.Value >= 0 && idx.Value < all.Length) ? all[idx.Value] : null;
            }
            return go.GetComponent<T>();
        }

        public static string FindComponentError<T>(JObject @params) where T : Component
        {
            string typeName = typeof(T).Name;
            GameObject go = FindTargetGameObject(@params);
            if (go == null) return $"{typeName} not found";
            int? idx = ParamCoercion.CoerceIntNullable(@params["componentIndex"] ?? @params["component_index"]);
            if (idx.HasValue)
            {
                int count = go.GetComponents<T>().Length;
                if (idx.Value < 0 || idx.Value >= count)
                    return $"component_index {idx.Value} out of range. Found {count} {typeName} component(s) on '{go.name}'.";
            }
            return $"{typeName} not found";
        }
    }
}
