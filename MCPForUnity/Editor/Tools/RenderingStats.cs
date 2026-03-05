using MCPForUnity.Editor.Helpers;
using Newtonsoft.Json.Linq;
using Unity.Profiling;
using UnityEditor;
using UnityEngine;
using UnityEngine.Profiling;

namespace MCPForUnity.Editor.Tools
{
    /// <summary>
    /// MCP tool for reading Unity rendering and performance statistics.
    /// Actions: get_stats (rendering counters), get_memory (memory usage),
    ///          get_profiler (profiler frame data).
    /// Most useful during Play mode when rendering is active.
    /// </summary>
    [McpForUnityTool("rendering_stats", AutoRegister = true)]
    public static class RenderingStats
    {
        public static object HandleCommand(JObject @params)
        {
            if (@params == null)
                return new ErrorResponse("Parameters cannot be null.");

            var p = new ToolParams(@params);
            var actionResult = p.GetRequired("action");
            if (!actionResult.IsSuccess)
                return new ErrorResponse(actionResult.ErrorMessage);

            string action = actionResult.Value.ToLowerInvariant();

            try
            {
                return action switch
                {
                    "get_stats"    => GetRenderingStats(p),
                    "get_memory"   => GetMemoryStats(p),
                    "get_profiler" => GetProfilerStats(p),
                    _              => new ErrorResponse($"Unknown action: '{action}'. Valid: get_stats, get_memory, get_profiler.")
                };
            }
            catch (System.Exception ex)
            {
                return new ErrorResponse($"Error in {action}: {ex.Message}");
            }
        }

        private static object GetRenderingStats(ToolParams p)
        {
            bool isPlaying = EditorApplication.isPlaying;

            // Compute "saved by batching" = total batched draw calls - total batches
            int savedByBatching =
                (UnityStats.dynamicBatchedDrawCalls - UnityStats.dynamicBatches) +
                (UnityStats.staticBatchedDrawCalls - UnityStats.staticBatches) +
                (UnityStats.instancedBatchedDrawCalls - UnityStats.instancedBatches);

            // CPU/render thread timing via ProfilerRecorder (works in Editor + Play)
            double cpuMainMs = 0;
            double renderThreadMs = 0;
            using (var mainRec = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "Main Thread", 1))
            using (var renderRec = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "Render Thread", 1))
            {
                // Recorders need at least one frame; read last value if available
                if (mainRec.Valid && mainRec.LastValue > 0)
                    cpuMainMs = mainRec.LastValueAsDouble / 1_000_000.0; // ns → ms
                if (renderRec.Valid && renderRec.LastValue > 0)
                    renderThreadMs = renderRec.LastValueAsDouble / 1_000_000.0;
            }

