using System;
using MCPForUnity.Editor.Helpers;
using Newtonsoft.Json.Linq;

namespace MCPForUnity.Editor.Tools.Profiler
{
    [McpForUnityTool("manage_profiler", AutoRegister = false, Group = "core")]
    public static class ManageProfiler
    {
        public static object HandleCommand(JObject @params)
        {
            if (@params == null)
                return new ErrorResponse("Parameters cannot be null.");

            var p = new ToolParams(@params);
            string action = p.Get("action")?.ToLowerInvariant();

            if (string.IsNullOrEmpty(action))
                return new ErrorResponse("'action' parameter is required.");

            try
            {
                switch (action)
                {
                    case "get_frame_timing":
                        return FrameTimingOps.GetFrameTiming(@params);
                    case "get_script_timing":
                        return ScriptTimingOps.GetScriptTiming(@params);
                    case "get_physics_timing":
                        return PhysicsTimingOps.GetPhysicsTiming(@params);
                    case "get_gc_alloc":
                        return GCAllocOps.GetGCAlloc(@params);
                    case "get_animation_timing":
                        return AnimationTimingOps.GetAnimationTiming(@params);
                    default:
                        return new ErrorResponse(
                            $"Unknown action: '{action}'. Valid actions: "
                            + "get_frame_timing, get_script_timing, get_physics_timing, "
                            + "get_gc_alloc, get_animation_timing.");
                }
            }
            catch (Exception ex)
            {
                McpLog.Error($"[ManageProfiler] Action '{action}' failed: {ex}");
                return new ErrorResponse($"Error in action '{action}': {ex.Message}");
            }
        }
    }
}
