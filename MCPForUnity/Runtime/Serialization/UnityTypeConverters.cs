using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor; // Required for AssetDatabase and EditorUtility
#endif

namespace MCPForUnity.Runtime.Serialization
{
    public class Vector3Converter : JsonConverter<Vector3>
    {
        public override void WriteJson(JsonWriter writer, Vector3 value, JsonSerializer serializer)
        {
            writer.WriteStartObject();
            writer.WritePropertyName("x");
            writer.WriteValue(value.x);
            writer.WritePropertyName("y");
            writer.WriteValue(value.y);
            writer.WritePropertyName("z");
            writer.WriteValue(value.z);
            writer.WriteEndObject();
        }

        public override Vector3 ReadJson(JsonReader reader, Type objectType, Vector3 existingValue, bool hasExistingValue, JsonSerializer serializer)
        {
            JObject jo = JObject.Load(reader);
            return new Vector3(
                (float)jo["x"],
                (float)jo["y"],
                (float)jo["z"]
            );
        }
    }

    public class Vector2Converter : JsonConverter<Vector2>
    {
        public override void WriteJson(JsonWriter writer, Vector2 value, JsonSerializer serializer)
        {
            writer.WriteStartObject();
            writer.WritePropertyName("x");
            writer.WriteValue(value.x);
            writer.WritePropertyName("y");
            writer.WriteValue(value.y);
            writer.WriteEndObject();
        }

        public override Vector2 ReadJson(JsonReader reader, Type objectType, Vector2 existingValue, bool hasExistingValue, JsonSerializer serializer)
        {
            JObject jo = JObject.Load(reader);
            return new Vector2(
                (float)jo["x"],
                (float)jo["y"]
            );
        }
    }

    public class QuaternionConverter : JsonConverter<Quaternion>
    {
        public override void WriteJson(JsonWriter writer, Quaternion value, JsonSerializer serializer)
        {
            writer.WriteStartObject();
            writer.WritePropertyName("x");
            writer.WriteValue(value.x);
            writer.WritePropertyName("y");
            writer.WriteValue(value.y);
            writer.WritePropertyName("z");
            writer.WriteValue(value.z);
            writer.WritePropertyName("w");
            writer.WriteValue(value.w);
            writer.WriteEndObject();
        }

        public override Quaternion ReadJson(JsonReader reader, Type objectType, Quaternion existingValue, bool hasExistingValue, JsonSerializer serializer)
        {
            JObject jo = JObject.Load(reader);
            return new Quaternion(
                (float)jo["x"],
                (float)jo["y"],
                (float)jo["z"],
                (float)jo["w"]
            );
        }
    }

    public class ColorConverter : JsonConverter<Color>
    {
        public override void WriteJson(JsonWriter writer, Color value, JsonSerializer serializer)
        {
            writer.WriteStartObject();
            writer.WritePropertyName("r");
            writer.WriteValue(value.r);
            writer.WritePropertyName("g");
            writer.WriteValue(value.g);
            writer.WritePropertyName("b");
            writer.WriteValue(value.b);
            writer.WritePropertyName("a");
            writer.WriteValue(value.a);
            writer.WriteEndObject();
        }

        public override Color ReadJson(JsonReader reader, Type objectType, Color existingValue, bool hasExistingValue, JsonSerializer serializer)
        {
            JObject jo = JObject.Load(reader);
            return new Color(
                (float)jo["r"],
                (float)jo["g"],
                (float)jo["b"],
                (float)jo["a"]
            );
        }
    }

    public class RectConverter : JsonConverter<Rect>
    {
        public override void WriteJson(JsonWriter writer, Rect value, JsonSerializer serializer)
        {
            writer.WriteStartObject();
            writer.WritePropertyName("x");
            writer.WriteValue(value.x);
            writer.WritePropertyName("y");
            writer.WriteValue(value.y);
            writer.WritePropertyName("width");
            writer.WriteValue(value.width);
            writer.WritePropertyName("height");
            writer.WriteValue(value.height);
            writer.WriteEndObject();
        }

        public override Rect ReadJson(JsonReader reader, Type objectType, Rect existingValue, bool hasExistingValue, JsonSerializer serializer)
        {
            JObject jo = JObject.Load(reader);
            return new Rect(
                (float)jo["x"],
                (float)jo["y"],
                (float)jo["width"],
                (float)jo["height"]
            );
        }
    }

