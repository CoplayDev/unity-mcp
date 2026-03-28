using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using Unity.Profiling;

namespace MCPForUnity.Editor.Tools.Profiler
{
    internal static class AnimationTimingOps
    {
        internal static object GetAnimationTiming(JObject @params)
        {
            var data = new Dictionary<string, object>();

            using var recorder = ProfilerRecorder.StartNew(ProfilerCategory.Animation, "Animator.Update");
            data["animator_update_ms"] = recorder.Valid ? recorder.CurrentValue / 1e6 : 0.0;
            data["animator_update_valid"] = recorder.Valid;

            return new
            {
                success = true,
                message = "Animation timing captured.",
                data
            };
        }
    }
}
