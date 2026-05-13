using System;
using System.Reflection;
using UObject = UnityEngine.Object;

namespace MCPForUnity.Runtime.Helpers
{
    // Part of MCP for Unity's compat-shim family. See UnityCompatShims.cs in this
    // folder for the full list of shims, the audit policy, and the reflection pattern.
    /// <summary>
    /// Version-compatible wrappers for the Object.FindObjectsOfType / FindObjectsByType family.
    ///
    /// API timeline:
    ///   Pre-2022.3 : FindObjectsOfType / FindObjectOfType
    ///   2022.3-6.4 : FindObjectsByType(sortMode) / FindAnyObjectByType
    ///   6.5+       : FindObjectsByType() (no sort param) / FindAnyObjectByType
    ///
    /// Notes:
    ///   - Some Unity 2022.2.x editor versions did not reliably expose FindObjectsByType,
    ///     so the new APIs are gated at 2022.3+ rather than 2022.2+ (issue with 2022.2.1f1).
    ///   - On the legacy branch we dispatch to FindObjectsOfType / FindObjectOfType through
    ///     reflection rather than direct calls. This keeps the file CS0618-clean across all
    ///     SDKs we compile against and lets the package keep working if Unity ever fully
    ///     removes the legacy methods (CS0619).
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
            var arr = LegacyFindObjectsOfType(typeof(T));
            if (arr == null) return Array.Empty<T>();
            var typed = new T[arr.Length];
            for (int i = 0; i < arr.Length; i++) typed[i] = (T)arr[i];
            return typed;
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
            return LegacyFindObjectsOfType(type) ?? Array.Empty<UObject>();
#endif
        }

        /// <summary>Find all objects of the given runtime type, optionally including inactive.</summary>
        public static UObject[] FindAll(Type type, bool includeInactive)
        {
#if UNITY_6000_5_OR_NEWER
            return UObject.FindObjectsByType(type,
                includeInactive ? UnityEngine.FindObjectsInactive.Include : UnityEngine.FindObjectsInactive.Exclude);
#elif UNITY_2022_3_OR_NEWER
            return UObject.FindObjectsByType(type,
                includeInactive ? UnityEngine.FindObjectsInactive.Include : UnityEngine.FindObjectsInactive.Exclude,
                UnityEngine.FindObjectsSortMode.None);
#else
            return LegacyFindObjectsOfType(type, includeInactive) ?? Array.Empty<UObject>();
#endif
        }

        /// <summary>Find any single object of the given runtime type (no ordering guarantee).</summary>
        public static UObject FindAny(Type type)
        {
#if UNITY_2022_3_OR_NEWER
            return UObject.FindAnyObjectByType(type);
#else
            return LegacyFindObjectOfType(type);
#endif
        }

#if !UNITY_2022_3_OR_NEWER
        // ---------- legacy reflection helpers (pre-2022.3 only) ----------

        private static MethodInfo _findObjectsOfTypeByType;
        private static bool _findObjectsOfTypeByTypeProbed;

        private static MethodInfo _findObjectsOfTypeWithInactive;
        private static bool _findObjectsOfTypeWithInactiveProbed;

        private static MethodInfo _findObjectOfTypeByType;
        private static bool _findObjectOfTypeByTypeProbed;

        private static UObject[] LegacyFindObjectsOfType(Type type)
        {
            if (!_findObjectsOfTypeByTypeProbed)
            {
                _findObjectsOfTypeByTypeProbed = true;
                _findObjectsOfTypeByType = typeof(UObject).GetMethod(
                    "FindObjectsOfType",
                    BindingFlags.Public | BindingFlags.Static,
                    null,
                    new[] { typeof(Type) },
                    null);
            }
            if (_findObjectsOfTypeByType == null) return null;
            try { return (UObject[])_findObjectsOfTypeByType.Invoke(null, new object[] { type }); }
            catch { return null; }
        }

        private static UObject[] LegacyFindObjectsOfType(Type type, bool includeInactive)
        {
            if (!_findObjectsOfTypeWithInactiveProbed)
            {
                _findObjectsOfTypeWithInactiveProbed = true;
                _findObjectsOfTypeWithInactive = typeof(UObject).GetMethod(
                    "FindObjectsOfType",
                    BindingFlags.Public | BindingFlags.Static,
                    null,
                    new[] { typeof(Type), typeof(bool) },
                    null);
            }
            if (_findObjectsOfTypeWithInactive == null)
            {
                // Older Unity versions only had the (Type) overload — fall back without the flag.
                return LegacyFindObjectsOfType(type);
            }
            try { return (UObject[])_findObjectsOfTypeWithInactive.Invoke(null, new object[] { type, includeInactive }); }
            catch { return null; }
        }

        private static UObject LegacyFindObjectOfType(Type type)
        {
            if (!_findObjectOfTypeByTypeProbed)
            {
                _findObjectOfTypeByTypeProbed = true;
                _findObjectOfTypeByType = typeof(UObject).GetMethod(
                    "FindObjectOfType",
                    BindingFlags.Public | BindingFlags.Static,
                    null,
                    new[] { typeof(Type) },
                    null);
            }
            if (_findObjectOfTypeByType == null) return null;
            try { return (UObject)_findObjectOfTypeByType.Invoke(null, new object[] { type }); }
            catch { return null; }
        }
#endif
    }
}
