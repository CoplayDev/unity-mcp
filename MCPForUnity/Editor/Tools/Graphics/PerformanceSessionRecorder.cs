using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;
using Unity.Profiling;
using Unity.Profiling.LowLevel.Unsafe;
using UnityEditor;
using UnityEngine;

namespace MCPForUnity.Editor.Tools.Graphics
{
    /// <summary>
    /// Continuous per-frame performance recorder that auto-starts on Play mode entry.
    /// Records rendering stats + top-N system timings every frame into a fixed ring buffer.
    /// Query via <see cref="GetAggregatedStats"/>, <see cref="GetSystemStats"/>,
    /// and <see cref="GetSessionReport"/>.
    /// </summary>
    [InitializeOnLoad]
    internal static class PerformanceSessionRecorder
    {
        // --- Configuration ---
        private const int BufferCapacity = 72000; // ~20 min @ 60fps
        private const int TopSystemCount = 10;
        private const int RecorderRingCapacity = 512; // ProfilerRecorder internal buffer

        // --- Per-frame data structure ---
        internal struct FrameRecord
        {
            public int Frame;
            public float TimeSec;
            public float Fps;
            public float CpuMainMs;
            public float RenderThreadMs;
            public float GpuMs;
            public int DrawCalls;
            public int Batches;
            public int Triangles;
            public int EntityCount;
            public SystemTiming[] TopSystems;
        }

        internal struct SystemTiming
        {
            public string Name;
            public float Ms;
        }

        // --- State ---
        private static FrameRecord[] _buffer;
        private static int _writeIndex;
        private static int _frameCount;
        private static bool _recording;
        private static float _sessionStartTime;
        private static bool _gpuAvailable;

        // --- Static ProfilerRecorders (long-lived, warmed up) ---
        private static ProfilerRecorder _drawCallsRecorder;
        private static ProfilerRecorder _batchesRecorder;
        private static ProfilerRecorder _trianglesRecorder;

        // --- System profiling ---
        private static Dictionary<string, ProfilerRecorder> _systemRecorders;
        private static bool _systemRecordersInitialized;
        private static readonly FrameTiming[] _timingBuffer = new FrameTiming[1];

        static PerformanceSessionRecorder()
        {
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }

        private static void OnPlayModeChanged(PlayModeStateChange state)
        {
            switch (state)
            {
                case PlayModeStateChange.EnteredPlayMode:
                    StartRecording();
                    break;
                case PlayModeStateChange.ExitingPlayMode:
                    StopRecording();
                    break;
            }
        }

        private static void StartRecording()
        {
            _buffer = new FrameRecord[BufferCapacity];
            _writeIndex = 0;
            _frameCount = 0;
            _recording = true;
            _sessionStartTime = Time.realtimeSinceStartup;
            _systemRecordersInitialized = false;

            // Start long-lived rendering recorders
            _drawCallsRecorder = ProfilerRecorder.StartNew(
                ProfilerCategory.Render, "Draw Calls Count", RecorderRingCapacity);
            _batchesRecorder = ProfilerRecorder.StartNew(
                ProfilerCategory.Render, "Batches Count", RecorderRingCapacity);
            _trianglesRecorder = ProfilerRecorder.StartNew(
                ProfilerCategory.Render, "Triangles Count", RecorderRingCapacity);

            _systemRecorders = new Dictionary<string, ProfilerRecorder>();

            EditorApplication.update += RecordFrame;
        }

        private static void StopRecording()
        {
            _recording = false;
            EditorApplication.update -= RecordFrame;

            _drawCallsRecorder.Dispose();
            _batchesRecorder.Dispose();
            _trianglesRecorder.Dispose();

            if (_systemRecorders != null)
            {
                foreach (var rec in _systemRecorders.Values)
                    rec.Dispose();
                _systemRecorders = null;
            }
        }

