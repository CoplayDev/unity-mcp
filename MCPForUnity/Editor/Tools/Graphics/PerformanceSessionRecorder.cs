using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
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
        private const string SessionDirectory = "Logs/PerfSessions";
        private const int ExportTimelineMaxPoints = 500; // full-res sample cap for disk export
        private const int ExportSystemTopN = 30; // more systems saved to disk than MCP returns

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
        private static string _lastExportPath;

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

            // Auto-export session to disk before disposing recorders
            if (_frameCount > 0)
                ExportSessionToDisk();

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

        // --- Persistent Session Export ---

        private static void ExportSessionToDisk()
        {
            try
            {
                int available = Math.Min(_frameCount, BufferCapacity);
                int startIdx = (_writeIndex - available + BufferCapacity) % BufferCapacity;
                float duration = _buffer[(_writeIndex - 1 + BufferCapacity) % BufferCapacity].TimeSec;

                // Compute summary stats
                var fpsList = new float[available];
                var cpuList = new float[available];
                int peakEntities = 0;
                for (int i = 0; i < available; i++)
                {
                    var r = _buffer[(startIdx + i) % BufferCapacity];
                    fpsList[i] = r.Fps;
                    cpuList[i] = r.CpuMainMs;
                    if (r.EntityCount > peakEntities) peakEntities = r.EntityCount;
                }

                // System stats (capture before recorders are disposed)
                var systemStats = new List<object>();
                if (_systemRecorders != null)
                {
                    systemStats = _systemRecorders
                        .Where(kv => kv.Value.Valid)
                        .Select(kv =>
                        {
                            var samples = new List<ProfilerRecorderSample>(kv.Value.Count);
                            kv.Value.CopyTo(samples);
                            var msValues = samples.Select(s => (float)(s.Value * 1e-6)).ToArray();
                            float avg = msValues.Length > 0 ? msValues.Average() : 0;
                            float max = msValues.Length > 0 ? msValues.Max() : 0;
                            float p95 = Percentile(msValues.OrderBy(v => v).ToArray(), 95);
                            return (object)new { name = kv.Key, avg_ms = Math.Round(avg, 3),
                                max_ms = Math.Round(max, 3), p95_ms = Math.Round(p95, 3) };
                        })
                        .OrderByDescending(s => ((dynamic)s).avg_ms)
                        .Take(ExportSystemTopN)
                        .ToList();
                }

                // Timeline (higher resolution for disk — up to ExportTimelineMaxPoints)
                int step = Math.Max(1, available / ExportTimelineMaxPoints);
                var timeline = new List<object>();
                for (int i = 0; i < available; i += step)
                {
                    var r = _buffer[(startIdx + i) % BufferCapacity];
                    timeline.Add(new
                    {
                        frame = r.Frame, time = Math.Round(r.TimeSec, 2),
                        fps = Math.Round(r.Fps, 1), cpu_ms = Math.Round(r.CpuMainMs, 2),
                        render_ms = Math.Round(r.RenderThreadMs, 2), gpu_ms = Math.Round(r.GpuMs, 2),
                        draw_calls = r.DrawCalls, batches = r.Batches,
                        triangles = r.Triangles, entities = r.EntityCount,
                        top_system = r.TopSystems != null && r.TopSystems.Length > 0
                            ? r.TopSystems[0].Name : null
                    });
                }

                Array.Sort(fpsList);
                Array.Sort(cpuList);

                var session = new
                {
                    version = 1,
                    exported_at = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                    project = Application.productName,
                    scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name,
                    duration_sec = Math.Round(duration, 2),
                    frames_recorded = available,
                    peak_entities = peakEntities,
                    gpu_available = _gpuAvailable,
                    summary = new
                    {
                        fps = new { avg = Math.Round(fpsList.Average(), 1),
                            min = Math.Round(fpsList[0], 1),
                            max = Math.Round(fpsList[fpsList.Length - 1], 1),
                            p95 = Math.Round(Percentile(fpsList, 95), 1) },
                        cpu_ms = new { avg = Math.Round(cpuList.Average(), 2),
                            min = Math.Round(cpuList[0], 2),
                            max = Math.Round(cpuList[cpuList.Length - 1], 2),
                            p95 = Math.Round(Percentile(cpuList, 95), 2) }
                    },
                    systems = systemStats,
                    timeline
                };

                // Write to disk
                string dir = Path.Combine(Application.dataPath, "..", SessionDirectory);
                Directory.CreateDirectory(dir);

                string filename = $"perf-{DateTime.Now:yyyyMMdd-HHmmss}.json";
                string path = Path.Combine(dir, filename);

                File.WriteAllText(path, JsonConvert.SerializeObject(session, Formatting.Indented));
                _lastExportPath = path;

                Debug.Log($"[PerfRecorder] Session exported: {path} ({available} frames, {duration:F1}s)");
            }
            catch (Exception e)
            {
                Debug.LogError($"[PerfRecorder] Failed to export session: {e.Message}");
            }
        }

        /// <summary>Returns the path of the last exported session file, or null.</summary>
        internal static string GetLastExportPath() => _lastExportPath;

        /// <summary>Lists all saved session files in the PerfSessions directory.</summary>
        internal static object ListSessions()
        {
            string dir = Path.Combine(Application.dataPath, "..", SessionDirectory);
            if (!Directory.Exists(dir))
                return new { success = true, message = "No sessions found.", data = new object[0] };

            var files = Directory.GetFiles(dir, "perf-*.json")
                .OrderByDescending(f => f)
                .Select(f =>
                {
                    var fi = new FileInfo(f);
                    // Read just the summary from the file header
                    try
                    {
                        var json = JObject.Parse(File.ReadAllText(f));
                        return (object)new
                        {
                            filename = fi.Name,
                            path = f,
                            size_kb = Math.Round(fi.Length / 1024.0, 1),
                            exported_at = json["exported_at"]?.ToString(),
                            scene = json["scene"]?.ToString(),
                            duration_sec = json["duration_sec"]?.Value<double>() ?? 0,
                            frames = json["frames_recorded"]?.Value<int>() ?? 0,
                            peak_entities = json["peak_entities"]?.Value<int>() ?? 0,
                            avg_fps = json["summary"]?["fps"]?["avg"]?.Value<double>() ?? 0
                        };
                    }
                    catch
                    {
                        return (object)new { filename = fi.Name, path = f,
                            size_kb = Math.Round(fi.Length / 1024.0, 1),
                            error = "Failed to parse" };
                    }
                })
                .ToArray();

            return new { success = true, message = $"Found {files.Length} session(s).", data = files };
        }

        /// <summary>Loads a session file and returns its full JSON content.</summary>
        internal static object LoadSession(string filename)
        {
            string dir = Path.Combine(Application.dataPath, "..", SessionDirectory);
            string path = Path.Combine(dir, filename);
            if (!File.Exists(path))
                return new { success = false, message = $"Session file not found: {filename}" };

            try
            {
                var json = JObject.Parse(File.ReadAllText(path));
                return new { success = true, message = $"Loaded session: {filename}", data = json };
            }
            catch (Exception e)
            {
                return new { success = false, message = $"Failed to parse: {e.Message}" };
            }
        }

        /// <summary>Analyzes a session file for bottlenecks and returns a report.</summary>
        internal static object AnalyzeSession(string filename)
        {
            string dir = Path.Combine(Application.dataPath, "..", SessionDirectory);
            string path = Path.Combine(dir, filename);
            if (!File.Exists(path))
                return new { success = false, message = $"Session file not found: {filename}" };

            try
            {
                var json = JObject.Parse(File.ReadAllText(path));
                var summary = json["summary"];
                var systems = json["systems"] as JArray;
                var timeline = json["timeline"] as JArray;

                double avgFps = summary?["fps"]?["avg"]?.Value<double>() ?? 0;
                double avgCpu = summary?["cpu_ms"]?["avg"]?.Value<double>() ?? 0;
                double p95Cpu = summary?["cpu_ms"]?["p95"]?.Value<double>() ?? 0;

                // Bottleneck detection
                var issues = new List<object>();

                if (avgFps < 30)
                    issues.Add(new { severity = "HIGH", issue = $"Low FPS: avg {avgFps:F1} (target: 60)" });
                else if (avgFps < 60)
                    issues.Add(new { severity = "MEDIUM", issue = $"Below target FPS: avg {avgFps:F1}" });

                if (p95Cpu > 33.3)
                    issues.Add(new { severity = "HIGH",
                        issue = $"CPU P95 spikes: {p95Cpu:F1}ms (>33.3ms = <30fps)" });

                // Identify heaviest systems
                var heavySystems = new List<object>();
                if (systems != null)
                {
                    double totalSystemMs = systems.Sum(s => s["avg_ms"]?.Value<double>() ?? 0);
                    foreach (var sys in systems.Take(5))
                    {
                        double sysMs = sys["avg_ms"]?.Value<double>() ?? 0;
                        double pct = totalSystemMs > 0 ? sysMs / totalSystemMs * 100 : 0;
                        heavySystems.Add(new
                        {
                            name = sys["name"]?.ToString(),
                            avg_ms = sysMs,
                            p95_ms = sys["p95_ms"]?.Value<double>() ?? 0,
                            pct_of_tracked = Math.Round(pct, 1)
                        });

                        if (sysMs > 5.0)
                            issues.Add(new { severity = "HIGH",
                                issue = $"Heavy system: {sys["name"]} avg {sysMs:F1}ms" });
                        else if (sysMs > 2.0)
                            issues.Add(new { severity = "MEDIUM",
                                issue = $"Notable system: {sys["name"]} avg {sysMs:F1}ms" });
                    }
                }

                // Timeline anomalies — detect FPS drops
                if (timeline != null && timeline.Count > 10)
                {
                    int spikeCount = 0;
                    foreach (var point in timeline)
                    {
                        double cpuMs = point["cpu_ms"]?.Value<double>() ?? 0;
                        if (cpuMs > avgCpu * 3) spikeCount++;
                    }
                    if (spikeCount > timeline.Count * 0.1)
                        issues.Add(new { severity = "MEDIUM",
                            issue = $"Frequent CPU spikes: {spikeCount}/{timeline.Count} frames >3x avg" });
                }

                return new
                {
                    success = true,
                    message = $"Analysis of {filename}: {issues.Count} issue(s) found.",
                    data = new
                    {
                        session = new
                        {
                            scene = json["scene"]?.ToString(),
                            duration_sec = json["duration_sec"]?.Value<double>() ?? 0,
                            frames = json["frames_recorded"]?.Value<int>() ?? 0,
                            peak_entities = json["peak_entities"]?.Value<int>() ?? 0,
                        },
                        performance = new { avg_fps = avgFps, avg_cpu_ms = avgCpu, p95_cpu_ms = p95Cpu },
                        top_systems = heavySystems,
                        issues,
                        recommendation = issues.Any(i => ((dynamic)i).severity == "HIGH")
                            ? "HIGH priority optimization needed. Focus on the heaviest systems listed above."
                            : issues.Any() ? "Minor issues detected. Consider optimizing noted systems."
                            : "Performance looks healthy."
                    }
                };
            }
            catch (Exception e)
            {
                return new { success = false, message = $"Analysis failed: {e.Message}" };
            }
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
