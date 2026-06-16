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
    ///   Forward (Object → ulong, lossless): <see cref="GetInstanceIDLongCompat"/>
    ///   Reverse (int → Object, Editor-only): <see cref="InstanceIDToObjectCompat"/>
    ///   Reverse (ulong → Object, Editor-only): <see cref="InstanceIDToObjectLongCompat"/>
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
        /// Like <see cref="GetInstanceIDCompat"/> but returns the full handle without the
        /// lossy int truncation: on 6.5+ the EntityId's underlying ulong, on older versions
        /// the int instance ID widened to ulong. Returns null for a null object. Use when
        /// the handle must round-trip exactly (e.g. matching the same object back across a
        /// JSON request).
        /// </summary>
        public static ulong? GetInstanceIDLongCompat(this Object obj)
        {
            if (obj == null)
            {
                return null;
            }

#if UNITY_6000_5_OR_NEWER
            return EntityId.ToULong(obj.GetEntityId());
#else
            return unchecked((ulong)obj.GetInstanceID());
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
        /// Resolves a ulong handle (from <see cref="GetInstanceIDLongCompat"/>) back to a
        /// UnityEngine.Object. Disambiguates by Unity version, not by inspecting the numeric
        /// range — a wrapped-negative int and a genuine 64-bit EntityId can occupy the same
        /// high band, so range checks cannot tell them apart.
        ///   6.5+    : the handle is the EntityId's ulong — resolve via EntityId.FromULong.
        ///   Pre-6.5 : the handle is an int instance ID round-tripped through an unchecked
        ///             ulong cast (negatives are valid) — cast back and use the int resolver.
        /// </summary>
        public static Object InstanceIDToObjectLongCompat(ulong instanceId)
        {
#if UNITY_6000_5_OR_NEWER
            return EditorUtility.EntityIdToObject(EntityId.FromULong(instanceId));
#else
            return InstanceIDToObjectCompat(unchecked((int)instanceId));
#endif
        }
#endif
    }
}
