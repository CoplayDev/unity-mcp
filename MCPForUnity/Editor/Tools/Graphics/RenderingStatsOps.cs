using System;
using System.Collections.Generic;
using System.Linq;
using MCPForUnity.Editor.Helpers;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using Unity.Profiling;
using Unity.Profiling.LowLevel.Unsafe;
using UnityEngine.Profiling;

namespace MCPForUnity.Editor.Tools.Graphics
{
    internal static class RenderingStatsOps
    {
        private static readonly (string counterName, string jsonKey)[] COUNTER_MAP = new[]
        {
            ("Draw Calls Count", "draw_calls"),
            ("Batches Count", "batches"),
            ("SetPass Calls Count", "set_pass_calls"),
            ("Triangles Count", "triangles"),
            ("Vertices Count", "vertices"),
            ("Dynamic Batches Count", "dynamic_batches"),
            ("Dynamic Batched Draw Calls Count", "dynamic_batched_draw_calls"),
            ("Static Batches Count", "static_batches"),
            ("Static Batched Draw Calls Count", "static_batched_draw_calls"),
            ("Instanced Batches Count", "instanced_batches"),
            ("Instanced Batched Draw Calls Count", "instanced_batched_draw_calls"),
            ("Shadow Casters Count", "shadow_casters"),
            ("Render Textures Count", "render_textures"),
            ("Render Textures Bytes", "render_textures_bytes"),
            ("Used Textures Count", "used_textures"),
            ("Used Textures Bytes", "used_textures_bytes"),
            ("Render Textures Changes Count", "render_target_changes"),
            ("Visible Skinned Meshes Count", "visible_skinned_meshes"),
        };

        // === stats_get ===
        internal static object GetStats(JObject @params)
        {
            var stats = new Dictionary<string, object>();

            foreach (var (counterName, jsonKey) in COUNTER_MAP)
            {
                using var recorder = ProfilerRecorder.StartNew(ProfilerCategory.Render, counterName);
                stats[jsonKey] = recorder.Valid ? recorder.LastValue : 0;
            }

            return new
            {
                success = true,
                message = "Rendering stats captured.",
                data = stats
            };
        }

        // === stats_list_counters ===
        internal static object ListCounters(JObject @params)
        {
            var p = new ToolParams(@params);
            string categoryName = p.Get("category");

            ProfilerCategory category = ProfilerCategory.Render;
            if (!string.IsNullOrEmpty(categoryName))
            {
                category = TryResolveCategory(categoryName);
            }

            var handles = ProfilerRecorderHandle.GetAvailable(category);
            var counters = handles.Select(h =>
            {
                var desc = ProfilerRecorderHandle.GetDescription(h);
                return new
                {
                    name = desc.Name,
                    category = desc.Category.Name,
                    unit = desc.UnitType.ToString()
                };
            }).OrderBy(c => c.name).ToList();

            return new
            {
                success = true,
                message = $"Found {counters.Count} counters in category '{category.Name}'.",
                data = new { counters }
            };
        }

        // === stats_set_scene_debug_mode ===
        internal static object SetSceneDebugMode(JObject @params)
        {
            var p = new ToolParams(@params);
            string modeName = p.Get("mode");
            if (string.IsNullOrEmpty(modeName))
            {
                var validModes = string.Join(", ", Enum.GetNames(typeof(DrawCameraMode)).Take(20));
                return new ErrorResponse(
                    $"'mode' parameter required. Options: {validModes}");
            }

            if (!Enum.TryParse<DrawCameraMode>(modeName, true, out var drawMode))
            {
                var validModes = string.Join(", ", Enum.GetNames(typeof(DrawCameraMode)).Take(20));
                return new ErrorResponse($"Unknown mode '{modeName}'. Valid: {validModes}");
            }

            var sceneView = SceneView.lastActiveSceneView;
            if (sceneView == null)
                return new ErrorResponse("No active Scene View found.");

            var builtinModes = SceneView.GetBuiltinCameraModes().ToList();
            var matchingMode = builtinModes.FirstOrDefault(m => m.drawMode == drawMode);

            if (matchingMode.drawMode == drawMode)
            {
                sceneView.cameraMode = matchingMode;
            }
            else
            {
                sceneView.cameraMode = new SceneView.CameraMode
                {
                    drawMode = drawMode,
                    name = drawMode.ToString(),
                    section = "Shading Mode"
                };
            }

            sceneView.Repaint();

            return new
            {
                success = true,
                message = $"Scene debug mode set to '{drawMode}'."
            };
        }

        // === stats_get_memory ===
        internal static object GetMemory(JObject @params)
        {
            var data = new Dictionary<string, object>
            {
                ["totalAllocatedMB"] = Math.Round(Profiler.GetTotalAllocatedMemoryLong() / (1024.0 * 1024.0), 2),
                ["totalReservedMB"] = Math.Round(Profiler.GetTotalReservedMemoryLong() / (1024.0 * 1024.0), 2),
                ["totalUnusedReservedMB"] = Math.Round(Profiler.GetTotalUnusedReservedMemoryLong() / (1024.0 * 1024.0), 2),
                ["monoUsedMB"] = Math.Round(Profiler.GetMonoUsedSizeLong() / (1024.0 * 1024.0), 2),
                ["monoHeapMB"] = Math.Round(Profiler.GetMonoHeapSizeLong() / (1024.0 * 1024.0), 2),
                ["graphicsDriverMB"] = Math.Round(Profiler.GetAllocatedMemoryForGraphicsDriver() / (1024.0 * 1024.0), 2),
            };

            return new
            {
                success = true,
                message = "Memory stats captured.",
                data
            };
        }

        // --- Helper: Try to resolve a ProfilerCategory by name ---
        private static ProfilerCategory TryResolveCategory(string name)
        {
            // ProfilerCategory has static properties for well-known categories
            switch (name.ToLowerInvariant())
            {
                case "render": return ProfilerCategory.Render;
                case "scripts": return ProfilerCategory.Scripts;
                case "memory": return ProfilerCategory.Memory;
                case "physics": return ProfilerCategory.Physics;
                case "animation": return ProfilerCategory.Animation;
                case "audio": return ProfilerCategory.Audio;
                case "lighting": return ProfilerCategory.Lighting;
                case "network": return ProfilerCategory.Network;
                case "gui": return ProfilerCategory.Gui;
                case "ai": return ProfilerCategory.Ai;
                case "video": return ProfilerCategory.Video;
                case "loading": return ProfilerCategory.Loading;
                case "input": return ProfilerCategory.Input;
                case "vr": return ProfilerCategory.Vr;
                case "internal": return ProfilerCategory.Internal;
                default: return ProfilerCategory.Render;
            }
        }
    }
}
