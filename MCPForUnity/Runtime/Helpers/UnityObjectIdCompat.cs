using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif
#if UNITY_6000_6_OR_NEWER
using System.Collections.Generic;
#endif

namespace MCPForUnity.Runtime.Helpers
{
    // Part of MCP for Unity's compat-shim family. See UnityCompatShims.cs in this
    // folder for the full list of shims, the audit policy, and the reflection pattern.
    /// <summary>
    /// Version-gated wrappers for the InstanceID ↔ EntityId migration introduced in Unity 6.5
    /// and tightened in 6.6.
    ///   Forward (Object → int): <see cref="GetInstanceIDCompat"/>
    ///   Reverse (int → Object, Editor-only): <see cref="InstanceIDToObjectCompat"/>
    /// </summary>
    public static class UnityObjectIdCompat
    {
#if UNITY_6000_6_OR_NEWER
        // 6000.6 stubs out both EditorUtility.InstanceIDToObject(int) and EntityId's
        // implicit int->EntityId conversion (both throw NotImplementedException at
        // runtime despite existing in the API surface - verified against 6000.6.0f1).
        // The only working reverse path is EntityIdToObject(EntityId) fed a value built
        // via EntityId.FromULong(ulong), which needs the full 64-bit id, not the
        // truncated int this class hands out. So we cache the full value behind the
        // truncated int at mint time and look it up here instead of trying to
        // reconstruct it from the int alone.
        private static readonly Dictionary<int, ulong> _fullIdCache = new Dictionary<int, ulong>();
#endif

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
            ulong full = EntityId.ToULong(obj.GetEntityId());
            int truncated = (int)full;
#if UNITY_6000_6_OR_NEWER
            _fullIdCache[truncated] = full;
#endif
            return truncated;
#else
            return obj.GetInstanceID();
#endif
        }

#if UNITY_EDITOR
        /// <summary>
        /// Resolves an int instance ID handle back to a UnityEngine.Object.
        ///   Pre-6.0  : EditorUtility.InstanceIDToObject(int)
        ///   6.0–6.5  : EditorUtility.EntityIdToObject(int)  (implicit int→EntityId cast)
        ///   6.6+     : EditorUtility.EntityIdToObject(EntityId.FromULong(full)) using the
        ///              full 64-bit value cached in GetInstanceIDCompat, since the int-only
        ///              paths (including the implicit int→EntityId cast) are unimplemented
        ///              stubs on this Unity version. Only resolves ids minted this session.
        /// </summary>
        public static Object InstanceIDToObjectCompat(int instanceId)
        {
#if UNITY_6000_6_OR_NEWER
            if (_fullIdCache.TryGetValue(instanceId, out ulong full))
            {
                return EditorUtility.EntityIdToObject(EntityId.FromULong(full));
            }
            return null;
#elif UNITY_6000_3_OR_NEWER
            return EditorUtility.EntityIdToObject(instanceId);
#else
            return EditorUtility.InstanceIDToObject(instanceId);
#endif
        }
#endif
    }
}
