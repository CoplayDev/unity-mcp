using System;
using System.Collections.Generic;
using MCPForUnity.Editor.Helpers;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Profiling;
using Unity.Profiling;

namespace MCPForUnity.Editor.Tools
{
    [McpForUnityTool("manage_profiler", AutoRegister = true)]
    public static class ManageProfiler
    {
        public static object HandleCommand(JObject @params)
        {
            if (@params == null) return new ErrorResponse("Parameters cannot be null.");
            var p = new ToolParams(@params);
            var actionResult = p.GetRequired("action");
            if (!actionResult.IsSuccess) return new ErrorResponse(actionResult.ErrorMessage);
            string action = actionResult.Value.ToLowerInvariant();

            try
            {
                return action switch
                {
                    "get_counters"       => GetCounters(p),
                    "list_categories"    => ListCategories(),
                    "start_recording"    => StartRecording(p),
                    "stop_recording"     => StopRecording(),
                    "get_frame_data"     => GetFrameData(p),
                    "get_memory_snapshot" => GetMemorySnapshot(),
                    _ => new ErrorResponse($"Unknown action: '{action}'.")
                };
            }
            catch (Exception e)
            {
                McpLog.Error($"[ManageProfiler] Action '{action}' failed: {e}");
                return new ErrorResponse($"Internal error: {e.Message}");
            }
        }

        private static object GetCounters(ToolParams p)
        {
            var countersStr = p.Get("counters");
            if (string.IsNullOrEmpty(countersStr))
                return new ErrorResponse("'counters' parameter is required (comma-separated names).");

            var names = countersStr.Split(',');
            var results = new Dictionary<string, object>();

            foreach (var raw in names)
            {
                var name = raw.Trim();
                if (string.IsNullOrEmpty(name)) continue;

                try
                {
                    var recorder = ProfilerRecorder.StartNew(ProfilerCategory.Any, name);
                    if (recorder.Valid && recorder.Count > 0)
                    {
                        results[name] = recorder.LastValue;
                    }
                    else
                    {
                        results[name] = null;
                    }
                    recorder.Dispose();
                }
                catch
                {
                    results[name] = null;
                }
            }

            return new SuccessResponse($"Read {results.Count} counter(s).", results);
        }

        private static object ListCategories()
        {
            // ProfilerCategory doesn't have a built-in "list all" API.
            // Return the well-known built-in categories.
            var categories = new List<string>
            {
                "Render", "Scripts", "Physics", "Animation", "AI",
                "Audio", "Video", "Particles", "Lighting", "Network",
                "Loading", "GC", "VSync", "Memory", "Internal", "UI",
                "FileIO", "Input", "Virtual Texturing",
            };

            return new SuccessResponse($"{categories.Count} known profiler categories.", new Dictionary<string, object>
            {
                ["categories"] = categories,
                ["note"] = "These are well-known built-in categories. Custom categories may also exist.",
            });
        }

        private static object StartRecording(ToolParams p)
        {
            var path = p.Get("path");
            if (string.IsNullOrEmpty(path))
                path = System.IO.Path.Combine(Application.persistentDataPath, "profiler_recording.raw");

            Profiler.logFile = path;
            Profiler.enableBinaryLog = true;
            Profiler.enabled = true;

            return new SuccessResponse($"Profiler recording started. Output: {path}.");
        }

        private static object StopRecording()
        {
            var path = Profiler.logFile;
            Profiler.enabled = false;
            Profiler.enableBinaryLog = false;
            Profiler.logFile = "";

            return new SuccessResponse($"Profiler recording stopped. File: {path}.");
        }

        private static object GetFrameData(ToolParams p)
        {
            int count = p.GetInt("count") ?? 10;

            var frames = new List<Dictionary<string, object>>();

            // Use ProfilerRecorder for main thread frame time
            var mainThreadRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "Main Thread", count);

            if (mainThreadRecorder.Valid && mainThreadRecorder.Count > 0)
            {
                int sampleCount = Math.Min(count, mainThreadRecorder.Count);
                for (int i = 0; i < sampleCount; i++)
                {
                    var sample = mainThreadRecorder.GetSample(i);
                    frames.Add(new Dictionary<string, object>
                    {
                        ["frame_index"] = i,
                        ["main_thread_ns"] = sample.Value,
                        ["main_thread_ms"] = Math.Round(sample.Value / 1_000_000.0, 3),
                    });
                }
            }

            mainThreadRecorder.Dispose();

            return new SuccessResponse($"Frame data: {frames.Count} frame(s).", new Dictionary<string, object>
            {
                ["frames"] = frames,
                ["frame_count"] = frames.Count,
            });
        }

        private static object GetMemorySnapshot()
        {
            long totalReserved = Profiler.GetTotalReservedMemoryLong();
            long totalAllocated = Profiler.GetTotalAllocatedMemoryLong();
            long totalUnused = Profiler.GetTotalUnusedReservedMemoryLong();
            long monoHeap = Profiler.GetMonoHeapSizeLong();
            long monoUsed = Profiler.GetMonoUsedSizeLong();
            long gfxMem = Profiler.GetAllocatedMemoryForGraphicsDriver();
            long tempMem = Profiler.GetTempAllocatorSize();

            return new SuccessResponse("Memory snapshot.", new Dictionary<string, object>
            {
                ["total_reserved_mb"] = Math.Round(totalReserved / (1024.0 * 1024.0), 2),
                ["total_allocated_mb"] = Math.Round(totalAllocated / (1024.0 * 1024.0), 2),
                ["total_unused_mb"] = Math.Round(totalUnused / (1024.0 * 1024.0), 2),
                ["mono_heap_mb"] = Math.Round(monoHeap / (1024.0 * 1024.0), 2),
                ["mono_used_mb"] = Math.Round(monoUsed / (1024.0 * 1024.0), 2),
                ["gfx_driver_mb"] = Math.Round(gfxMem / (1024.0 * 1024.0), 2),
                ["temp_allocator_mb"] = Math.Round(tempMem / (1024.0 * 1024.0), 2),
            });
        }
    }
}