        private static void RecordFrame()
        {
            if (!_recording || !EditorApplication.isPlaying) return;

            // Lazy-init system recorders after first frame (markers need to exist)
            if (!_systemRecordersInitialized && Time.frameCount > 2)
                InitializeSystemRecorders();

            // Frame timing
            FrameTimingManager.CaptureFrameTimings();
            float cpuMs = 0f, renderMs = 0f, gpuMs = 0f;
            uint count = FrameTimingManager.GetLatestTimings(1, _timingBuffer);
            if (count > 0)
            {
                cpuMs = (float)_timingBuffer[0].cpuFrameTime;
                renderMs = (float)_timingBuffer[0].cpuRenderThreadFrameTime;
                gpuMs = (float)_timingBuffer[0].gpuFrameTime;
                _gpuAvailable = gpuMs > 0.001f;
            }

            // Collect top-N systems
            SystemTiming[] topSystems = null;
            if (_systemRecorders != null && _systemRecorders.Count > 0)
            {
                topSystems = _systemRecorders
                    .Where(kv => kv.Value.Valid && kv.Value.LastValue > 0)
                    .Select(kv => new SystemTiming
                    {
                        Name = kv.Key,
                        Ms = kv.Value.LastValue * 1e-6f // ns → ms
                    })
                    .OrderByDescending(s => s.Ms)
                    .Take(TopSystemCount)
                    .ToArray();
            }

            // Entity count (try DOTS if available)
            int entityCount = 0;
#if UNITY_ENTITIES
            try
            {
                var world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
                if (world != null && world.IsCreated)
                    entityCount = world.EntityManager.UniversalQuery.CalculateEntityCount();
            }
            catch { /* DOTS not available or world destroyed */ }
#endif

            var record = new FrameRecord
            {
                Frame = Time.frameCount,
                TimeSec = Time.realtimeSinceStartup - _sessionStartTime,
                Fps = 1f / Mathf.Max(Time.unscaledDeltaTime, 0.0001f),
                CpuMainMs = cpuMs,
                RenderThreadMs = renderMs,
                GpuMs = gpuMs,
                DrawCalls = _drawCallsRecorder.Valid ? (int)_drawCallsRecorder.LastValue : 0,
                Batches = _batchesRecorder.Valid ? (int)_batchesRecorder.LastValue : 0,
                Triangles = _trianglesRecorder.Valid ? (int)_trianglesRecorder.LastValue : 0,
                EntityCount = entityCount,
                TopSystems = topSystems
            };

            _buffer[_writeIndex % BufferCapacity] = record;
            _writeIndex++;
            _frameCount++;
        }

        private static void InitializeSystemRecorders()
        {
            _systemRecordersInitialized = true;

            var allHandles = new List<ProfilerRecorderHandle>();
            ProfilerRecorderHandle.GetAvailable(allHandles);

            foreach (var handle in allHandles)
            {
                var desc = ProfilerRecorderHandle.GetDescription(handle);
                // DOTS auto-instruments systems with "Default World <SystemName>"
                if (!desc.Name.StartsWith("Default World ")) continue;
                // Skip system groups (they aggregate children)
                if (desc.Name.EndsWith("Group")) continue;

                string shortName = desc.Name.Substring("Default World ".Length);
                // Only track DOTSRPG, BDP, and ProjectDawn systems (skip Unity internal)
                if (!shortName.StartsWith("DOTSRPG.") &&
                    !shortName.Contains("BehaviorTree") &&
                    !shortName.Contains("BDP") &&
                    !shortName.StartsWith("ProjectDawn."))
                    continue;

                // Clean up namespace prefix for readability
                string displayName = shortName
                    .Replace("DOTSRPG.", "")
                    .Replace("ProjectDawn.Navigation.", "Nav.");

                _systemRecorders[displayName] = ProfilerRecorder.StartNew(
                    desc.Category, desc.Name, RecorderRingCapacity);
            }

            Debug.Log($"[PerfRecorder] Tracking {_systemRecorders.Count} systems.");
        }

        // --- Query Methods ---

        internal static object GetAggregatedStats(int frames)
        {
            if (_frameCount == 0)
                return new { success = false, message = "No frames recorded. Enter Play mode first." };

            int available = Math.Min(_frameCount, BufferCapacity);
            int sampleCount = Math.Min(frames > 0 ? frames : available, available);
            int startIdx = (_writeIndex - sampleCount + BufferCapacity) % BufferCapacity;

