using Newtonsoft.Json.Linq;
using UnityEngine;

#if UNITY_VFX_GRAPH
using UnityEngine.VFX;
#endif

namespace MCPForUnity.Editor.Tools.Vfx
{
    /// <summary>
    /// Read operations for VFX Graph (VisualEffect component).
    /// Requires com.unity.visualeffectgraph package and UNITY_VFX_GRAPH symbol.
    /// </summary>
    internal static class VfxGraphRead
    {
#if !UNITY_VFX_GRAPH
        public static object GetInfo(JObject @params)
        {
            return new { success = false, message = "VFX Graph package (com.unity.visualeffectgraph) not installed" };
        }
#else
        public static object GetInfo(JObject @params)
        {
            VisualEffect vfx = VfxGraphCommon.FindVisualEffect(@params);
            if (vfx == null)
            {
                return new { success = false, message = "VisualEffect not found" };
            }

            return new
            {
                success = true,
                data = new
                {
                    gameObject = vfx.gameObject.name,
                    assetName = vfx.visualEffectAsset?.name ?? "None",
                    aliveParticleCount = vfx.aliveParticleCount,
                    culled = vfx.culled,
                    pause = vfx.pause,
                    playRate = vfx.playRate,
                    startSeed = vfx.startSeed
                }
            };
        }
#endif
    }
}
