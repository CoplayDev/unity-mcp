using System;
using System.Collections.Generic;
using MCPForUnity.Editor.Helpers;
using Newtonsoft.Json.Linq;
using UnityEngine.Profiling;

namespace MCPForUnity.Editor.Tools.Profiler
{
    internal static class SessionOps
    {
        private static readonly string[] AreaNames = Enum.GetNames(typeof(ProfilerArea));

        internal static object Start(JObject @params)
        {
            var p = new ToolParams(@params);
            string logFile = p.Get("log_file");
            bool enableCallstacks = p.GetBool("enable_callstacks");

            Profiler.enabled = true;

            bool recording = false;
            if (!string.IsNullOrEmpty(logFile))
            {
                Profiler.logFile = logFile;
                Profiler.enableBinaryLog = true;
                recording = true;
            }

            if (enableCallstacks)
                Profiler.enableAllocationCallstacks = true;

            return new SuccessResponse("Profiler started.", new
            {
                enabled = Profiler.enabled,
                recording,
                log_file = recording ? Profiler.logFile : null,
                allocation_callstacks = enableCallstacks,
            });
        }

        internal static object Stop(JObject @params)
        {
            string previousLogFile = Profiler.enableBinaryLog ? Profiler.logFile : null;

            Profiler.enableBinaryLog = false;
            Profiler.enableAllocationCallstacks = false;
            Profiler.enabled = false;

            return new SuccessResponse("Profiler stopped.", new
            {
                enabled = false,
                previous_log_file = previousLogFile,
            });
        }

        internal static object Status(JObject @params)
        {
            var areas = new Dictionary<string, bool>();
            foreach (string name in AreaNames)
            {
                if (Enum.TryParse<ProfilerArea>(name, out var area))
                    areas[name] = Profiler.GetAreaEnabled(area);
            }

            return new SuccessResponse("Profiler status.", new
            {
                enabled = Profiler.enabled,
                recording = Profiler.enableBinaryLog,
                log_file = Profiler.enableBinaryLog ? Profiler.logFile : null,
                allocation_callstacks = Profiler.enableAllocationCallstacks,
                areas,
            });
        }

        internal static object SetAreas(JObject @params)
        {
            var areasToken = @params["areas"] as JObject;
            if (areasToken == null)
                return new ErrorResponse($"'areas' parameter required. Valid areas: {string.Join(", ", AreaNames)}");

            var updated = new Dictionary<string, bool>();
            foreach (var prop in areasToken.Properties())
            {
                if (!Enum.TryParse<ProfilerArea>(prop.Name, true, out var area))
                    return new ErrorResponse($"Unknown area '{prop.Name}'. Valid: {string.Join(", ", AreaNames)}");

                bool enabled = prop.Value.ToObject<bool>();
                Profiler.SetAreaEnabled(area, enabled);
                updated[prop.Name] = enabled;
            }

            return new SuccessResponse($"Updated {updated.Count} profiler area(s).", new { areas = updated });
        }
    }
}
