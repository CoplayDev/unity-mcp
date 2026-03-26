using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using Unity.Profiling;

namespace MCPForUnity.Editor.Tools.Profiler
{
    internal static class GCAllocOps
    {
        internal static object GetGCAlloc(JObject @params)
        {
            var data = new Dictionary<string, object>();

            using (var recorder = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC.Alloc"))
            {
                data["gc_alloc_bytes"] = recorder.Valid ? recorder.CurrentValue : 0L;
                data["gc_alloc_valid"] = recorder.Valid;
            }

            using (var recorder = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC.Alloc.Count"))
            {
                data["gc_alloc_count"] = recorder.Valid ? recorder.CurrentValue : 0L;
                data["gc_alloc_count_valid"] = recorder.Valid;
            }

            return new
            {
                success = true,
                message = "GC allocation stats captured.",
                data
            };
        }
    }
}