            // Animation stats via ProfilerRecorder
            int visibleSkinnedMeshes = 0;
            int animationComponentsPlaying = 0;
            int animatorComponentsPlaying = 0;
            using (var skinnedRec = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Visible Skinned Meshes"))
            {
                if (skinnedRec.Valid) visibleSkinnedMeshes = (int)skinnedRec.LastValue;
            }
            using (var animRec = ProfilerRecorder.StartNew(ProfilerCategory.Animation, "Animation Components Playing"))
            {
                if (animRec.Valid) animationComponentsPlaying = (int)animRec.LastValue;
            }
            using (var animatorRec = ProfilerRecorder.StartNew(ProfilerCategory.Animation, "Animator Components Playing"))
            {
                if (animatorRec.Valid) animatorComponentsPlaying = (int)animatorRec.LastValue;
            }

            var data = new
            {
                isPlaying,
                rendering = new
                {
                    fps = isPlaying ? 1.0f / Time.unscaledDeltaTime : 0f,
                    deltaTime = Time.unscaledDeltaTime,
                    frameCount = Time.frameCount,
                    cpuMainMs,
                    renderThreadMs,
                    drawCalls = UnityStats.drawCalls,
                    batches = UnityStats.batches,
                    savedByBatching,
                    setPassCalls = UnityStats.setPassCalls,
                    triangles = UnityStats.triangles,
                    vertices = UnityStats.vertices,
                    shadowCasters = UnityStats.shadowCasters,
                    visibleSkinnedMeshes,
                    renderTextureChanges = UnityStats.renderTextureChanges,
                    renderTextureBytes = UnityStats.renderTextureBytes,
                    usedTextureMemorySize = UnityStats.usedTextureMemorySize,
                    usedTextureCount = UnityStats.usedTextureCount,
                    screenRes = $"{Screen.width}x{Screen.height}",
                    screenBytes = UnityStats.screenBytes,
                    dynamicBatchedDrawCalls = UnityStats.dynamicBatchedDrawCalls,
                    dynamicBatches = UnityStats.dynamicBatches,
                    staticBatchedDrawCalls = UnityStats.staticBatchedDrawCalls,
                    staticBatches = UnityStats.staticBatches,
                    instancedBatchedDrawCalls = UnityStats.instancedBatchedDrawCalls,
                    instancedBatches = UnityStats.instancedBatches,
                    visible = UnityStats.screenRes != ""
                },
                animation = new
                {
                    animationComponentsPlaying,
                    animatorComponentsPlaying
                },
                audio = new
                {
                    levelDb = AudioListener.volume > 0 ? 20f * Mathf.Log10(AudioListener.volume) : -80f,
                    dspLoad = AudioSettings.dspTime > 0 ? "available" : "unavailable",
                    speakerMode = AudioSettings.speakerMode.ToString(),
                    sampleRate = AudioSettings.outputSampleRate
                },
                qualitySettings = new
                {
                    qualityLevel = QualitySettings.GetQualityLevel(),
                    qualityName = QualitySettings.names[QualitySettings.GetQualityLevel()],
                    vSyncCount = QualitySettings.vSyncCount,
                    targetFrameRate = Application.targetFrameRate,
                    maxQueuedFrames = QualitySettings.maxQueuedFrames
                }
            };

            string note = !isPlaying
                ? "Not in Play mode — most stats will be 0."
                : (data.rendering.visible ? "" : "Game view may not be visible — stats may be 0.");

            return new SuccessResponse(
                isPlaying ? "Rendering stats captured." : "Editor not playing — limited stats.",
                new { data, note }
            );
        }

        private static object GetMemoryStats(ToolParams p)
        {
            const long BytesPerMB = 1024L * 1024L;

            var data = new
            {
                totalAllocatedMemoryMB = Profiler.GetTotalAllocatedMemoryLong() / (double)BytesPerMB,
                totalReservedMemoryMB = Profiler.GetTotalReservedMemoryLong() / (double)BytesPerMB,
                totalUnusedReservedMemoryMB = Profiler.GetTotalUnusedReservedMemoryLong() / (double)BytesPerMB,
                monoUsedSizeMB = Profiler.GetMonoUsedSizeLong() / (double)BytesPerMB,
                monoHeapSizeMB = Profiler.GetMonoHeapSizeLong() / (double)BytesPerMB,
                tempAllocatorSizeMB = Profiler.GetTempAllocatorSize() / (double)BytesPerMB,
                graphicsDriverAllocatedMemoryMB = Profiler.GetAllocatedMemoryForGraphicsDriver() / (double)BytesPerMB,
                isPlaying = EditorApplication.isPlaying
            };

            return new SuccessResponse("Memory stats captured.", data);
        }

        private static object GetProfilerStats(ToolParams p)
        {
            bool isPlaying = EditorApplication.isPlaying;
            int frameCount = Time.frameCount;

            var data = new
            {
                isPlaying,
                frameCount,
                realtimeSinceStartup = Time.realtimeSinceStartup,
                timeSinceLevelLoad = Time.timeSinceLevelLoad,
                timeScale = Time.timeScale,
                fixedDeltaTime = Time.fixedDeltaTime,
                maximumDeltaTime = Time.maximumDeltaTime,
                smoothDeltaTime = Time.smoothDeltaTime,
                captureFramerate = Time.captureFramerate,
                profilerEnabled = Profiler.enabled,
                // System info for context
                systemInfo = new
                {
                    gpuName = SystemInfo.graphicsDeviceName,
                    gpuVendor = SystemInfo.graphicsDeviceVendor,
                    gpuType = SystemInfo.graphicsDeviceType.ToString(),
                    gpuMemoryMB = SystemInfo.graphicsMemorySize,
                    gpuMultiThreaded = SystemInfo.graphicsMultiThreaded,
                    maxTextureSize = SystemInfo.maxTextureSize,
                    renderingThreadingMode = SystemInfo.renderingThreadingMode.ToString(),
                    processorType = SystemInfo.processorType,
                    processorCount = SystemInfo.processorCount,
                    systemMemoryMB = SystemInfo.systemMemorySize
                }
            };

            return new SuccessResponse("Profiler stats captured.", data);
        }
    }
}
