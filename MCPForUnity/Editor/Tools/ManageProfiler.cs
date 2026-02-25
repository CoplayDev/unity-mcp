using System.Collections.Generic;
using MCPForUnity.Editor.Helpers;
using Newtonsoft.Json.Linq;
using UnityEditor.Profiling;
using UnityEditorInternal;
using UnityEngine;

namespace MCPForUnity.Editor.Tools
{
    /// <summary>
    /// Reads Unity Profiler frame data and controls profiler state.
    /// Uses ProfilerDriver to access the same data visible in the Profiler window.
    /// </summary>
    [McpForUnityTool("manage_profiler", AutoRegister = false)]
    public static class ManageProfiler
    {
        /// <summary>
        /// Main handler for profiler actions.
        /// </summary>
        public static object HandleCommand(JObject @params)
        {
            if (@params == null)
            {
                return new ErrorResponse("Parameters cannot be null.");
            }

            var p = new ToolParams(@params);

            var actionResult = p.GetRequired("action");
            if (!actionResult.IsSuccess)
            {
                return new ErrorResponse(actionResult.ErrorMessage);
            }
            string action = actionResult.Value.ToLowerInvariant();

            switch (action)
            {
                case "read_frames":
                    return ReadFrames(p);
                case "enable":
                    return EnableProfiler();
                case "disable":
                    return DisableProfiler();
                case "status":
                    return GetStatus();
                case "clear":
                    return ClearFrames();
                default:
                    return new ErrorResponse($"Unknown action: '{action}'. Valid actions: read_frames, enable, disable, status, clear.");
            }
        }

        private static object ReadFrames(ToolParams p)
        {
            if (ProfilerDriver.enabled == false)
            {
                return new ErrorResponse("Profiler is not enabled. Use action='enable' first or open the Profiler window.");
            }

            int lastFrame = ProfilerDriver.lastFrameIndex;
            int firstFrame = ProfilerDriver.firstFrameIndex;
            if (lastFrame < 0 || firstFrame < 0)
            {
                return new ErrorResponse("No profiler frames available.");
            }

            int frameCount = p.GetInt("frameCount", 1) ?? 1;
            int threadIndex = p.GetInt("thread", 0) ?? 0;
            string filter = p.Get("filter") ?? "";
            float minMs = p.GetFloat("minMs", 0.01f) ?? 0.01f;

            frameCount = System.Math.Min(frameCount, lastFrame - firstFrame + 1);

            var frames = new List<object>();
            for (int f = 0; f < frameCount; f++)
            {
                int frameIndex = lastFrame - f;
                using var frameData = ProfilerDriver.GetRawFrameDataView(frameIndex, threadIndex);
                if (frameData == null || frameData.valid == false)
                {
                    continue;
                }

                var collected = new List<(string name, float ms, int childCount)>();
                for (int i = 0; i < frameData.sampleCount; i++)
                {
                    float timeMs = frameData.GetSampleTimeMs(i);
                    if (timeMs < minMs)
                    {
                        continue;
                    }

                    string name = frameData.GetSampleName(i);
                    if (string.IsNullOrEmpty(filter) == false &&
                        name.IndexOf(filter, System.StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        continue;
                    }

                    collected.Add((name, timeMs, frameData.GetSampleChildrenCount(i)));
                }

                // Sort by time descending
                collected.Sort((a, b) => b.ms.CompareTo(a.ms));

                var samples = new List<object>(collected.Count);
                foreach (var s in collected)
                {
                    samples.Add(new { s.name, s.ms, formatted = FormatTime(s.ms), s.childCount });
                }

                frames.Add(new
                {
                    frameIndex,
                    threadIndex,
                    sampleCount = frameData.sampleCount,
                    samples
                });
            }

            return new SuccessResponse($"{frames.Count} frames read", new { frames });
        }

        private static object EnableProfiler()
        {
            UnityEngine.Profiling.Profiler.enabled = true;
            ProfilerDriver.enabled = true;
            return new SuccessResponse("Profiler enabled.");
        }

        private static object DisableProfiler()
        {
            ProfilerDriver.enabled = false;
            UnityEngine.Profiling.Profiler.enabled = false;
            return new SuccessResponse("Profiler disabled.");
        }

        private static object GetStatus()
        {
            int firstFrame = ProfilerDriver.firstFrameIndex;
            int lastFrame = ProfilerDriver.lastFrameIndex;
            int frameCount = lastFrame >= firstFrame ? lastFrame - firstFrame + 1 : 0;

            return new SuccessResponse("Profiler status", new
            {
                enabled = ProfilerDriver.enabled,
                firstFrame,
                lastFrame,
                frameCount,
                isPlaying = Application.isPlaying
            });
        }

        private static object ClearFrames()
        {
            ProfilerDriver.ClearAllFrames();
            return new SuccessResponse("All profiler frames cleared.");
        }

        private static string FormatTime(float ms)
        {
            if (ms >= 1f) return $"{ms:F2}ms";
            if (ms >= 0.001f) return $"{ms * 1000f:F2}us";
            return $"{ms * 1_000_000f:F0}ns";
        }
    }
}
