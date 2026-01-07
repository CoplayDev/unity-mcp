using System;
using System.Collections.Generic;
using System.Linq;
using MCPForUnity.Editor.Tools;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace MCPForUnity.Editor.Helpers
{
    public static class MaterialOps
    {
        /// <summary>
        /// Applies a set of properties (JObject) to a material, handling aliases and structured formats.
        /// </summary>
        public static bool ApplyProperties(Material mat, JObject properties, JsonSerializer serializer)
        {
            if (mat == null || properties == null)
                return false;
            bool modified = false;

            // Helper for case-insensitive lookup
            JToken GetValue(string key)
            {
                return properties.Properties()
                    .FirstOrDefault(p => string.Equals(p.Name, key, StringComparison.OrdinalIgnoreCase))?.Value;
            }

            // --- Structured / Legacy Format Handling ---
            // Example: Set shader
            var shaderToken = GetValue("shader");
            if (shaderToken?.Type == JTokenType.String)
            {
                string shaderRequest = shaderToken.ToString();
                // Set shader
                Shader newShader = RenderPipelineUtility.ResolveShader(shaderRequest);
                if (newShader != null && mat.shader != newShader)
                {
                    mat.shader = newShader;
                    modified = true;
                }
            }

            // Example: Set color property (structured)
            var colorToken = GetValue("color");
            if (colorToken is JObject colorProps)
            {
                string propName = colorProps["name"]?.ToString() ?? GetMainColorPropertyName(mat);
                if (colorProps["value"] is JArray colArr && colArr.Count >= 3)
                {
                    try
                    {
                        Color newColor = ParseColor(colArr, serializer);
                        if (mat.HasProperty(propName))
                        {
                            if (mat.GetColor(propName) != newColor)
                            {
                                mat.SetColor(propName, newColor);
                                modified = true;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        McpLog.Warn($"[MaterialOps] Failed to parse color for property '{propName}': {ex.Message}");
                    }
                }
            }
            else if (colorToken is JArray colorArr) // Structured shorthand
            {
                string propName = GetMainColorPropertyName(mat);
                try
                {
                    Color newColor = ParseColor(colorArr, serializer);
                    if (mat.HasProperty(propName) && mat.GetColor(propName) != newColor)
                    {
                        mat.SetColor(propName, newColor);
                        modified = true;
                    }
                }
                catch (Exception ex)
                {
                    McpLog.Warn($"[MaterialOps] Failed to parse color array: {ex.Message}");
                }
            }

            // Example: Set float property (structured)
            var floatToken = GetValue("float");
            if (floatToken is JObject floatProps)
            {
                string propName = floatProps["name"]?.ToString();
                if (!string.IsNullOrEmpty(propName) &&
                   (floatProps["value"]?.Type == JTokenType.Float || floatProps["value"]?.Type == JTokenType.Integer))
                {
                    try
                    {
                        float newVal = floatProps["value"].ToObject<float>();
                        if (mat.HasProperty(propName) && mat.GetFloat(propName) != newVal)
                        {
                            mat.SetFloat(propName, newVal);
                            modified = true;
                        }
                    }
                    catch (Exception ex)
                    {
                        McpLog.Warn($"[MaterialOps] Failed to set float property '{propName}': {ex.Message}");
                    }
                }
            }

            // Example: Set texture property (structured)
            {
                var texToken = GetValue("texture");
                if (texToken is JObject texProps)
                {
                    string rawName = (texProps["name"] ?? texProps["Name"])?.ToString();
                    string texPath = (texProps["path"] ?? texProps["Path"])?.ToString();
                    if (!string.IsNullOrEmpty(texPath))
                    {
                        var sanitizedPath = AssetPathUtility.SanitizeAssetPath(texPath);
                        var newTex = AssetDatabase.LoadAssetAtPath<Texture>(sanitizedPath);
                        // Use ResolvePropertyName to handle aliases even for structured texture names
                        string candidateName = string.IsNullOrEmpty(rawName) ? "_BaseMap" : rawName;
                        string targetProp = ResolvePropertyName(mat, candidateName);

                        if (!string.IsNullOrEmpty(targetProp) && mat.HasProperty(targetProp))
                        {
                            if (mat.GetTexture(targetProp) != newTex)
                            {
                                mat.SetTexture(targetProp, newTex);
                                modified = true;
                            }
                        }
                    }
                }
            }

            // --- Direct Property Assignment (Flexible) ---
            var reservedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "shader", "color", "float", "texture" };

            foreach (var prop in properties.Properties())
            {
                if (reservedKeys.Contains(prop.Name)) continue;
                string shaderProp = ResolvePropertyName(mat, prop.Name);
                JToken v = prop.Value;

                if (TrySetShaderProperty(mat, shaderProp, v, serializer))
                {
                    modified = true;
                }
            }

            return modified;
        }

        /// <summary>
        /// Resolves common property aliases (e.g. "metallic" -> "_Metallic").
        /// </summary>
        public static string ResolvePropertyName(Material mat, string name)
        {
            if (mat == null || string.IsNullOrEmpty(name)) return name;
            string[] candidates;
            var lower = name.ToLowerInvariant();
            switch (lower)
            {
                case "_color": candidates = new[] { "_Color", "_BaseColor" }; break;
                case "_basecolor": candidates = new[] { "_BaseColor", "_Color" }; break;
                case "_maintex": candidates = new[] { "_MainTex", "_BaseMap" }; break;
                case "_basemap": candidates = new[] { "_BaseMap", "_MainTex" }; break;
                case "_glossiness": candidates = new[] { "_Glossiness", "_Smoothness" }; break;
                case "_smoothness": candidates = new[] { "_Smoothness", "_Glossiness" }; break;
                // Friendly names → shader property names
                case "metallic": candidates = new[] { "_Metallic" }; break;
                case "smoothness": candidates = new[] { "_Smoothness", "_Glossiness" }; break;
                case "albedo": candidates = new[] { "_BaseMap", "_MainTex" }; break;
                default: candidates = new[] { name }; break; // keep original as-is
            }
            foreach (var candidate in candidates)
            {
                if (mat.HasProperty(candidate)) return candidate;
            }
            return name;
        }

        /// <summary>
        /// Auto-detects the main color property name for a material's shader.
        /// </summary>
        public static string GetMainColorPropertyName(Material mat)
        {
            if (mat == null || mat.shader == null)
                return "_Color";

            string[] commonColorProps = { "_BaseColor", "_Color", "_MainColor", "_Tint", "_TintColor" };
            foreach (var prop in commonColorProps)
            {
                if (mat.HasProperty(prop))
                    return prop;
            }
            return "_Color";
        }

        /// <summary>
        /// Tries to set a shader property on a material based on a JToken value.
        /// Handles Colors, Vectors, Floats, Ints, Booleans, and Textures.
        /// </summary>
        public static bool TrySetShaderProperty(Material material, string propertyName, JToken value, JsonSerializer serializer)
        {
            if (material == null || string.IsNullOrEmpty(propertyName) || value == null)
                return false;

            // Handle stringified JSON
            if (value.Type == JTokenType.String)
            {
                string s = value.ToString();
                if (s.TrimStart().StartsWith("[") || s.TrimStart().StartsWith("{"))
                {
                    try
                    {
                        JToken parsed = JToken.Parse(s);
                        return TrySetShaderProperty(material, propertyName, parsed, serializer);
                    }
                    catch { }
                }
            }

            // Use the serializer to convert the JToken value first
            if (value is JArray jArray)
            {
                if (jArray.Count == 4)
                {
                    if (material.HasProperty(propertyName))
                    {
                        try { material.SetColor(propertyName, ParseColor(value, serializer)); return true; }
                        catch (Exception ex)
                        {
                            // Log at Debug level since we'll try other conversions
                            McpLog.Info($"[MaterialOps] SetColor attempt for '{propertyName}' failed: {ex.Message}");
                        }

                        try { Vector4 vec = value.ToObject<Vector4>(serializer); material.SetVector(propertyName, vec); return true; }
                        catch (Exception ex)
                        {
                            McpLog.Info($"[MaterialOps] SetVector (Vec4) attempt for '{propertyName}' failed: {ex.Message}");
                        }
                    }
                }
                else if (jArray.Count == 3)
                {
                    if (material.HasProperty(propertyName))
                    {
                        try { material.SetColor(propertyName, ParseColor(value, serializer)); return true; }
                        catch (Exception ex)
                        {
                            McpLog.Info($"[MaterialOps] SetColor (Vec3) attempt for '{propertyName}' failed: {ex.Message}");
                        }
                    }
                }
                else if (jArray.Count == 2)
                {
                    if (material.HasProperty(propertyName))
                    {
                        try { Vector2 vec = value.ToObject<Vector2>(serializer); material.SetVector(propertyName, vec); return true; }
                        catch (Exception ex)
                        {
                            McpLog.Info($"[MaterialOps] SetVector (Vec2) attempt for '{propertyName}' failed: {ex.Message}");
                        }
                    }
                }
            }
            else if (value.Type == JTokenType.Float || value.Type == JTokenType.Integer)
            {
                if (!material.HasProperty(propertyName))
                    return false;

                try { material.SetFloat(propertyName, value.ToObject<float>(serializer)); return true; }
                catch (Exception ex)
                {
                    McpLog.Info($"[MaterialOps] SetFloat attempt for '{propertyName}' failed: {ex.Message}");
                }
            }
            else if (value.Type == JTokenType.Boolean)
            {
                if (!material.HasProperty(propertyName))
                    return false;

                try { material.SetFloat(propertyName, value.ToObject<bool>(serializer) ? 1f : 0f); return true; }
                catch (Exception ex)
                {
                    McpLog.Info($"[MaterialOps] SetFloat (bool) attempt for '{propertyName}' failed: {ex.Message}");
                }
            }
            else if (value.Type == JTokenType.String)
            {
                try
                {
                    // Try loading as asset path first (most common case for strings in this context)
                    string path = value.ToString();
                    if (!string.IsNullOrEmpty(path) && path.Contains("/")) // Heuristic: paths usually have slashes
                    {
                        // We need to handle texture assignment here. 
                        // Since we don't have easy access to AssetDatabase here directly without using UnityEditor namespace (which is imported),
                        // we can try to load it.
                        var sanitizedPath = AssetPathUtility.SanitizeAssetPath(path);
                        Texture tex = AssetDatabase.LoadAssetAtPath<Texture>(sanitizedPath);
                        if (tex != null && material.HasProperty(propertyName))
                        {
                            material.SetTexture(propertyName, tex);
                            return true;
                        }
                    }
                }
                catch (Exception ex)
                {
                    McpLog.Warn($"SetTexture (string path) for '{propertyName}' failed: {ex.Message}");
                }
            }

            if (value.Type == JTokenType.Object)
            {
                try
                {
                    Texture texture = value.ToObject<Texture>(serializer);
                    if (texture != null && material.HasProperty(propertyName))
                    {
                        material.SetTexture(propertyName, texture);
                        return true;
                    }
                }
                catch (Exception ex)
                {
                    McpLog.Warn($"SetTexture (object) for '{propertyName}' failed: {ex.Message}");
                }
            }

            McpLog.Warn(
                $"[MaterialOps] Unsupported or failed conversion for material property '{propertyName}' from value: {value.ToString(Formatting.None)}"
            );
            return false;
        }

        /// <summary>
        /// Helper to parse color from JToken (array or object).
        /// </summary>
        public static Color ParseColor(JToken token, JsonSerializer serializer)
        {
            if (token.Type == JTokenType.String)
            {
                string s = token.ToString();
                if (s.TrimStart().StartsWith("[") || s.TrimStart().StartsWith("{"))
                {
                    try
                    {
                        return ParseColor(JToken.Parse(s), serializer);
                    }
                    catch { }
                }
            }

            if (token is JArray jArray)
            {
                if (jArray.Count == 4)
                {
                    return new Color(
                        (float)jArray[0],
                        (float)jArray[1],
                        (float)jArray[2],
                        (float)jArray[3]
                    );
                }
                else if (jArray.Count == 3)
                {
                    return new Color(
                        (float)jArray[0],
                        (float)jArray[1],
                        (float)jArray[2],
                        1f
                    );
                }
                else
                {
                    throw new ArgumentException("Color array must have 3 or 4 elements.");
                }
            }

            try
            {
                return token.ToObject<Color>(serializer);
            }
            catch (Exception ex)
            {
                McpLog.Warn($"[MaterialOps] Failed to parse color from token: {ex.Message}");
                throw;
            }
        }
    }
}
