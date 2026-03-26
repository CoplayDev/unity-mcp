using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using Unity.Profiling;

namespace MCPForUnity.Editor.Tools.Profiler
{
    internal static class PhysicsTimingOps
    {
        private static readonly (string counterName, string jsonKey)[] COUNTER_MAP = new[]
        {
            ("Physics.Simulate", "simulate_ms"),
            ("Physics2D.Simulate", "simulate_2d_ms"),
        };

        internal static object GetPhysicsTiming(JObject @params)
        {
            var data = new Dictionary<string, object>();

            foreach (var (counterName, jsonKey) in COUNTER_MAP)
            {
                using var recorder = ProfilerRecorder.StartNew(ProfilerCategory.Physics, counterName);
                data[jsonKey] = recorder.Valid ? recorder.CurrentValue / 1e6 : 0.0;
                data[jsonKey.Replace("_ms", "_valid")] = recorder.Valid;
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
