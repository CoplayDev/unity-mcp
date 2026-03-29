using MCPForUnity.Editor.Helpers;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace MCPForUnity.Editor.Tools.Profiler
{
    internal static class FrameTimingOps
    {
        internal static object GetFrameTiming(JObject @params)
        {
            if (!FrameTimingManager.IsFeatureEnabled())
            {
                return new ErrorResponse(
                    "Frame Timing Stats is not enabled. "
                    + "Enable it in Project Settings > Player > Other Settings > 'Frame Timing Stats', "
                    + "or use a Development Build (always enabled).");
            }

            FrameTimingManager.CaptureFrameTimings();
            var timings = new FrameTiming[1];
            uint count = FrameTimingManager.GetLatestTimings(1, timings);

            if (count == 0)
            {
                return new SuccessResponse("No frame timing data available yet (need a few frames).", new
                {
                    available = false,
                });
            }

            var t = timings[0];
            return new SuccessResponse("Frame timing captured.", new
            {
                available = true,
                cpu_frame_time_ms = t.cpuFrameTime,
                cpu_main_thread_frame_time_ms = t.cpuMainThreadFrameTime,
                cpu_main_thread_present_wait_time_ms = t.cpuMainThreadPresentWaitTime,
                cpu_render_thread_frame_time_ms = t.cpuRenderThreadFrameTime,
                gpu_frame_time_ms = t.gpuFrameTime,
                frame_start_timestamp = t.frameStartTimestamp,
                first_submit_timestamp = t.firstSubmitTimestamp,
                cpu_time_present_called = t.cpuTimePresentCalled,
                cpu_time_frame_complete = t.cpuTimeFrameComplete,
                height_scale = t.heightScale,
                width_scale = t.widthScale,
                sync_interval = t.syncInterval,
            });
        }
    }
}
