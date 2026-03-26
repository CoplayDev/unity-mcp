using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using Unity.Profiling;

namespace MCPForUnity.Editor.Tools.Profiler
{
    internal static class ScriptTimingOps
    {
        private static readonly (string counterName, string valueKey, string validKey)[] COUNTER_MAP = new[]
        {
            ("BehaviourUpdate", "update_ms", "update_valid"),
            ("FixedBehaviourUpdate", "fixed_update_ms", "fixed_update_valid"),
            ("PreLateUpdate.ScriptRunBehaviourLateUpdate", "late_update_ms", "late_update_valid"),
        };

        internal static object GetScriptTiming(JObject @params)
        {
            var data = new Dictionary<string, object>();

            foreach (var (counterName, valueKey, validKey) in COUNTER_MAP)
            {
                using var recorder = ProfilerRecorder.StartNew(ProfilerCategory.Scripts, counterName);
                data[valueKey] = recorder.Valid ? recorder.CurrentValue / 1e6 : 0.0;
                data[validKey] = recorder.Valid;
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
