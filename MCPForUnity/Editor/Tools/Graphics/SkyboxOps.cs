using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using MCPForUnity.Editor.Helpers;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace MCPForUnity.Editor.Tools.Graphics
{
    internal static class SkyboxOps
    {
        // ---------------------------------------------------------------
        // skybox_get — read all environment settings
        // ---------------------------------------------------------------
        public static object GetEnvironment(JObject @params)
        {
            var skyMat = RenderSettings.skybox;
            var sun = RenderSettings.sun;

            object matInfo = null;
            if (skyMat != null)
            {
                var props = new List<object>();
                int count = skyMat.shader.GetPropertyCount();
                for (int i = 0; i < count; i++)
                {
                    string propName = skyMat.shader.GetPropertyName(i);
                    var propType = skyMat.shader.GetPropertyType(i);
                    object val = ReadMaterialProperty(skyMat, propName, propType);
                    props.Add(new { name = propName, type = propType.ToString(), value = val });
                }
                matInfo = new
                {
                    name = skyMat.name,
                    shader = skyMat.shader.name,
                    path = AssetDatabase.GetAssetPath(skyMat),
                    properties = props
                };
            }

            return new
            {
                success = true,
                message = "Environment settings retrieved.",
                data = new
                {
                    skybox = matInfo,
                    ambient = new
                    {
                        mode = RenderSettings.ambientMode.ToString(),
                        skyColor = ColorToArray(RenderSettings.ambientSkyColor),
                        equatorColor = ColorToArray(RenderSettings.ambientEquatorColor),
                        groundColor = ColorToArray(RenderSettings.ambientGroundColor),
                        ambientLight = ColorToArray(RenderSettings.ambientLight),
                        intensity = RenderSettings.ambientIntensity
                    },
                    fog = new
                    {
                        enabled = RenderSettings.fog,
                        mode = RenderSettings.fogMode.ToString(),
                        color = ColorToArray(RenderSettings.fogColor),
                        density = RenderSettings.fogDensity,
                        startDistance = RenderSettings.fogStartDistance,
                        endDistance = RenderSettings.fogEndDistance
                    },
                    reflection = new
                    {
                        intensity = RenderSettings.reflectionIntensity,
                        bounces = RenderSettings.reflectionBounces,
                        mode = RenderSettings.defaultReflectionMode.ToString(),
                        resolution = RenderSettings.defaultReflectionResolution,
                        customCubemap = RenderSettings.customReflectionTexture != null
                            ? AssetDatabase.GetAssetPath(RenderSettings.customReflectionTexture)
                            : null
                    },
                    sun = sun != null
                        ? (object)new { name = sun.gameObject.name, instanceID = sun.gameObject.GetInstanceID() }
                        : null,
                    subtractiveShadowColor = ColorToArray(RenderSettings.subtractiveShadowColor)
                }
            };
        }

        // ---------------------------------------------------------------
        // skybox_set_material — assign a skybox material
        // ---------------------------------------------------------------
        public static object SetMaterial(JObject @params)
        {
            var p = new ToolParams(@params);
            string materialPath = p.Get("material") ?? p.Get("path") ?? p.Get("material_path");
            if (string.IsNullOrEmpty(materialPath))
                return new ErrorResponse("'material' (asset path) is required.");

            var mat = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (mat == null)
                return new ErrorResponse($"Material not found at '{materialPath}'.");

            RenderSettings.skybox = mat;
            MarkSceneDirty();

            return new
            {
                success = true,
                message = $"Skybox set to '{mat.name}' (shader: {mat.shader.name}).",
                data = new
                {
                    material = mat.name,
                    shader = mat.shader.name,
                    path = materialPath
                }
            };
        }

        // ---------------------------------------------------------------
        // skybox_set_properties — modify properties on the current skybox material
        // ---------------------------------------------------------------
        public static object SetMaterialProperties(JObject @params)
        {
            var p = new ToolParams(@params);
            var skyMat = RenderSettings.skybox;
            if (skyMat == null)
                return new ErrorResponse("No skybox material is set.");

            var propsRaw = p.GetRaw("properties") ?? p.GetRaw("parameters");
            if (propsRaw == null || propsRaw.Type != JTokenType.Object)
                return new ErrorResponse("'properties' dict is required.");

            var set = new List<string>();
            var failed = new List<string>();

            foreach (var kvp in (JObject)propsRaw)
            {
                string propName = kvp.Key;
                if (!skyMat.HasProperty(propName))
                {
                    string altName = "_" + propName;
                    if (skyMat.HasProperty(altName))
                        propName = altName;
                    else
                    {
                        failed.Add(kvp.Key);
                        continue;
                    }
                }

                if (SetMaterialProperty(skyMat, propName, kvp.Value))
                    set.Add(kvp.Key);
                else
                    failed.Add(kvp.Key);
            }

            EditorUtility.SetDirty(skyMat);
            MarkSceneDirty();

            return new
            {
                success = true,
                message = $"Set {set.Count} property(ies) on skybox material '{skyMat.name}'.",
                data = new { material = skyMat.name, set, failed }
            };
        }

        // ---------------------------------------------------------------
        // skybox_set_ambient — set ambient lighting mode and colors
        // ---------------------------------------------------------------
        public static object SetAmbient(JObject @params)
        {
            var p = new ToolParams(@params);

            string modeStr = p.Get("ambient_mode") ?? p.Get("mode");
            if (!string.IsNullOrEmpty(modeStr))
            {
                if (Enum.TryParse<AmbientMode>(modeStr, true, out var mode))
                    RenderSettings.ambientMode = mode;
                else
                    return new ErrorResponse(
                        $"Invalid ambient mode '{modeStr}'. Valid: Skybox, Trilight, Flat, Custom.");
            }

            var skyColor = ParseColorToken(p.GetRaw("color") ?? p.GetRaw("sky_color"));
            if (skyColor.HasValue)
                RenderSettings.ambientSkyColor = skyColor.Value;

            var equatorColor = ParseColorToken(p.GetRaw("equator_color"));
            if (equatorColor.HasValue)
                RenderSettings.ambientEquatorColor = equatorColor.Value;

            var groundColor = ParseColorToken(p.GetRaw("ground_color"));
            if (groundColor.HasValue)
                RenderSettings.ambientGroundColor = groundColor.Value;

            var intensity = p.GetFloat("intensity");
            if (intensity.HasValue)
                RenderSettings.ambientIntensity = intensity.Value;

            MarkSceneDirty();

            return new
            {
                success = true,
                message = $"Ambient lighting updated (mode: {RenderSettings.ambientMode}).",
                data = new
                {
                    mode = RenderSettings.ambientMode.ToString(),
                    skyColor = ColorToArray(RenderSettings.ambientSkyColor),
                    equatorColor = ColorToArray(RenderSettings.ambientEquatorColor),
                    groundColor = ColorToArray(RenderSettings.ambientGroundColor),
                    intensity = RenderSettings.ambientIntensity
                }
            };
        }

        // ---------------------------------------------------------------
        // skybox_set_fog — enable/configure fog
        // ---------------------------------------------------------------
        public static object SetFog(JObject @params)
        {
            var p = new ToolParams(@params);

            var enabledToken = p.GetRaw("fog_enabled") ?? p.GetRaw("enabled");
            if (enabledToken != null && enabledToken.Type != JTokenType.Null)
                RenderSettings.fog = ParamCoercion.CoerceBool(enabledToken, RenderSettings.fog);

            string modeStr = p.Get("fog_mode") ?? p.Get("mode");
            if (!string.IsNullOrEmpty(modeStr))
            {
                if (Enum.TryParse<FogMode>(modeStr, true, out var fogMode))
                    RenderSettings.fogMode = fogMode;
                else
                    return new ErrorResponse(
                        $"Invalid fog mode '{modeStr}'. Valid: Linear, Exponential, ExponentialSquared.");
            }

            var fogColor = ParseColorToken(p.GetRaw("fog_color") ?? p.GetRaw("color"));
            if (fogColor.HasValue)
                RenderSettings.fogColor = fogColor.Value;

            var density = p.GetFloat("fog_density") ?? p.GetFloat("density");
            if (density.HasValue)
                RenderSettings.fogDensity = density.Value;

            var start = p.GetFloat("fog_start") ?? p.GetFloat("start");
            if (start.HasValue)
                RenderSettings.fogStartDistance = start.Value;

            var end = p.GetFloat("fog_end") ?? p.GetFloat("end");
            if (end.HasValue)
                RenderSettings.fogEndDistance = end.Value;

            MarkSceneDirty();

            return new
            {
                success = true,
                message = $"Fog settings updated (enabled: {RenderSettings.fog}, mode: {RenderSettings.fogMode}).",
                data = new
                {
                    enabled = RenderSettings.fog,
                    mode = RenderSettings.fogMode.ToString(),
                    color = ColorToArray(RenderSettings.fogColor),
                    density = RenderSettings.fogDensity,
                    startDistance = RenderSettings.fogStartDistance,
                    endDistance = RenderSettings.fogEndDistance
                }
            };
        }

        // ---------------------------------------------------------------
        // skybox_set_reflection — configure environment reflections
        // ---------------------------------------------------------------
        public static object SetReflection(JObject @params)
        {
            var p = new ToolParams(@params);

            var intensity = p.GetFloat("intensity");
            if (intensity.HasValue)
                RenderSettings.reflectionIntensity = intensity.Value;

            var bounces = p.GetInt("bounces");
            if (bounces.HasValue)
                RenderSettings.reflectionBounces = bounces.Value;

            string modeStr = p.Get("reflection_mode") ?? p.Get("mode");
            if (!string.IsNullOrEmpty(modeStr))
            {
                if (Enum.TryParse<DefaultReflectionMode>(modeStr, true, out var mode))
                    RenderSettings.defaultReflectionMode = mode;
                else
                    return new ErrorResponse(
                        $"Invalid reflection mode '{modeStr}'. Valid: Skybox, Custom.");
            }

            var resolution = p.GetInt("resolution");
            if (resolution.HasValue)
                RenderSettings.defaultReflectionResolution = resolution.Value;

            string cubemapPath = p.Get("path") ?? p.Get("cubemap_path");
            if (!string.IsNullOrEmpty(cubemapPath))
            {
                var cubemap = AssetDatabase.LoadAssetAtPath<Texture>(cubemapPath);
                if (cubemap != null)
                    RenderSettings.customReflectionTexture = cubemap;
                else
                    return new ErrorResponse($"Cubemap not found at '{cubemapPath}'.");
            }

            MarkSceneDirty();

            return new
            {
                success = true,
                message = $"Reflection settings updated (intensity: {RenderSettings.reflectionIntensity}, bounces: {RenderSettings.reflectionBounces}).",
                data = new
                {
                    intensity = RenderSettings.reflectionIntensity,
                    bounces = RenderSettings.reflectionBounces,
                    mode = RenderSettings.defaultReflectionMode.ToString(),
                    resolution = RenderSettings.defaultReflectionResolution,
                    customCubemap = RenderSettings.customReflectionTexture != null
                        ? AssetDatabase.GetAssetPath(RenderSettings.customReflectionTexture)
                        : null
                }
            };
        }

        // ---------------------------------------------------------------
        // skybox_set_sun — set the sun source light
        // ---------------------------------------------------------------
        public static object SetSun(JObject @params)
        {
            var p = new ToolParams(@params);
            string target = p.Get("target") ?? p.Get("name");
            if (string.IsNullOrEmpty(target))
                return new ErrorResponse("'target' (light GameObject name or instance ID) is required.");

            GameObject go = null;
            if (int.TryParse(target, out int instanceId))
                go = GameObjectLookup.ResolveInstanceID(instanceId) as GameObject;
            if (go == null)
                go = GameObject.Find(target);
            if (go == null)
                return new ErrorResponse($"GameObject '{target}' not found.");

            var light = go.GetComponent<Light>();
            if (light == null)
                return new ErrorResponse($"'{go.name}' does not have a Light component.");

            RenderSettings.sun = light;
            MarkSceneDirty();

            return new
            {
                success = true,
                message = $"Sun source set to '{go.name}'.",
                data = new
                {
                    name = go.name,
                    instanceID = go.GetInstanceID(),
                    lightType = light.type.ToString()
                }
            };
        }

        // ---------------------------------------------------------------
        // Helpers
        // ---------------------------------------------------------------

        private static float[] ColorToArray(Color c)
        {
            return new[] { c.r, c.g, c.b, c.a };
        }

        private static Color ArrayToColor(float[] arr)
        {
            return new Color(
                arr[0], arr[1], arr[2],
                arr.Length >= 4 ? arr[3] : 1f);
        }

        private static Color? ParseColorToken(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null) return null;
            if (token is JArray arr && arr.Count >= 3)
            {
                return new Color(
                    (float)arr[0], (float)arr[1], (float)arr[2],
                    arr.Count >= 4 ? (float)arr[3] : 1f);
            }
            return null;
        }

        private static object ReadMaterialProperty(Material mat, string propName, ShaderPropertyType propType)
        {
            switch (propType)
            {
                case ShaderPropertyType.Color:
                    return ColorToArray(mat.GetColor(propName));
                case ShaderPropertyType.Float:
                case ShaderPropertyType.Range:
                    return mat.GetFloat(propName);
                case ShaderPropertyType.Int:
                    return mat.GetInt(propName);
                case ShaderPropertyType.Vector:
                    var v = mat.GetVector(propName);
                    return new[] { v.x, v.y, v.z, v.w };
                case ShaderPropertyType.Texture:
                    var tex = mat.GetTexture(propName);
                    return tex != null ? AssetDatabase.GetAssetPath(tex) : null;
                default:
                    return null;
            }
        }

        private static bool SetMaterialProperty(Material mat, string propName, JToken value)
        {
            int propIdx = mat.shader.FindPropertyIndex(propName);
            if (propIdx < 0) return false;

            var propType = mat.shader.GetPropertyType(propIdx);
            try
            {
                switch (propType)
                {
                    case ShaderPropertyType.Color:
                        if (value is JArray colorArr && colorArr.Count >= 3)
                        {
                            mat.SetColor(propName, new Color(
                                (float)colorArr[0], (float)colorArr[1], (float)colorArr[2],
                                colorArr.Count >= 4 ? (float)colorArr[3] : 1f));
                            return true;
                        }
                        return false;
                    case ShaderPropertyType.Float:
                    case ShaderPropertyType.Range:
                        mat.SetFloat(propName, (float)value);
                        return true;
                    case ShaderPropertyType.Int:
                        mat.SetInt(propName, (int)value);
                        return true;
                    case ShaderPropertyType.Vector:
                        if (value is JArray vecArr && vecArr.Count >= 2)
                        {
                            mat.SetVector(propName, new Vector4(
                                (float)vecArr[0], (float)vecArr[1],
                                vecArr.Count >= 3 ? (float)vecArr[2] : 0f,
                                vecArr.Count >= 4 ? (float)vecArr[3] : 0f));
                            return true;
                        }
                        return false;
                    case ShaderPropertyType.Texture:
                        if (value.Type == JTokenType.String)
                        {
                            var tex = AssetDatabase.LoadAssetAtPath<Texture>(value.ToString());
                            if (tex != null) { mat.SetTexture(propName, tex); return true; }
                        }
                        else if (value.Type == JTokenType.Null)
                        {
                            mat.SetTexture(propName, null);
                            return true;
                        }
                        return false;
                    default:
                        return false;
                }
            }
            catch
            {
                return false;
            }
        }

        private static void MarkSceneDirty()
        {
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());
        }
    }
}
