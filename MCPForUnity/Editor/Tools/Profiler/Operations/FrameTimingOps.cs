using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using Unity.Profiling;

namespace MCPForUnity.Editor.Tools.Profiler
{
    internal static class FrameTimingOps
    {
        private static readonly (string counterName, string jsonKey)[] COUNTER_MAP = new[]
        {
            ("Main Thread", "main_thread_ms"),
            ("Render Thread", "render_thread_ms"),
            ("CPU Frame Time", "cpu_frame_ms"),
            ("GPU Frame Time", "gpu_frame_ms"),
        };

        internal static object GetFrameTiming(JObject @params)
        {
            var data = new Dictionary<string, object>();

            foreach (var (counterName, jsonKey) in COUNTER_MAP)
            {
                using var recorder = ProfilerRecorder.StartNew(ProfilerCategory.Internal, counterName);
                data[jsonKey] = recorder.Valid ? recorder.CurrentValue / 1e6 : 0.0;
                data[jsonKey.Replace("_ms", "_valid")] = recorder.Valid;
            }

            return new
            {
                success = true,
                message = "Frame timing captured.",
                data
            };
        }
    }
}
