using System;
using UObject = UnityEngine.Object;

namespace MCPForUnity.Runtime.Helpers
{
    /// <summary>
    /// Version-compatible wrappers for the Object.FindObjectsOfType / FindObjectsByType family,
    /// which changed across Unity 2022, Unity 6.0, and Unity 6.5.
    /// </summary>
    public static class UnityFindObjectsCompat
    {
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