            var fpsList = new float[sampleCount];
            var cpuList = new float[sampleCount];
            var drawCallList = new float[sampleCount];
            var batchList = new float[sampleCount];
            var triList = new float[sampleCount];
            var entityList = new float[sampleCount];

            for (int i = 0; i < sampleCount; i++)
            {
                var r = _buffer[(startIdx + i) % BufferCapacity];
                fpsList[i] = r.Fps;
                cpuList[i] = r.CpuMainMs;
                drawCallList[i] = r.DrawCalls;
                batchList[i] = r.Batches;
                triList[i] = r.Triangles;
                entityList[i] = r.EntityCount;
            }

            float durationSec = _buffer[(_writeIndex - 1 + BufferCapacity) % BufferCapacity].TimeSec
                              - _buffer[startIdx].TimeSec;

            return new
            {
                success = true,
                message = $"Aggregated stats over {sampleCount} frames ({durationSec:F1}s).",
                data = new
                {
                    frames_sampled = sampleCount,
                    duration_sec = Math.Round(durationSec, 2),
                    gpu_available = _gpuAvailable,
                    fps = ComputeStats(fpsList),
                    cpu_main_ms = ComputeStats(cpuList),
                    draw_calls = ComputeStats(drawCallList),
                    batches = ComputeStats(batchList),
                    triangles = ComputeStats(triList),
                    entities = ComputeStats(entityList)
                }
            };
        }

        internal static object GetSystemStats(int topN)
        {
            if (_systemRecorders == null || _systemRecorders.Count == 0)
                return new { success = false, message = "No system recorders. Enter Play mode first." };

            int n = topN > 0 ? topN : TopSystemCount;

            var systems = _systemRecorders
                .Where(kv => kv.Value.Valid)
                .Select(kv =>
                {
                    var sampleList = new List<ProfilerRecorderSample>(kv.Value.Count);
                    kv.Value.CopyTo(sampleList);
                    var msValues = sampleList.Select(s => (float)(s.Value * 1e-6)).ToArray();
                    float avg = msValues.Length > 0 ? msValues.Average() : 0;
                    return new { name = kv.Key, avg_ms = avg, samples = msValues };
                })
                .OrderByDescending(s => s.avg_ms)
                .Take(n)
                .Select(s => new
                {
                    s.name,
                    avg_ms = Math.Round(s.avg_ms, 3),
                    max_ms = Math.Round(s.samples.Length > 0 ? s.samples.Max() : 0, 3),
                    p95_ms = Math.Round(Percentile(s.samples, 95), 3),
                    pct_of_frame = 0.0 // filled below
                })
                .ToList();

            // Compute total and percentages
            double totalMs = systems.Sum(s => s.avg_ms);
            var result = systems.Select(s => new
            {
                s.name,
                s.avg_ms,
                s.max_ms,
                s.p95_ms,
                pct_of_frame = totalMs > 0 ? Math.Round(s.avg_ms / totalMs * 100, 1) : 0
            }).ToList();

            return new
            {
                success = true,
                message = $"Top {result.Count()} systems by avg CPU time.",
                data = new
                {
                    total_tracked_ms = Math.Round(totalMs, 3),
                    system_count = _systemRecorders.Count(),
                    systems = result
                }
            };
        }

        internal static object GetSessionReport(bool includeTimeline, bool includeCsv)
        {
            if (_frameCount == 0)
                return new { success = false, message = "No session data. Play and stop first." };

            int available = Math.Min(_frameCount, BufferCapacity);
            int startIdx = (_writeIndex - available + BufferCapacity) % BufferCapacity;

            // Summary stats
            var fpsList = new float[available];
            var cpuList = new float[available];
            for (int i = 0; i < available; i++)
            {
                var r = _buffer[(startIdx + i) % BufferCapacity];
                fpsList[i] = r.Fps;
                cpuList[i] = r.CpuMainMs;
            }

            float duration = _buffer[(_writeIndex - 1 + BufferCapacity) % BufferCapacity].TimeSec;

