using UnityEngine;
#if UNITY_EDITOR
using System.Reflection;
using UnityEditor;
#endif

namespace MCPForUnity.Runtime.Helpers
{
    // Part of MCP for Unity's compat-shim family. See UnityCompatShims.cs in this
    // folder for the full list of shims, the audit policy, and the reflection pattern.
    /// <summary>
    /// Version-gated wrappers for the InstanceID ↔ EntityId migration introduced in Unity 6.5
    /// and tightened in 6.6.
    ///   Forward (Object → int): <see cref="GetInstanceIDCompat"/>
    ///   Forward (Object → string, lossless wire format): <see cref="GetInstanceIDString"/>
    ///   Reverse (int → Object, Editor-only): <see cref="InstanceIDToObjectCompat"/>
    ///   Reverse (string → Object, Editor-only): <see cref="InstanceIDFromString"/>
    /// </summary>
    public static class UnityObjectIdCompat
    {
        /// <summary>
        /// Returns a session-scoped int handle for the object. On 6.5+ truncates the
        /// EntityId's underlying ulong; lossy but stable within a session and preserves
        /// the int-shaped wire format that older consumers expect. For deserialization
        /// round-trips on 6.5+, prefer the full <c>entityID</c> field.
        /// </summary>
        public static int GetInstanceIDCompat(this Object obj)
        {
            if (obj == null)
            {
                return 0;
            }

#if UNITY_6000_5_OR_NEWER
            return (int)EntityId.ToULong(obj.GetEntityId());
#else
            return obj.GetInstanceID();
#endif
        }

        /// <summary>
        /// Returns the full object handle as a STRING for lossless JSON round-trips: on 6.5+
        /// the EntityId's underlying ulong, on older versions the raw signed int instance ID.
        /// Returns null for a null object.
        ///
        /// A string (not a number) because JSON's number type is parsed as an IEEE-754 double
        /// by some consumers (e.g. JavaScript JSON.parse), which silently collapses integers
        /// above 2^53 — and instance handles can exceed that (a 64-bit EntityId, or a negative
        /// int). The handle is an opaque identifier, never arithmetic, so a string is the
        /// correct wire form. Resolve it back with <see cref="InstanceIDFromString"/>.
        /// </summary>
        public static string GetInstanceIDString(this Object obj)
        {
            if (obj == null)
            {
                return null;
            }

#if UNITY_6000_5_OR_NEWER
            return EntityId.ToULong(obj.GetEntityId()).ToString();
#else
            return obj.GetInstanceID().ToString();
#endif
        }

#if UNITY_EDITOR
#if UNITY_6000_6_OR_NEWER
        private static MethodInfo _instanceIdToObject;
        private static bool _instanceIdToObjectInitialized;
#endif

        /// <summary>
        /// Resolves an int instance ID handle back to a UnityEngine.Object.
        ///   Pre-6.0  : EditorUtility.InstanceIDToObject(int)
        ///   6.0–6.5  : EditorUtility.EntityIdToObject(int)  (implicit int→EntityId cast)
        ///   6.6+     : reflection on InstanceIDToObject(int) — the API still exists at runtime
        ///              but is obsolete-as-error; reflection bypasses CS0619 until the public
        ///              EntityId(int) ctor stabilizes.
        /// </summary>
        public static Object InstanceIDToObjectCompat(int instanceId)
        {
#if UNITY_6000_6_OR_NEWER
            if (!_instanceIdToObjectInitialized)
            {
                _instanceIdToObject = typeof(EditorUtility).GetMethod(
                    "InstanceIDToObject",
                    BindingFlags.Public | BindingFlags.Static,
                    null,
                    new[] { typeof(int) },
                    null);
                _instanceIdToObjectInitialized = true;
            }
            return _instanceIdToObject?.Invoke(null, new object[] { instanceId }) as Object;
#elif UNITY_6000_3_OR_NEWER
            return EditorUtility.EntityIdToObject(instanceId);
#else
            return EditorUtility.InstanceIDToObject(instanceId);
#endif
        }

        /// <summary>
        /// Resolves a string handle (from <see cref="GetInstanceIDString"/>) back to a
        /// UnityEngine.Object. Disambiguates by Unity version, not by inspecting the value.
        ///   6.5+    : the handle is the EntityId's ulong — parse ulong, resolve via EntityId.FromULong.
        ///   Pre-6.5 : the handle is a signed int instance ID — parse int, use the int resolver.
        /// Returns null if the string is null/empty or does not parse.
        /// </summary>
        public static Object InstanceIDFromString(string instanceId)
        {
            if (string.IsNullOrEmpty(instanceId))
            {
                return null;
            }

#if UNITY_6000_5_OR_NEWER
            if (!ulong.TryParse(instanceId, out var entityId))
                return null;
            return EditorUtility.EntityIdToObject(EntityId.FromULong(entityId));
#else
            if (!int.TryParse(instanceId, out var id))
                return null;
            return InstanceIDToObjectCompat(id);
#endif
        }
#endif
    }
}
