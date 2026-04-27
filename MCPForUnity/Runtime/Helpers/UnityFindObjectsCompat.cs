using System;
using UObject = UnityEngine.Object;

namespace MCPForUnity.Runtime.Helpers
{
    /// <summary>
    /// Version-compatible wrappers for the Object.FindObjectsOfType / FindObjectsByType family,
    /// which changed across Unity 2022 → 6.0 → 6.5:
    ///   Pre-2022.2  : FindObjectsOfType / FindObjectOfType
    ///   2022.2–6.4  : FindObjectsByType(sortMode) / FindFirstObjectByType
    ///   6.5+        : FindObjectsByType() (no sort param) / FindAnyObjectByType
    /// </summary>
    public static class UnityFindObjectsCompat
    {
        /// <summary>Find all active objects of type T.</summary>
        public static T[] FindAll<T>() where T : UObject
        {
#if UNITY_6000_5_OR_NEWER
            return UObject.FindObjectsByType<T>();
#elif UNITY_2022_2_OR_NEWER
            return UObject.FindObjectsByType<T>(UnityEngine.FindObjectsSortMode.None);
#else
            return UObject.FindObjectsOfType<T>();
#endif
        }

        /// <summary>Find all active objects of the given runtime type.</summary>
        public static UObject[] FindAll(Type type)
        {
#if UNITY_6000_5_OR_NEWER
            return UObject.FindObjectsByType(type, UnityEngine.FindObjectsInactive.Exclude);
#elif UNITY_2022_2_OR_NEWER
            return UObject.FindObjectsByType(type, UnityEngine.FindObjectsSortMode.None);
#else
            return UObject.FindObjectsOfType(type);
#endif
        }

        /// <summary>Find all objects of the given runtime type, optionally including inactive.</summary>
        public static UObject[] FindAll(Type type, bool includeInactive)
        {
#if UNITY_6000_5_OR_NEWER
            return UObject.FindObjectsByType(type,
                includeInactive ? UnityEngine.FindObjectsInactive.Include : UnityEngine.FindObjectsInactive.Exclude);
#elif UNITY_2023_1_OR_NEWER
            return UObject.FindObjectsByType(type,
                includeInactive ? UnityEngine.FindObjectsInactive.Include : UnityEngine.FindObjectsInactive.Exclude,
                UnityEngine.FindObjectsSortMode.None);
#else
            return UObject.FindObjectsOfType(type, includeInactive);
#endif
        }

        /// <summary>Find any single object of the given runtime type (no ordering guarantee).</summary>
        public static UObject FindAny(Type type)
        {
#if UNITY_2022_2_OR_NEWER
            return UObject.FindAnyObjectByType(type);
#else
            return UObject.FindObjectOfType(type);
#endif
        }
    }
}