    public class BoundsConverter : JsonConverter<Bounds>
    {
        public override void WriteJson(JsonWriter writer, Bounds value, JsonSerializer serializer)
        {
            writer.WriteStartObject();
            writer.WritePropertyName("center");
            serializer.Serialize(writer, value.center); // Use serializer to handle nested Vector3
            writer.WritePropertyName("size");
            serializer.Serialize(writer, value.size);   // Use serializer to handle nested Vector3
            writer.WriteEndObject();
        }

        public override Bounds ReadJson(JsonReader reader, Type objectType, Bounds existingValue, bool hasExistingValue, JsonSerializer serializer)
        {
            JObject jo = JObject.Load(reader);
            Vector3 center = jo["center"].ToObject<Vector3>(serializer); // Use serializer to handle nested Vector3
            Vector3 size = jo["size"].ToObject<Vector3>(serializer);     // Use serializer to handle nested Vector3
            return new Bounds(center, size);
        }
    }

    public class Vector4Converter : JsonConverter<Vector4>
    {
        public override void WriteJson(JsonWriter writer, Vector4 value, JsonSerializer serializer)
        {
            writer.WriteStartObject();
            writer.WritePropertyName("x");
            writer.WriteValue(value.x);
            writer.WritePropertyName("y");
            writer.WriteValue(value.y);
            writer.WritePropertyName("z");
            writer.WriteValue(value.z);
            writer.WritePropertyName("w");
            writer.WriteValue(value.w);
            writer.WriteEndObject();
        }

        public override Vector4 ReadJson(JsonReader reader, Type objectType, Vector4 existingValue, bool hasExistingValue, JsonSerializer serializer)
        {
            JObject jo = JObject.Load(reader);
            return new Vector4(
                (float)jo["x"],
                (float)jo["y"],
                (float)jo["z"],
                (float)jo["w"]
            );
        }
    }

    /// <summary>
    /// Safe converter for Matrix4x4 that only accesses raw matrix elements (m00-m33).
    /// Avoids computed properties (lossyScale, rotation, inverse) that call ValidTRS()
    /// and can crash Unity on non-TRS matrices (common in Cinemachine components).
    /// Fixes: https://github.com/CoplayDev/unity-mcp/issues/478
    /// </summary>
    public class Matrix4x4Converter : JsonConverter<Matrix4x4>
    {
        public override void WriteJson(JsonWriter writer, Matrix4x4 value, JsonSerializer serializer)
        {
            writer.WriteStartObject();
            // Only access raw matrix elements - NEVER computed properties like lossyScale/rotation
            writer.WritePropertyName("m00"); writer.WriteValue(value.m00);
            writer.WritePropertyName("m01"); writer.WriteValue(value.m01);
            writer.WritePropertyName("m02"); writer.WriteValue(value.m02);
            writer.WritePropertyName("m03"); writer.WriteValue(value.m03);
            writer.WritePropertyName("m10"); writer.WriteValue(value.m10);
            writer.WritePropertyName("m11"); writer.WriteValue(value.m11);
            writer.WritePropertyName("m12"); writer.WriteValue(value.m12);
            writer.WritePropertyName("m13"); writer.WriteValue(value.m13);
            writer.WritePropertyName("m20"); writer.WriteValue(value.m20);
            writer.WritePropertyName("m21"); writer.WriteValue(value.m21);
            writer.WritePropertyName("m22"); writer.WriteValue(value.m22);
            writer.WritePropertyName("m23"); writer.WriteValue(value.m23);
            writer.WritePropertyName("m30"); writer.WriteValue(value.m30);
            writer.WritePropertyName("m31"); writer.WriteValue(value.m31);
            writer.WritePropertyName("m32"); writer.WriteValue(value.m32);
            writer.WritePropertyName("m33"); writer.WriteValue(value.m33);
            writer.WriteEndObject();
        }

        public override Matrix4x4 ReadJson(JsonReader reader, Type objectType, Matrix4x4 existingValue, bool hasExistingValue, JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.Null)
                return new Matrix4x4(); // Return zero matrix for null (consistent with missing field defaults)

            if (reader.TokenType != JsonToken.StartObject)
                throw new JsonSerializationException($"Expected JSON object or null when deserializing Matrix4x4, got '{reader.TokenType}'.");

