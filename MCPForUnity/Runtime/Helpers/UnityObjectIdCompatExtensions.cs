using UnityEngine;

namespace MCPForUnity.Runtime.Helpers
{
    /// <summary>
    /// Version-gated wrapper for <see cref="Object.GetInstanceID"/>, which was removed in Unity 6.5
    /// in favor of <see cref="Object.GetEntityId"/>. Returns a session-scoped int handle that
    /// callers can use for in-process comparisons and wire-format compatibility with older readers.
    /// For deserialization round-trips on Unity 6.5+, prefer the full <c>entityID</c> field.
    /// </summary>
    public static class UnityObjectIdCompatExtensions
    {
        public static int GetInstanceIDCompat(this Object obj)
        {
            if (obj == null)
            {
                return 0;
            }

#if UNITY_6000_5_OR_NEWER
            // GetInstanceID() is obsolete-as-error on Unity 6.5+. Truncate the EntityId's
            // underlying ulong to int; this is lossy but stable within a session and
            // preserves the int-shaped wire format that older consumers expect.
            return (int)EntityId.ToULong(obj.GetEntityId());
#else
            return obj.GetInstanceID();
#endif
        }
    }
}
