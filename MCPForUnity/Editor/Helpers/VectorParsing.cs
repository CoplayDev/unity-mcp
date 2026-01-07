using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace MCPForUnity.Editor.Helpers
{
    /// <summary>
    /// Utility class for parsing JSON tokens into Unity vector, math, and animation types.
    /// Supports both array format [x, y, z] and object format {x: 1, y: 2, z: 3}.
   /// </summary>
    public static class VectorParsing
    {
        /// <summary>
        /// Parses a JToken (array or object) into a Vector3.
        /// </summary>
        /// <param name="token">The JSON token to parse</param>
        /// <returns>The parsed Vector3 or null if parsing fails</returns>
        public static Vector3? ParseVector3(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null)
                return null;

            try
            {
                // Array format: [x, y, z]
                if (token is JArray array && array.Count >= 3)
                {
                    return new Vector3(
                        array[0].ToObject<float>(),
                        array[1].ToObject<float>(),
                        array[2].ToObject<float>()
                    );
                }

                // Object format: {x: 1, y: 2, z: 3}
                if (token is JObject obj && obj.ContainsKey("x") && obj.ContainsKey("y") && obj.ContainsKey("z"))
                {
                    return new Vector3(
                        obj["x"].ToObject<float>(),
                        obj["y"].ToObject<float>(),
                        obj["z"].ToObject<float>()
                    );
                }
            }
            catch (Exception ex)
            {
                McpLog.Warn($"[VectorParsing] Failed to parse Vector3 from '{token}': {ex.Message}");
            }

            return null;
        }

        /// <summary>
        /// Parses a JToken into a Vector3, returning a default value if parsing fails.
        /// </summary>
        public static Vector3 ParseVector3OrDefault(JToken token, Vector3 defaultValue = default)
        {
            return ParseVector3(token) ?? defaultValue;
        }

        /// <summary>
        /// Parses a JToken (array or object) into a Vector2.
        /// </summary>
        /// <param name="token">The JSON token to parse</param>
        /// <returns>The parsed Vector2 or null if parsing fails</returns>
        public static Vector2? ParseVector2(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null)
                return null;

            try
            {
                // Array format: [x, y]
                if (token is JArray array && array.Count >= 2)
                {
                    return new Vector2(
                        array[0].ToObject<float>(),
                        array[1].ToObject<float>()
                    );
                }

                // Object format: {x: 1, y: 2}
                if (token is JObject obj && obj.ContainsKey("x") && obj.ContainsKey("y"))
                {
                    return new Vector2(
                        obj["x"].ToObject<float>(),
                        obj["y"].ToObject<float>()
                    );
                }
            }
            catch (Exception ex)
            {
                McpLog.Warn($"[VectorParsing] Failed to parse Vector2 from '{token}': {ex.Message}");
            }

            return null;
        }

        /// <summary>
        /// Parses a JToken (array or object) into a Vector4.
        /// </summary>
        /// <param name="token">The JSON token to parse</param>
        /// <returns>The parsed Vector4 or null if parsing fails</returns>
        public static Vector4? ParseVector4(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null)
                return null;

            try
            {
                // Array format: [x, y, z, w]
                if (token is JArray array && array.Count >= 4)
                {
                    return new Vector4(
                        array[0].ToObject<float>(),
                        array[1].ToObject<float>(),
                        array[2].ToObject<float>(),
                        array[3].ToObject<float>()
                    );
                }

                // Object format: {x: 1, y: 2, z: 3, w: 4}
                if (token is JObject obj && obj.ContainsKey("x") && obj.ContainsKey("y") && 
                    obj.ContainsKey("z") && obj.ContainsKey("w"))
                {
                    return new Vector4(
                        obj["x"].ToObject<float>(),
                        obj["y"].ToObject<float>(),
                        obj["z"].ToObject<float>(),
                        obj["w"].ToObject<float>()
                    );
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[VectorParsing] Failed to parse Vector4 from '{token}': {ex.Message}");
            }

            return null;
        }

        /// <summary>
        /// Parses a JToken (array or object) into a Quaternion.
        /// Supports both euler angles [x, y, z] and quaternion components [x, y, z, w].
        /// Note: Raw quaternion components are NOT normalized. Callers should normalize if needed
        /// for operations like interpolation where non-unit quaternions cause issues.
        /// </summary>
        /// <param name="token">The JSON token to parse</param>
        /// <param name="asEulerAngles">If true, treats 3-element arrays as euler angles</param>
        /// <returns>The parsed Quaternion or null if parsing fails</returns>
        public static Quaternion? ParseQuaternion(JToken token, bool asEulerAngles = true)
        {
            if (token == null || token.Type == JTokenType.Null)
                return null;

            try
            {
                if (token is JArray array)
                {
                    // Quaternion components: [x, y, z, w]
                    if (array.Count >= 4)
                    {
                        return new Quaternion(
                            array[0].ToObject<float>(),
                            array[1].ToObject<float>(),
                            array[2].ToObject<float>(),
                            array[3].ToObject<float>()
                        );
                    }

                    // Euler angles: [x, y, z]
                    if (array.Count >= 3 && asEulerAngles)
                    {
                        return Quaternion.Euler(
                            array[0].ToObject<float>(),
                            array[1].ToObject<float>(),
                            array[2].ToObject<float>()
                        );
                    }
                }

                // Object format: {x: 0, y: 0, z: 0, w: 1}
                if (token is JObject obj)
                {
                    if (obj.ContainsKey("x") && obj.ContainsKey("y") && obj.ContainsKey("z") && obj.ContainsKey("w"))
                    {
                        return new Quaternion(
                            obj["x"].ToObject<float>(),
                            obj["y"].ToObject<float>(),
                            obj["z"].ToObject<float>(),
                            obj["w"].ToObject<float>()
                        );
                    }

                    // Euler format in object: {x: 45, y: 90, z: 0} (as euler angles)
                    if (obj.ContainsKey("x") && obj.ContainsKey("y") && obj.ContainsKey("z") && asEulerAngles)
                    {
                        return Quaternion.Euler(
                            obj["x"].ToObject<float>(),
                            obj["y"].ToObject<float>(),
                            obj["z"].ToObject<float>()
                        );
                    }
                }
            }
            catch (Exception ex)
            {
                McpLog.Warn($"[VectorParsing] Failed to parse Quaternion from '{token}': {ex.Message}");
            }

            return null;
        }

        /// <summary>
        /// Parses a JToken (array or object) into a Color.
        /// Supports both [r, g, b, a] and {r: 1, g: 1, b: 1, a: 1} formats.
        /// </summary>
        /// <param name="token">The JSON token to parse</param>
        /// <returns>The parsed Color or null if parsing fails</returns>
        public static Color? ParseColor(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null)
                return null;

            try
            {
                // Array format: [r, g, b, a] or [r, g, b]
                if (token is JArray array)
                {
                    if (array.Count >= 4)
                    {
                        return new Color(
                            array[0].ToObject<float>(),
                            array[1].ToObject<float>(),
                            array[2].ToObject<float>(),
                            array[3].ToObject<float>()
                        );
                    }
                    if (array.Count >= 3)
                    {
                        return new Color(
                            array[0].ToObject<float>(),
                            array[1].ToObject<float>(),
                            array[2].ToObject<float>(),
                            1f // Default alpha
                        );
                    }
                }

                // Object format: {r: 1, g: 1, b: 1, a: 1}
                if (token is JObject obj && obj.ContainsKey("r") && obj.ContainsKey("g") && obj.ContainsKey("b"))
                {
                    float a = obj.ContainsKey("a") ? obj["a"].ToObject<float>() : 1f;
                    return new Color(
                        obj["r"].ToObject<float>(),
                        obj["g"].ToObject<float>(),
                        obj["b"].ToObject<float>(),
                        a
                    );
                }
            }
            catch (Exception ex)
            {
                McpLog.Warn($"[VectorParsing] Failed to parse Color from '{token}': {ex.Message}");
            }

            return null;
        }

        /// <summary>
        /// Parses a JToken into a Color, returning Color.white if parsing fails and no default is specified.
        /// </summary>
        public static Color ParseColorOrDefault(JToken token) => ParseColor(token) ?? Color.white;
        
        /// <summary>
        /// Parses a JToken into a Color, returning the specified default if parsing fails.
        /// </summary>
        public static Color ParseColorOrDefault(JToken token, Color defaultValue) => ParseColor(token) ?? defaultValue;

        /// <summary>
        /// Parses a JToken into a Vector4, returning a default value if parsing fails.
        /// Added for ManageVFX refactoring.
        /// </summary>
        public static Vector4 ParseVector4OrDefault(JToken token, Vector4 defaultValue = default)
        {
            return ParseVector4(token) ?? defaultValue;
        }

        /// <summary>
        /// Parses a JToken into a Gradient.
        /// Supports formats:
        /// - Simple: {startColor: [r,g,b,a], endColor: [r,g,b,a]}
        /// - Full: {colorKeys: [{color: [r,g,b,a], time: 0.0}, ...], alphaKeys: [{alpha: 1.0, time: 0.0}, ...]}
        /// Added for ManageVFX refactoring.
        /// </summary>
        /// <param name="token">The JSON token to parse</param>
        /// <returns>The parsed Gradient or null if parsing fails</returns>
        public static Gradient ParseGradient(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null)
                return null;

            try
            {
                Gradient gradient = new Gradient();

                if (token is JObject obj)
                {
                    // Simple format: {startColor: ..., endColor: ...}
                    if (obj.ContainsKey("startColor"))
                    {
                        Color startColor = ParseColorOrDefault(obj["startColor"]);
                        Color endColor = ParseColorOrDefault(obj["endColor"] ?? obj["startColor"]);
                        float startAlpha = obj["startAlpha"]?.ToObject<float>() ?? startColor.a;
                        float endAlpha = obj["endAlpha"]?.ToObject<float>() ?? endColor.a;
                        
                        gradient.SetKeys(
                            new GradientColorKey[] { new GradientColorKey(startColor, 0f), new GradientColorKey(endColor, 1f) },
                            new GradientAlphaKey[] { new GradientAlphaKey(startAlpha, 0f), new GradientAlphaKey(endAlpha, 1f) }
                        );
                        return gradient;
                    }

                    // Full format: {colorKeys: [...], alphaKeys: [...]}
                    var colorKeys = new List<GradientColorKey>();
                    var alphaKeys = new List<GradientAlphaKey>();

                    if (obj["colorKeys"] is JArray colorKeysArr)
                    {
                        foreach (var key in colorKeysArr)
                        {
                            Color color = ParseColorOrDefault(key["color"]);
                            float time = key["time"]?.ToObject<float>() ?? 0f;
                            colorKeys.Add(new GradientColorKey(color, time));
                        }
                    }

                    if (obj["alphaKeys"] is JArray alphaKeysArr)
                    {
                        foreach (var key in alphaKeysArr)
                        {
                            float alpha = key["alpha"]?.ToObject<float>() ?? 1f;
                            float time = key["time"]?.ToObject<float>() ?? 0f;
                            alphaKeys.Add(new GradientAlphaKey(alpha, time));
                        }
                    }

                    // Ensure at least 2 keys
                    if (colorKeys.Count == 0)
                    {
                        colorKeys.Add(new GradientColorKey(Color.white, 0f));
                        colorKeys.Add(new GradientColorKey(Color.white, 1f));
                    }

                    if (alphaKeys.Count == 0)
                    {
                        alphaKeys.Add(new GradientAlphaKey(1f, 0f));
                        alphaKeys.Add(new GradientAlphaKey(1f, 1f));
                    }

                    gradient.SetKeys(colorKeys.ToArray(), alphaKeys.ToArray());
                    return gradient;
                }
            }
            catch (Exception ex)
            {
                McpLog.Warn($"[VectorParsing] Failed to parse Gradient from '{token}': {ex.Message}");
            }

            return null;
        }

        /// <summary>
        /// Parses a JToken into a Gradient, returning a default gradient if parsing fails.
        /// Added for ManageVFX refactoring.
        /// </summary>
        public static Gradient ParseGradientOrDefault(JToken token)
        {
            var result = ParseGradient(token);
            if (result != null) return result;

            // Return default white gradient
            var gradient = new Gradient();
            gradient.SetKeys(
                new GradientColorKey[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new GradientAlphaKey[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) }
            );
            return gradient;
        }

        /// <summary>
        /// Parses a JToken into an AnimationCurve.
        /// 
        /// <para><b>Supported formats:</b></para>
        /// <list type="bullet">
        ///   <item>Constant: <c>1.0</c> (number) - Creates constant curve at that value</item>
        ///   <item>Simple: <c>{start: 0.0, end: 1.0}</c> or <c>{startValue: 0.0, endValue: 1.0}</c></item>
        ///   <item>Full: <c>{keys: [{time: 0, value: 1, inTangent: 0, outTangent: 0}, ...]}</c></item>
        /// </list>
        /// 
        /// <para><b>Keyframe field defaults (for Full format):</b></para>
        /// <list type="bullet">
        ///   <item><c>time</c> (float): <b>Default: 0</b></item>
        ///   <item><c>value</c> (float): <b>Default: 1</b> (note: differs from ManageScriptableObject which uses 0)</item>
        ///   <item><c>inTangent</c> (float): <b>Default: 0</b></item>
        ///   <item><c>outTangent</c> (float): <b>Default: 0</b></item>
        /// </list>
        /// 
        /// <para><b>Note:</b> This method is used by ManageVFX. For ScriptableObject patching,
        /// see <see cref="MCPForUnity.Editor.Tools.ManageScriptableObject"/> which has slightly different defaults.</para>
        /// </summary>
        /// <param name="token">The JSON token to parse</param>
        /// <returns>The parsed AnimationCurve or null if parsing fails</returns>
        public static AnimationCurve ParseAnimationCurve(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null)
                return null;

            try
            {
                // Constant value: just a number
                if (token.Type == JTokenType.Float || token.Type == JTokenType.Integer)
                {
                    return AnimationCurve.Constant(0f, 1f, token.ToObject<float>());
                }

                if (token is JObject obj)
                {
                    // Full format: {keys: [...]}
                    if (obj["keys"] is JArray keys)
                    {
                        AnimationCurve curve = new AnimationCurve();
                        foreach (var key in keys)
                        {
                            float time = key["time"]?.ToObject<float>() ?? 0f;
                            float value = key["value"]?.ToObject<float>() ?? 1f;
                            float inTangent = key["inTangent"]?.ToObject<float>() ?? 0f;
                            float outTangent = key["outTangent"]?.ToObject<float>() ?? 0f;
                            curve.AddKey(new Keyframe(time, value, inTangent, outTangent));
                        }
                        return curve;
                    }

                    // Simple format: {start: 0.0, end: 1.0} or {startValue: 0.0, endValue: 1.0}
                    if (obj.ContainsKey("start") || obj.ContainsKey("startValue") || obj.ContainsKey("end") || obj.ContainsKey("endValue"))
                    {
                        float startValue = obj["start"]?.ToObject<float>() ?? obj["startValue"]?.ToObject<float>() ?? 1f;
                        float endValue = obj["end"]?.ToObject<float>() ?? obj["endValue"]?.ToObject<float>() ?? 1f;
                        AnimationCurve curve = new AnimationCurve();
                        curve.AddKey(0f, startValue);
                        curve.AddKey(1f, endValue);
                        return curve;
                    }
                }
            }
            catch (Exception ex)
            {
                McpLog.Warn($"[VectorParsing] Failed to parse AnimationCurve from '{token}': {ex.Message}");
            }

            return null;
        }

        /// <summary>
        /// Parses a JToken into an AnimationCurve, returning a constant curve if parsing fails.
        /// Added for ManageVFX refactoring.
        /// </summary>
        /// <param name="token">The JSON token to parse</param>
        /// <param name="defaultValue">The constant value for the default curve</param>
        public static AnimationCurve ParseAnimationCurveOrDefault(JToken token, float defaultValue = 1f)
        {
            return ParseAnimationCurve(token) ?? AnimationCurve.Constant(0f, 1f, defaultValue);
        }
        
        /// <summary>
        /// Validates AnimationCurve JSON format without parsing it.
        /// Used by dry-run validation to provide early feedback on format errors.
        /// 
        /// <para><b>Validated formats:</b></para>
        /// <list type="bullet">
        ///   <item>Wrapped: <c>{ "keys": [ { "time": 0, "value": 1.0 }, ... ] }</c></item>
        ///   <item>Direct array: <c>[ { "time": 0, "value": 1.0 }, ... ]</c></item>
        ///   <item>Null/empty: Valid (will set empty curve)</item>
        /// </list>
        /// </summary>
        /// <param name="valueToken">The JSON value to validate</param>
        /// <param name="message">Output message describing validation result or error</param>
        /// <returns>True if format is valid, false otherwise</returns>
        public static bool ValidateAnimationCurveFormat(JToken valueToken, out string message)
        {
            message = null;
            
            if (valueToken == null || valueToken.Type == JTokenType.Null)
            {
                message = "Value format valid (will set empty curve).";
                return true;
            }
            
            JArray keysArray = null;
            
            if (valueToken is JObject curveObj)
            {
                keysArray = curveObj["keys"] as JArray;
                if (keysArray == null)
                {
                    message = "AnimationCurve object requires 'keys' array. Expected: { \"keys\": [ { \"time\": 0, \"value\": 0 }, ... ] }";
                    return false;
                }
            }
            else if (valueToken is JArray directArray)
            {
                keysArray = directArray;
            }
            else
            {
                message = "AnimationCurve requires object with 'keys' or array of keyframes. " +
                          "Expected: { \"keys\": [ { \"time\": 0, \"value\": 0, \"inSlope\": 0, \"outSlope\": 0 }, ... ] }";
                return false;
            }
            
            // Validate each keyframe
            for (int i = 0; i < keysArray.Count; i++)
            {
                var keyToken = keysArray[i];
                if (keyToken is not JObject keyObj)
                {
                    message = $"Keyframe at index {i} must be an object with 'time' and 'value'.";
                    return false;
                }
                
                // Validate numeric fields if present
                string[] numericFields = { "time", "value", "inSlope", "outSlope", "inTangent", "outTangent", "inWeight", "outWeight" };
                foreach (var field in numericFields)
                {
                    if (!ParamCoercion.ValidateNumericField(keyObj, field, out var fieldError))
                    {
                        message = $"Keyframe[{i}].{field}: {fieldError}";
                        return false;
                    }
                }
                
                if (!ParamCoercion.ValidateIntegerField(keyObj, "weightedMode", out var weightedModeError))
                {
                    message = $"Keyframe[{i}].weightedMode: {weightedModeError}";
                    return false;
                }
            }
            
            message = $"Value format valid (AnimationCurve with {keysArray.Count} keyframes). " +
                      "Note: Missing keyframe fields default to 0 (time, value, inSlope, outSlope, inWeight, outWeight).";
            return true;
        }
        
        /// <summary>
        /// Validates Quaternion JSON format without parsing it.
        /// Used by dry-run validation to provide early feedback on format errors.
        /// 
        /// <para><b>Validated formats:</b></para>
        /// <list type="bullet">
        ///   <item>Euler array: <c>[x, y, z]</c> - 3 numeric elements</item>
        ///   <item>Raw quaternion: <c>[x, y, z, w]</c> - 4 numeric elements</item>
        ///   <item>Object: <c>{ "x": 0, "y": 0, "z": 0, "w": 1 }</c></item>
        ///   <item>Explicit euler: <c>{ "euler": [x, y, z] }</c></item>
        ///   <item>Null/empty: Valid (will set identity)</item>
        /// </list>
        /// </summary>
        /// <param name="valueToken">The JSON value to validate</param>
        /// <param name="message">Output message describing validation result or error</param>
        /// <returns>True if format is valid, false otherwise</returns>
        public static bool ValidateQuaternionFormat(JToken valueToken, out string message)
        {
            message = null;
            
            if (valueToken == null || valueToken.Type == JTokenType.Null)
            {
                message = "Value format valid (will set identity quaternion).";
                return true;
            }
            
            if (valueToken is JArray arr)
            {
                if (arr.Count == 3)
                {
                    // Validate Euler angles [x, y, z]
                    for (int i = 0; i < 3; i++)
                    {
                        if (!ParamCoercion.IsNumericToken(arr[i]))
                        {
                            message = $"Euler angle at index {i} must be a number.";
                            return false;
                        }
                    }
                    message = "Value format valid (Quaternion from Euler angles [x, y, z]).";
                    return true;
                }
                else if (arr.Count == 4)
                {
                    // Validate raw quaternion [x, y, z, w]
                    for (int i = 0; i < 4; i++)
                    {
                        if (!ParamCoercion.IsNumericToken(arr[i]))
                        {
                            message = $"Quaternion component at index {i} must be a number.";
                            return false;
                        }
                    }
                    message = "Value format valid (Quaternion from [x, y, z, w]).";
                    return true;
                }
                else
                {
                    message = "Quaternion array must have 3 elements (Euler angles) or 4 elements (x, y, z, w).";
                    return false;
                }
            }
            else if (valueToken is JObject obj)
            {
                // Check for explicit euler property
                if (obj["euler"] is JArray eulerArr)
                {
                    if (eulerArr.Count != 3)
                    {
                        message = "Quaternion euler array must have exactly 3 elements [x, y, z].";
                        return false;
                    }
                    for (int i = 0; i < 3; i++)
                    {
                        if (!ParamCoercion.IsNumericToken(eulerArr[i]))
                        {
                            message = $"Euler angle at index {i} must be a number.";
                            return false;
                        }
                    }
                    message = "Value format valid (Quaternion from { euler: [x, y, z] }).";
                    return true;
                }
                
                // Object format { x, y, z, w }
                if (obj["x"] != null && obj["y"] != null && obj["z"] != null && obj["w"] != null)
                {
                    if (!ParamCoercion.IsNumericToken(obj["x"]) || !ParamCoercion.IsNumericToken(obj["y"]) || 
                        !ParamCoercion.IsNumericToken(obj["z"]) || !ParamCoercion.IsNumericToken(obj["w"]))
                    {
                        message = "Quaternion { x, y, z, w } fields must all be numbers.";
                        return false;
                    }
                    message = "Value format valid (Quaternion from { x, y, z, w }).";
                    return true;
                }
                
                message = "Quaternion object must have { x, y, z, w } or { euler: [x, y, z] }.";
                return false;
            }
            else
            {
                message = "Quaternion requires array [x,y,z] (Euler), [x,y,z,w] (raw), or object { x, y, z, w }.";
                return false;
            }
        }

        /// <summary>
        /// Parses a JToken into a Rect.
        /// Supports {x, y, width, height} format.
        /// </summary>
        public static Rect? ParseRect(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null)
                return null;

            try
            {
                if (token is JObject obj && 
                    obj.ContainsKey("x") && obj.ContainsKey("y") && 
                    obj.ContainsKey("width") && obj.ContainsKey("height"))
                {
                    return new Rect(
                        obj["x"].ToObject<float>(),
                        obj["y"].ToObject<float>(),
                        obj["width"].ToObject<float>(),
                        obj["height"].ToObject<float>()
                    );
                }

                // Array format: [x, y, width, height]
                if (token is JArray array && array.Count >= 4)
                {
                    return new Rect(
                        array[0].ToObject<float>(),
                        array[1].ToObject<float>(),
                        array[2].ToObject<float>(),
                        array[3].ToObject<float>()
                    );
                }
            }
            catch (Exception ex)
            {
                McpLog.Warn($"[VectorParsing] Failed to parse Rect from '{token}': {ex.Message}");
            }

            return null;
        }

        /// <summary>
        /// Parses a JToken into a Bounds.
        /// Supports {center: {x,y,z}, size: {x,y,z}} format.
        /// </summary>
        public static Bounds? ParseBounds(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null)
                return null;

            try
            {
                if (token is JObject obj && obj.ContainsKey("center") && obj.ContainsKey("size"))
                {
                    var center = ParseVector3(obj["center"]) ?? Vector3.zero;
                    var size = ParseVector3(obj["size"]) ?? Vector3.zero;
                    return new Bounds(center, size);
                }
            }
            catch (Exception ex)
            {
                McpLog.Warn($"[VectorParsing] Failed to parse Bounds from '{token}': {ex.Message}");
            }

            return null;
        }
    }
}

