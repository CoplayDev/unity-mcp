using Newtonsoft.Json.Linq;
using UnityEngine;

namespace MCPForUnity.Editor.Helpers
{
    /// <summary>
    /// Helper class for Vector3 operations
    /// </summary>
    public static class Vector3Helper
    {
        /// <summary>
        /// Parses a JArray into a Vector3
        /// </summary>
        /// <param name="array">The array containing x, y, z coordinates</param>
        /// <returns>A Vector3 with the parsed coordinates</returns>
        /// <exception cref="System.Exception">Thrown when array is invalid</exception>
        public static Vector3 ParseVector3(JArray array)
        {
            if (array == null || array.Count != 3)
                throw new System.Exception("Vector3 must be an array of 3 floats [x, y, z].");
            return new Vector3((float)array[0], (float)array[1], (float)array[2]);
        }
    }
}

