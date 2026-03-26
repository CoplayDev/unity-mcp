using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using Unity.Profiling;

namespace MCPForUnity.Editor.Tools.Profiler
{
    internal static class FrameTimingOps
    {
        private static readonly (string counterName, string valueKey, string validKey)[] COUNTER_MAP = new[]
        {
            ("Main Thread", "main_thread_ms", "main_thread_valid"),
            ("Render Thread", "render_thread_ms", "render_thread_valid"),
            ("CPU Frame Time", "cpu_frame_ms", "cpu_frame_valid"),
            ("GPU Frame Time", "gpu_frame_ms", "gpu_frame_valid"),
        };

        internal static object GetFrameTiming(JObject @params)
        {
            var data = new Dictionary<string, object>();

            foreach (var (counterName, valueKey, validKey) in COUNTER_MAP)
            {
                using var recorder = ProfilerRecorder.StartNew(ProfilerCategory.Internal, counterName);
                data[valueKey] = recorder.Valid ? recorder.CurrentValue / 1e6 : 0.0;
                data[validKey] = recorder.Valid;
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
