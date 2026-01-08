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
    }
}
