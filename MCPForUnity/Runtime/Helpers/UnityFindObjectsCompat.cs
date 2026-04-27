using System;
using UObject = UnityEngine.Object;

namespace MCPForUnity.Runtime.Helpers
{
    /// <summary>
    /// Version-compatible wrappers for the Object.FindObjectsOfType / FindObjectsByType family.
    /// Unity 2022.2 editor versions do not reliably expose the newer APIs, so we only
    /// switch to them once 2022.3+ symbols are available.
    /// </summary>
    public static class UnityFindObjectsCompat
    {
        /// <summary>Find all active objects of type T.</summary>
        public static T[] FindAll<T>() where T : UObject
        {
#if UNITY_6000_5_OR_NEWER
            return UObject.FindObjectsByType<T>();
#elif UNITY_2022_3_OR_NEWER
            return UObject.FindObjectsByType<T>(UnityEngine.FindObjectsSortMode.None);
#else
#pragma warning disable 618
            return UObject.FindObjectsOfType<T>();
#pragma warning restore 618
#endif
        }

        /// <summary>Find all active objects of the given runtime type.</summary>
        public static UObject[] FindAll(Type type)
        {
#if UNITY_6000_5_OR_NEWER
            return UObject.FindObjectsByType(type, UnityEngine.FindObjectsInactive.Exclude);
#elif UNITY_2022_3_OR_NEWER
            return UObject.FindObjectsByType(type, UnityEngine.FindObjectsSortMode.None);
#else
#pragma warning disable 618
            return UObject.FindObjectsOfType(type);
#pragma warning restore 618
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
#pragma warning disable 618
            return UObject.FindObjectsOfType(type, includeInactive);
#pragma warning restore 618
#endif
        }

        /// <summary>Find any single object of the given runtime type (no ordering guarantee).</summary>
        public static UObject FindAny(Type type)
        {
#if UNITY_2022_3_OR_NEWER
            return UObject.FindAnyObjectByType(type);
#else
#pragma warning disable 618
            return UObject.FindObjectOfType(type);
#pragma warning restore 618
#endif
        }
    }
}
