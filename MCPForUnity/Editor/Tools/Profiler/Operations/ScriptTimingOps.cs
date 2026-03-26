using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using Unity.Profiling;

namespace MCPForUnity.Editor.Tools.Profiler
{
    internal static class ScriptTimingOps
    {
        private static readonly (string counterName, string jsonKey)[] COUNTER_MAP = new[]
        {
            ("BehaviourUpdate", "update_ms"),
            ("FixedBehaviourUpdate", "fixed_update_ms"),
            ("LateBehaviourUpdate", "late_update_ms"),
        };

        internal static object GetScriptTiming(JObject @params)
        {
            var data = new Dictionary<string, object>();

            foreach (var (counterName, jsonKey) in COUNTER_MAP)
            {
                using var recorder = ProfilerRecorder.StartNew(ProfilerCategory.Scripts, counterName);
                data[jsonKey] = recorder.Valid ? recorder.CurrentValue / 1e6 : 0.0;
                data[jsonKey.Replace("_ms", "_valid")] = recorder.Valid;
            }

            return new
            {
                success = true,
                message = "Script timing captured.",
                data
            };
        }
    }
}
