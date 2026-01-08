using System.Collections.Generic;
using Newtonsoft.Json;
using MCPForUnity.Runtime.Serialization;

namespace MCPForUnity.Editor.Helpers
{
    /// <summary>
    /// Shared JsonSerializer with Unity type converters.
    /// Extracted from ManageGameObject to eliminate cross-tool dependencies.
    /// </summary>
    public static class UnityJsonSerializer
    {
        /// <summary>
        /// Shared JsonSerializer instance with converters for Unity types.
        /// Use this for all JToken-to-Unity-type conversions.
        /// </summary>
        public static readonly JsonSerializer Instance = JsonSerializer.Create(new JsonSerializerSettings
        {
            Converters = new List<JsonConverter>
            {
                new Vector2Converter(),
                new Vector3Converter(),
                new Vector4Converter(),
                new QuaternionConverter(),
                new ColorConverter(),
                new RectConverter(),
                new BoundsConverter(),
                new UnityEngineObjectConverter()
            }
        });
    }
}

