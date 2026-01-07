using System;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace MCPForUnity.Editor.Helpers
{
    /// <summary>
    /// Utility class for parsing JSON tokens into Unity vector and math types.
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