            // Markdown summary
            var md = new StringBuilder();
            md.AppendLine("# Performance Session Report");
            md.AppendLine();
            md.AppendLine($"| Metric | Avg | Min | Max | P95 |");
            md.AppendLine($"|--------|-----|-----|-----|-----|");
            AppendStatsRow(md, "FPS", fpsList);
            AppendStatsRow(md, "CPU (ms)", cpuList);
            md.AppendLine();
            md.AppendLine($"- **Frames**: {available}");
            md.AppendLine($"- **Duration**: {duration:F1}s");
            md.AppendLine($"- **GPU available**: {_gpuAvailable}");

            // JSON timeline (sampled — every Nth frame to keep payload <100KB)
            JArray timeline = null;
            if (includeTimeline)
            {
                int step = Math.Max(1, available / 100); // max 100 data points to keep payload under MCP limits
                timeline = new JArray();
                for (int i = 0; i < available; i += step)
                {
                    var r = _buffer[(startIdx + i) % BufferCapacity];
                    var entry = new JObject
                    {
                        ["frame"] = r.Frame,
                        ["time"] = Math.Round(r.TimeSec, 2),
                        ["fps"] = Math.Round(r.Fps, 1),
                        ["cpu_ms"] = Math.Round(r.CpuMainMs, 2),
                        ["draw_calls"] = r.DrawCalls,
                        ["entities"] = r.EntityCount
                    };
                    // Top system name only (use get_system_stats for full breakdown)
                    if (r.TopSystems != null && r.TopSystems.Length > 0)
                        entry["top_system"] = r.TopSystems[0].Name;
                    timeline.Add(entry);
                }
            }

            // CSV
            string csv = null;
            if (includeCsv)
            {
                var sb = new StringBuilder();
                sb.AppendLine("frame,time_sec,fps,cpu_ms,render_ms,draw_calls,batches,triangles,entities");
                int step = Math.Max(1, available / 500); // max 500 rows to keep CSV manageable
                for (int i = 0; i < available; i += step)
                {
                    var r = _buffer[(startIdx + i) % BufferCapacity];
                    sb.AppendLine($"{r.Frame},{r.TimeSec:F2},{r.Fps:F1},{r.CpuMainMs:F2}," +
                                 $"{r.RenderThreadMs:F2},{r.DrawCalls},{r.Batches},{r.Triangles},{r.EntityCount}");
                }
                csv = sb.ToString();
            }

            return new
            {
                success = true,
                message = $"Session report: {available} frames over {duration:F1}s.",
                data = new
                {
                    summary_markdown = md.ToString(),
                    timeline,
                    csv,
                    fps = ComputeStats(fpsList),
                    cpu_main_ms = ComputeStats(cpuList),
                    frames_recorded = available,
                    duration_sec = Math.Round(duration, 2),
                    gpu_available = _gpuAvailable
                }
            };
        }

        // --- Statistics helpers ---

        private static object ComputeStats(float[] values)
        {
            if (values.Length == 0) return new { avg = 0f, min = 0f, max = 0f, p50 = 0f, p95 = 0f };
            Array.Sort(values);
            return new
            {
                avg = Math.Round(values.Average(), 2),
                min = Math.Round(values[0], 2),
                max = Math.Round(values[values.Length - 1], 2),
                p50 = Math.Round(Percentile(values, 50), 2),
                p95 = Math.Round(Percentile(values, 95), 2)
            };
        }

        private static float Percentile(float[] sorted, int pct)
        {
            if (sorted.Length == 0) return 0;
            float idx = (pct / 100f) * (sorted.Length - 1);
            int lower = (int)Math.Floor(idx);
            int upper = Math.Min(lower + 1, sorted.Length - 1);
            float frac = idx - lower;
            return sorted[lower] + frac * (sorted[upper] - sorted[lower]);
        }

        private static void AppendStatsRow(StringBuilder sb, string label, float[] values)
        {
            Array.Sort(values);
            sb.AppendLine($"| {label} | {values.Average():F1} | {values[0]:F1} | " +
                          $"{values[values.Length - 1]:F1} | {Percentile(values, 95):F1} |");
        }
    }
}