            JObject jo = JObject.Load(reader);
            var matrix = new Matrix4x4();
            matrix.m00 = jo["m00"]?.Value<float>() ?? 0f;
            matrix.m01 = jo["m01"]?.Value<float>() ?? 0f;
            matrix.m02 = jo["m02"]?.Value<float>() ?? 0f;
            matrix.m03 = jo["m03"]?.Value<float>() ?? 0f;
            matrix.m10 = jo["m10"]?.Value<float>() ?? 0f;
            matrix.m11 = jo["m11"]?.Value<float>() ?? 0f;
            matrix.m12 = jo["m12"]?.Value<float>() ?? 0f;
            matrix.m13 = jo["m13"]?.Value<float>() ?? 0f;
            matrix.m20 = jo["m20"]?.Value<float>() ?? 0f;
            matrix.m21 = jo["m21"]?.Value<float>() ?? 0f;
            matrix.m22 = jo["m22"]?.Value<float>() ?? 0f;
            matrix.m23 = jo["m23"]?.Value<float>() ?? 0f;
            matrix.m30 = jo["m30"]?.Value<float>() ?? 0f;
            matrix.m31 = jo["m31"]?.Value<float>() ?? 0f;
            matrix.m32 = jo["m32"]?.Value<float>() ?? 0f;
            matrix.m33 = jo["m33"]?.Value<float>() ?? 0f;
            return matrix;
        }
    }

    // Converter for UnityEngine.Object references (GameObjects, Components, Materials, Textures, etc.)
    public class UnityEngineObjectConverter : JsonConverter<UnityEngine.Object>
    {
        public override bool CanRead => true; // We need to implement ReadJson
        public override bool CanWrite => true;

        public override void WriteJson(JsonWriter writer, UnityEngine.Object value, JsonSerializer serializer)
        {
            if (value == null)
            {
                writer.WriteNull();
                return;
            }

#if UNITY_EDITOR // AssetDatabase and EditorUtility are Editor-only
            if (UnityEditor.AssetDatabase.Contains(value))
            {
                // It's an asset (Material, Texture, Prefab, etc.)
                string path = UnityEditor.AssetDatabase.GetAssetPath(value);
                if (!string.IsNullOrEmpty(path))
                {
                    writer.WriteValue(path);
                }
                else
                {
                    // Asset exists but path couldn't be found? Write minimal info.
                    writer.WriteStartObject();
                    writer.WritePropertyName("name");
                    writer.WriteValue(value.name);
                    writer.WritePropertyName("instanceID");
                    writer.WriteValue(value.GetInstanceID());
                    writer.WritePropertyName("isAssetWithoutPath");
                    writer.WriteValue(true);
                    writer.WriteEndObject();
                }
            }
            else
            {
                // It's a scene object (GameObject, Component, etc.)
                writer.WriteStartObject();
                writer.WritePropertyName("name");
                writer.WriteValue(value.name);
                writer.WritePropertyName("instanceID");
                writer.WriteValue(value.GetInstanceID());
                writer.WriteEndObject();
            }
#else
            // Runtime fallback: Write basic info without AssetDatabase
            writer.WriteStartObject();
            writer.WritePropertyName("name");
            writer.WriteValue(value.name);
            writer.WritePropertyName("instanceID");
            writer.WriteValue(value.GetInstanceID());
             writer.WritePropertyName("warning");
            writer.WriteValue("UnityEngineObjectConverter running in non-Editor mode, asset path unavailable.");
            writer.WriteEndObject();
#endif
        }

        public override UnityEngine.Object ReadJson(JsonReader reader, Type objectType, UnityEngine.Object existingValue, bool hasExistingValue, JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.Null)
            {
                return null;
            }

#if UNITY_EDITOR
            if (reader.TokenType == JsonToken.String)
            {
                string strValue = reader.Value.ToString();

                // Check if it looks like a GUID (32 hex chars, optionally with hyphens)
                if (IsValidGuid(strValue))
                {
                    string path = UnityEditor.AssetDatabase.GUIDToAssetPath(strValue.Replace("-", "").ToLowerInvariant());
                    if (!string.IsNullOrEmpty(path))
                    {
                        var asset = UnityEditor.AssetDatabase.LoadAssetAtPath(path, objectType);
                        if (asset != null) return asset;
                    }
                    UnityEngine.Debug.LogWarning($"[UnityEngineObjectConverter] Could not load asset with GUID '{strValue}' as type '{objectType.Name}'.");
                    return null;
                }

                // Assume it's an asset path
                var loadedAsset = UnityEditor.AssetDatabase.LoadAssetAtPath(strValue, objectType);
                if (loadedAsset == null)
                {
                    UnityEngine.Debug.LogWarning($"[UnityEngineObjectConverter] Could not load asset at path '{strValue}' as type '{objectType.Name}'.");
                }
                return loadedAsset;
            }

            if (reader.TokenType == JsonToken.StartObject)
            {
                JObject jo = JObject.Load(reader);

                // Try to resolve by GUID first (for assets like ScriptableObjects, Materials, etc.)
                if (jo.TryGetValue("guid", out JToken guidToken) && guidToken.Type == JTokenType.String)
                {
                    string guid = guidToken.ToString().Replace("-", "").ToLowerInvariant();
                    string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
                    if (!string.IsNullOrEmpty(path))
                    {
                        var asset = UnityEditor.AssetDatabase.LoadAssetAtPath(path, objectType);
                        if (asset != null) return asset;
                    }
                    UnityEngine.Debug.LogWarning($"[UnityEngineObjectConverter] Could not load asset with GUID '{guidToken}' as type '{objectType.Name}'.");
                    return null;
                }

                // Try to resolve by instanceID
                if (jo.TryGetValue("instanceID", out JToken idToken) && idToken.Type == JTokenType.Integer)
                {
                    int instanceId = idToken.ToObject<int>();
                    UnityEngine.Object obj = UnityEditor.EditorUtility.InstanceIDToObject(instanceId);
                    if (obj != null)
                    {
                        // Direct type match
                        if (objectType.IsAssignableFrom(obj.GetType()))
                        {
                            return obj;
                        }

                        // Special case: expecting Transform but got GameObject - get its transform
                        if (objectType == typeof(Transform) && obj is GameObject go)
                        {
                            return go.transform;
                        }

                        // Special case: expecting a Component type but got GameObject - try to get the component
                        if (typeof(Component).IsAssignableFrom(objectType) && obj is GameObject gameObj)
                        {
                            var component = gameObj.GetComponent(objectType);
                            if (component != null)
                            {
                                return component;
                            }
                            UnityEngine.Debug.LogWarning($"[UnityEngineObjectConverter] GameObject '{gameObj.name}' (ID: {instanceId}) does not have a '{objectType.Name}' component.");
                            return null;
                        }

                        // Type mismatch with no automatic conversion available
                        UnityEngine.Debug.LogWarning($"[UnityEngineObjectConverter] Instance ID {instanceId} resolved to '{obj.GetType().Name}' but expected '{objectType.Name}'.");
                        return null;
                    }
                    // Instance ID lookup failed - this can happen if the object was destroyed or ID is stale
                    string objectName = jo.TryGetValue("name", out JToken nameToken) ? nameToken.ToString() : "unknown";
                    UnityEngine.Debug.LogWarning($"[UnityEngineObjectConverter] Could not resolve instance ID {instanceId} (name: '{objectName}') to a valid {objectType.Name}. The object may have been destroyed or the ID is stale.");
                    return null;
                }

                // Check if there's an asset path in the object
                if (jo.TryGetValue("path", out JToken pathToken) && pathToken.Type == JTokenType.String)
                {
                    string path = pathToken.ToString();
                    var asset = UnityEditor.AssetDatabase.LoadAssetAtPath(path, objectType);
                    if (asset != null)
                    {
                        return asset;
                    }
                    UnityEngine.Debug.LogWarning($"[UnityEngineObjectConverter] Could not load asset at path '{path}' as type '{objectType.Name}'.");
                    return null;
                }

                // Object format not recognized
                UnityEngine.Debug.LogWarning($"[UnityEngineObjectConverter] JSON object missing 'instanceID', 'guid', or 'path' field for {objectType.Name} deserialization. Object: {jo.ToString(Formatting.None)}");
                return null;
            }

            // Unexpected token type
            UnityEngine.Debug.LogWarning($"[UnityEngineObjectConverter] Unexpected token type '{reader.TokenType}' when deserializing {objectType.Name}. Expected Null, String, or Object.");
            return null;
#else
            // Runtime deserialization is tricky without AssetDatabase/EditorUtility
            UnityEngine.Debug.LogWarning("UnityEngineObjectConverter cannot deserialize complex objects in non-Editor mode.");
            // Skip the current token to avoid breaking the reader state
            reader.Skip();
            // Return existing value since we can't deserialize without Editor APIs
            return existingValue;
#endif
        }

        /// <summary>
        /// Checks if a string looks like a valid GUID (32 hex chars, with or without hyphens).
        /// </summary>
        private static bool IsValidGuid(string str)
        {
            if (string.IsNullOrEmpty(str)) return false;
            string normalized = str.Replace("-", "");
            if (normalized.Length != 32) return false;
            foreach (char c in normalized)
            {
                if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F')))
                    return false;
            }
            return true;
        }
    }
}