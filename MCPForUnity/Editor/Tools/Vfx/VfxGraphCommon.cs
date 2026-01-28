using Newtonsoft.Json.Linq;
using UnityEngine;

#if UNITY_VFX_GRAPH
using UnityEngine.VFX;
#endif

namespace MCPForUnity.Editor.Tools.Vfx
{
    /// <summary>
    /// Common utilities for VFX Graph operations.
    /// </summary>
    internal static class VfxGraphCommon
    {
#if UNITY_VFX_GRAPH
        /// <summary>
        /// Finds a VisualEffect component on the target GameObject.
        /// </summary>
        public static VisualEffect FindVisualEffect(JObject @params)
        {
            if (@params == null)
                return null;

            GameObject go = ManageVfxCommon.FindTargetGameObject(@params);
            return go?.GetComponent<VisualEffect>();
        }
#endif
    }
}
