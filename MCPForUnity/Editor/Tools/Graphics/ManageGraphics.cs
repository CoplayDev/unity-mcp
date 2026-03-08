using System;
using Newtonsoft.Json.Linq;
using MCPForUnity.Editor.Helpers;

namespace MCPForUnity.Editor.Tools.Graphics
{
    [McpForUnityTool("manage_graphics", AutoRegister = false, Group = "core")]
    public static class ManageGraphics
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
                    // --- Health check ---
                    case "ping":
                        var pipeName = GraphicsHelpers.GetPipelineName();
                        return new
                        {
                            success = true,
                            message = $"Graphics tool ready. Pipeline: {pipeName}",
                            data = new
                            {
                                pipeline = RenderPipelineUtility.GetActivePipeline().ToString(),
                                pipelineName = pipeName,
                                hasVolumeSystem = GraphicsHelpers.HasVolumeSystem,
                                hasURP = GraphicsHelpers.HasURP,
                                hasHDRP = GraphicsHelpers.HasHDRP,
                                availableEffects = GraphicsHelpers.HasVolumeSystem
                                    ? GraphicsHelpers.GetAvailableEffectTypes().Count : 0
                            }
                        };

                    // --- Volume actions (require Volume system = URP or HDRP) ---
                    case "volume_create":
                    case "volume_add_effect":
                    case "volume_set_effect":
                    case "volume_remove_effect":
                    case "volume_get_info":
                    case "volume_set_properties":
                    case "volume_list_effects":
                    case "volume_create_profile":
                    {
                        if (!GraphicsHelpers.HasVolumeSystem)
                            return new ErrorResponse(
                                "Volume system not available. Requires URP or HDRP (com.unity.render-pipelines.core).");

                        return action switch
                        {
                            "volume_create" => VolumeOps.CreateVolume(@params),
                            "volume_add_effect" => VolumeOps.AddEffect(@params),
                            "volume_set_effect" => VolumeOps.SetEffect(@params),
                            "volume_remove_effect" => VolumeOps.RemoveEffect(@params),
                            "volume_get_info" => VolumeOps.GetInfo(@params),
                            "volume_set_properties" => VolumeOps.SetProperties(@params),
                            "volume_list_effects" => VolumeOps.ListEffects(@params),
                            "volume_create_profile" => VolumeOps.CreateProfile(@params),
                            _ => new ErrorResponse($"Unknown volume action: '{action}'")
                        };
                    }

                    // --- Bake actions (always available, Edit mode only) ---
                    case "bake_start":
                        return LightBakingOps.StartBake(@params);
                    case "bake_cancel":
                        return LightBakingOps.CancelBake(@params);
                    case "bake_status":
                        return LightBakingOps.GetStatus(@params);
                    case "bake_clear":
                        return LightBakingOps.ClearBake(@params);
                    case "bake_reflection_probe":
                        return LightBakingOps.BakeReflectionProbe(@params);
                    case "bake_get_settings":
                        return LightBakingOps.GetSettings(@params);
                    case "bake_set_settings":
                        return LightBakingOps.SetSettings(@params);
                    case "bake_create_light_probe_group":
                        return LightBakingOps.CreateLightProbeGroup(@params);
                    case "bake_create_reflection_probe":
                        return LightBakingOps.CreateReflectionProbe(@params);
                    case "bake_set_probe_positions":
                        return LightBakingOps.SetProbePositions(@params);

                    // --- Stats actions (always available) ---
                    case "stats_get":
                        return RenderingStatsOps.GetStats(@params);
                    case "stats_list_counters":
                        return RenderingStatsOps.ListCounters(@params);
                    case "stats_set_scene_debug":
                        return RenderingStatsOps.SetSceneDebugMode(@params);
                    case "stats_get_memory":
                        return RenderingStatsOps.GetMemory(@params);

                    // --- Pipeline actions (always available) ---
                    case "pipeline_get_info":
                        return RenderPipelineOps.GetInfo(@params);
                    case "pipeline_set_quality":
                        return RenderPipelineOps.SetQuality(@params);
                    case "pipeline_get_settings":
                        return RenderPipelineOps.GetSettings(@params);
                    case "pipeline_set_settings":
                        return RenderPipelineOps.SetSettings(@params);

                    // --- Renderer feature actions (URP only) ---
                    case "feature_list":
                    case "feature_add":
                    case "feature_remove":
                    case "feature_configure":
                    case "feature_toggle":
                    case "feature_reorder":
                    {
                        if (!GraphicsHelpers.HasURP)
                            return new ErrorResponse("Renderer features require URP (Universal Render Pipeline).");

                        return action switch
                        {
                            "feature_list" => RendererFeatureOps.ListFeatures(@params),
                            "feature_add" => RendererFeatureOps.AddFeature(@params),
                            "feature_remove" => RendererFeatureOps.RemoveFeature(@params),
                            "feature_configure" => RendererFeatureOps.ConfigureFeature(@params),
                            "feature_toggle" => RendererFeatureOps.ToggleFeature(@params),
                            "feature_reorder" => RendererFeatureOps.ReorderFeatures(@params),
                            _ => new ErrorResponse($"Unknown feature action: '{action}'")
                        };
                    }

                    // --- Skybox / Environment actions (always available) ---
                    case "skybox_get":
                        return SkyboxOps.GetEnvironment(@params);
                    case "skybox_set_material":
                        return SkyboxOps.SetMaterial(@params);
                    case "skybox_set_properties":
                        return SkyboxOps.SetMaterialProperties(@params);
                    case "skybox_set_ambient":
                        return SkyboxOps.SetAmbient(@params);
                    case "skybox_set_fog":
                        return SkyboxOps.SetFog(@params);
                    case "skybox_set_reflection":
                        return SkyboxOps.SetReflection(@params);
                    case "skybox_set_sun":
                        return SkyboxOps.SetSun(@params);

                    default:
                        return new ErrorResponse(
                            $"Unknown action: '{action}'. Valid actions: ping, "
                            + "volume_create, volume_add_effect, volume_set_effect, volume_remove_effect, "
                            + "volume_get_info, volume_set_properties, volume_list_effects, volume_create_profile, "
                            + "bake_start, bake_cancel, bake_status, bake_clear, bake_reflection_probe, "
                            + "bake_get_settings, bake_set_settings, bake_create_light_probe_group, "
                            + "bake_create_reflection_probe, bake_set_probe_positions, "
                            + "stats_get, stats_list_counters, stats_set_scene_debug, stats_get_memory, "
                            + "pipeline_get_info, pipeline_set_quality, pipeline_get_settings, pipeline_set_settings, "
                            + "feature_list, feature_add, feature_remove, feature_configure, feature_toggle, feature_reorder, "
                            + "skybox_get, skybox_set_material, skybox_set_properties, skybox_set_ambient, "
                            + "skybox_set_fog, skybox_set_reflection, skybox_set_sun.");
                }
            }
            catch (Exception ex)
            {
                McpLog.Error($"[ManageGraphics] Action '{action}' failed: {ex}");
                return new ErrorResponse($"Error in action '{action}': {ex.Message}");
            }
        }
    }
}
