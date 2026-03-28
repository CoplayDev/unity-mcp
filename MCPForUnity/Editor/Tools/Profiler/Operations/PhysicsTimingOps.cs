using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using Unity.Profiling;

namespace MCPForUnity.Editor.Tools.Profiler
{
    internal static class PhysicsTimingOps
    {
        private static readonly (string counterName, string valueKey, string validKey)[] COUNTER_MAP = new[]
        {
            ("Physics.Simulate", "simulate_ms", "simulate_valid"),
            ("Physics2D.Simulate", "simulate_2d_ms", "simulate_2d_valid"),
        };

        internal static object GetPhysicsTiming(JObject @params)
        {
            var data = new Dictionary<string, object>();

            foreach (var (counterName, valueKey, validKey) in COUNTER_MAP)
            {
                using var recorder = ProfilerRecorder.StartNew(ProfilerCategory.Physics, counterName);
                data[valueKey] = recorder.Valid ? recorder.CurrentValue / 1e6 : 0.0;
                data[validKey] = recorder.Valid;
            }

            return new
            {
                success = true,
                message = "Physics timing captured.",
                data
            };
        }
    }
}
