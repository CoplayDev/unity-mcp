using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MCPForUnity.Editor.Helpers;
using Newtonsoft.Json.Linq;
using Unity.Profiling;
using Unity.Profiling.LowLevel.Unsafe;
using UnityEditor;

namespace MCPForUnity.Editor.Tools.Profiler
{
    internal static class CounterOps
    {
        internal static async Task<object> GetCountersAsync(JObject @params)
        {
            var p = new ToolParams(@params);
            var categoryResult = p.GetRequired("category");
            if (!categoryResult.IsSuccess)
                return new ErrorResponse(categoryResult.ErrorMessage);

            string categoryName = categoryResult.Value;
            var resolved = ResolveCategory(categoryName, out string categoryError);
            if (resolved == null)
                return new ErrorResponse(categoryError);
            ProfilerCategory category = resolved.Value;

            // Get counter names: explicit list or discover all in category
            var counterNames = GetRequestedCounters(p, category);
            if (counterNames.Count == 0)
                return new SuccessResponse($"No counters found in category '{categoryName}'.", new
                {
                    category = categoryName,
                    counters = new Dictionary<string, object>()
                });

            // Start recorders
            var recorders = new List<ProfilerRecorder>();
            foreach (string name in counterNames)
            {
                recorders.Add(ProfilerRecorder.StartNew(category, name));
            }

            // Wait 1 frame for recorders to accumulate data
            await WaitOneFrameAsync();

            // Read values and dispose
            var data = new Dictionary<string, object>();
            for (int i = 0; i < recorders.Count; i++)
            {
                var recorder = recorders[i];
                string name = counterNames[i];
                data[name] = recorder.Valid ? recorder.CurrentValueAsDouble : 0.0;
                data[name + "_valid"] = recorder.Valid;
                data[name + "_unit"] = recorder.Valid ? recorder.UnitType.ToString() : "Unknown";
                recorder.Dispose();
            }

            return new SuccessResponse($"Captured {counterNames.Count} counter(s) from '{categoryName}'.", new
            {
                category = categoryName,
                counters = data,
            });
        }

        private static List<string> GetRequestedCounters(ToolParams p, ProfilerCategory category)
        {
            var explicitCounters = p.GetStringArray("counters");
            if (explicitCounters != null && explicitCounters.Length > 0)
                return explicitCounters.ToList();

            var allHandles = new List<ProfilerRecorderHandle>();
            ProfilerRecorderHandle.GetAvailable(allHandles);
            return allHandles
                .Select(h => ProfilerRecorderHandle.GetDescription(h))
                .Where(d => string.Equals(d.Category.Name, category.Name, StringComparison.OrdinalIgnoreCase))
                .Select(d => d.Name)
                .OrderBy(n => n)
                .ToList();
        }

        private static Task WaitOneFrameAsync()
        {
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            void Tick()
            {
                EditorApplication.update -= Tick;
                tcs.TrySetResult(true);
            }

            EditorApplication.update += Tick;
            try { EditorApplication.QueuePlayerLoopUpdate(); } catch { /* throttled editor */ }
            return tcs.Task;
        }

        private static readonly string[] ValidCategories = new[]
        {
            "Render", "Scripts", "Memory", "Physics", "Physics2D", "Animation",
            "Audio", "Lighting", "Network", "Gui", "UI", "Ai", "Video",
            "Loading", "Input", "Vr", "Internal", "Particles", "FileIO", "VirtualTexturing"
        };

        internal static ProfilerCategory? ResolveCategory(string name, out string error)
        {
            error = null;
            switch (name.ToLowerInvariant())
            {
                case "render": return ProfilerCategory.Render;
                case "scripts": return ProfilerCategory.Scripts;
                case "memory": return ProfilerCategory.Memory;
                case "physics": return ProfilerCategory.Physics;
                case "physics2d": return ProfilerCategory.Physics2D;
                case "animation": return ProfilerCategory.Animation;
                case "audio": return ProfilerCategory.Audio;
                case "lighting": return ProfilerCategory.Lighting;
                case "network": return ProfilerCategory.Network;
                case "gui": case "ui": return ProfilerCategory.Gui;
                case "ai": return ProfilerCategory.Ai;
                case "video": return ProfilerCategory.Video;
                case "loading": return ProfilerCategory.Loading;
                case "input": return ProfilerCategory.Input;
                case "vr": return ProfilerCategory.Vr;
                case "internal": return ProfilerCategory.Internal;
                case "particles": return ProfilerCategory.Particles;
                case "fileio": return ProfilerCategory.FileIO;
                case "virtualtexturing": return ProfilerCategory.VirtualTexturing;
                default:
                    error = $"Unknown category '{name}'. Valid: {string.Join(", ", ValidCategories)}";
                    return null;
            }
        }
    }
}
