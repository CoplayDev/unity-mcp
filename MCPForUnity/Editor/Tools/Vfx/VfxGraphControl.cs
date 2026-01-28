using Newtonsoft.Json.Linq;
using UnityEditor;

#if UNITY_VFX_GRAPH
using UnityEngine.VFX;
#endif

namespace MCPForUnity.Editor.Tools.Vfx
{
    /// <summary>
    /// Playback control operations for VFX Graph (VisualEffect component).
    /// Requires com.unity.visualeffectgraph package and UNITY_VFX_GRAPH symbol.
    /// </summary>
    internal static class VfxGraphControl
    {
#if !UNITY_VFX_GRAPH
        public static object Control(JObject @params, string action)
        {
            return new { success = false, message = "VFX Graph package (com.unity.visualeffectgraph) not installed" };
        }

        public static object SetPlaybackSpeed(JObject @params)
        {
            return new { success = false, message = "VFX Graph package (com.unity.visualeffectgraph) not installed" };
        }

        public static object SetSeed(JObject @params)
        {
            return new { success = false, message = "VFX Graph package (com.unity.visualeffectgraph) not installed" };
        }
#else
        public static object Control(JObject @params, string action)
        {
            VisualEffect vfx = VfxGraphCommon.FindVisualEffect(@params);
            if (vfx == null)
            {
                return new { success = false, message = "VisualEffect not found" };
            }

            switch (action)
            {
                case "play": vfx.Play(); break;
                case "stop": vfx.Stop(); break;
                case "pause": vfx.pause = !vfx.pause; break;
                case "reinit": vfx.Reinit(); break;
                default:
                    return new { success = false, message = $"Unknown VFX action: {action}" };
            }

            return new { success = true, message = $"VFX {action}", isPaused = vfx.pause };
        }

        public static object SetPlaybackSpeed(JObject @params)
        {
            VisualEffect vfx = VfxGraphCommon.FindVisualEffect(@params);
            if (vfx == null)
            {
                return new { success = false, message = "VisualEffect not found" };
            }

            float rate = @params["playRate"]?.ToObject<float>() ?? 1f;
            Undo.RecordObject(vfx, "Set VFX Play Rate");
            vfx.playRate = rate;
            EditorUtility.SetDirty(vfx);

            return new { success = true, message = $"Set play rate = {rate}" };
        }

        public static object SetSeed(JObject @params)
        {
            VisualEffect vfx = VfxGraphCommon.FindVisualEffect(@params);
            if (vfx == null)
            {
                return new { success = false, message = "VisualEffect not found" };
            }

            uint seed = @params["seed"]?.ToObject<uint>() ?? 0;
            bool resetOnPlay = @params["resetSeedOnPlay"]?.ToObject<bool>() ?? true;

            Undo.RecordObject(vfx, "Set VFX Seed");
            vfx.startSeed = seed;
            vfx.resetSeedOnPlay = resetOnPlay;
            EditorUtility.SetDirty(vfx);

            return new { success = true, message = $"Set seed = {seed}" };
        }
#endif
    }
}
