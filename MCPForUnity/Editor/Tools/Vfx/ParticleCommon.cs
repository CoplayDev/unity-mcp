using Newtonsoft.Json.Linq;
using UnityEngine;

namespace MCPForUnity.Editor.Tools.Vfx
{
    internal static class ParticleCommon
    {
        public static ParticleSystem FindParticleSystem(JObject @params)
        {
            GameObject go = ManageVfxCommon.FindTargetGameObject(@params);
            return go?.GetComponent<ParticleSystem>();
        }

        public static ParticleSystem.MinMaxCurve ParseMinMaxCurve(JToken token, float defaultValue = 1f)
        {
            if (token == null)
                return new ParticleSystem.MinMaxCurve(defaultValue);

            if (token.Type == JTokenType.Float || token.Type == JTokenType.Integer)
            {
                return new ParticleSystem.MinMaxCurve(token.ToObject<float>());
            }

            if (token is JObject obj)
            {
                string mode = obj["mode"]?.ToString()?.ToLowerInvariant() ?? "constant";

                switch (mode)
                {
                    case "constant":
                        float constant = obj["value"]?.ToObject<float>() ?? defaultValue;
                        return new ParticleSystem.MinMaxCurve(constant);

                    case "random_between_constants":
                    case "two_constants":
                        float min = obj["min"]?.ToObject<float>() ?? 0f;
                        float max = obj["max"]?.ToObject<float>() ?? 1f;
                        return new ParticleSystem.MinMaxCurve(min, max);

                    case "curve":
                        AnimationCurve curve = ManageVfxCommon.ParseAnimationCurve(obj, defaultValue);
                        return new ParticleSystem.MinMaxCurve(obj["multiplier"]?.ToObject<float>() ?? 1f, curve);

                    default:
                        return new ParticleSystem.MinMaxCurve(defaultValue);
                }
            }

            return new ParticleSystem.MinMaxCurve(defaultValue);
        }

        public static ParticleSystem.MinMaxGradient ParseMinMaxGradient(JToken token)
        {
            if (token == null)
                return new ParticleSystem.MinMaxGradient(Color.white);

            if (token is JArray arr && arr.Count >= 3)
            {
                return new ParticleSystem.MinMaxGradient(ManageVfxCommon.ParseColor(arr));
            }

            if (token is JObject obj)
            {
                string mode = obj["mode"]?.ToString()?.ToLowerInvariant() ?? "color";

                switch (mode)
                {
                    case "color":
                        return new ParticleSystem.MinMaxGradient(ManageVfxCommon.ParseColor(obj["color"]));

                    case "two_colors":
                        Color colorMin = ManageVfxCommon.ParseColor(obj["colorMin"]);
                        Color colorMax = ManageVfxCommon.ParseColor(obj["colorMax"]);
                        return new ParticleSystem.MinMaxGradient(colorMin, colorMax);

                    case "gradient":
                        return new ParticleSystem.MinMaxGradient(ManageVfxCommon.ParseGradient(obj));

                    default:
                        return new ParticleSystem.MinMaxGradient(Color.white);
                }
            }

            return new ParticleSystem.MinMaxGradient(Color.white);
        }
    }
}
