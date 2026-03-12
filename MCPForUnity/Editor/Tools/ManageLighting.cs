using System;
using System.Collections.Generic;
using MCPForUnity.Editor.Helpers;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace MCPForUnity.Editor.Tools
{
    /// <summary>
    /// MCP tool for Unity lighting management.
    /// Actions: list_lights, get_light, set_light, bake, cancel_bake, get_bake_status,
    ///          list_probes, get_probe, get_environment, set_environment, get_lightmap_settings.
    /// </summary>
    [McpForUnityTool("manage_lighting", AutoRegister = true)]
    public static class ManageLighting
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
                    "list_lights"          => ListLights(p),
                    "get_light"            => GetLight(p),
                    "set_light"            => SetLight(p),
                    "bake"                 => Bake(),
                    "cancel_bake"          => CancelBake(),
                    "get_bake_status"      => GetBakeStatus(),
                    "list_probes"          => ListProbes(p),
                    "get_probe"            => GetProbe(p),
                    "get_environment"      => GetEnvironment(),
                    "set_environment"      => SetEnvironment(p),
                    "get_lightmap_settings" => GetLightmapSettings(),
                    _ => new ErrorResponse(
                        $"Unknown action: '{action}'. Supported: list_lights, get_light, set_light, " +
                        "bake, cancel_bake, get_bake_status, list_probes, get_probe, " +
                        "get_environment, set_environment, get_lightmap_settings")
                };
            }
            catch (Exception e)
            {
                McpLog.Error($"[ManageLighting] Action '{action}' failed: {e}");
                return new ErrorResponse($"Internal error: {e.Message}");
            }
        }

        #region Light Operations

        private static object ListLights(ToolParams p)
        {
            var lights = UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.InstanceID);
            string typeFilter = p.Get("type_filter");

            var pagination = PaginationRequest.FromParams(
                new JObject { ["page_size"] = p.GetInt("page_size"), ["cursor"] = p.GetInt("cursor") });

            var allItems = new List<Dictionary<string, object>>();
            foreach (var light in lights)
            {
                if (!string.IsNullOrEmpty(typeFilter) &&
                    !string.Equals(light.type.ToString(), typeFilter, StringComparison.OrdinalIgnoreCase))
                    continue;

                allItems.Add(new Dictionary<string, object>
                {
                    ["name"] = light.gameObject.name,
                    ["instance_id"] = light.gameObject.GetInstanceID(),
                    ["type"] = light.type.ToString(),
                    ["color"] = FormatColor(light.color),
                    ["intensity"] = Math.Round(light.intensity, 4),
                    ["range"] = Math.Round(light.range, 4),
                    ["shadows"] = light.shadows.ToString(),
                    ["enabled"] = light.enabled,
                });
            }

            var page = PaginationResponse<Dictionary<string, object>>.Create(allItems, pagination);
            return new SuccessResponse($"Found {allItems.Count} Light component(s).", page);
        }

        private static object GetLight(ToolParams p)
        {
            var targetResult = p.GetRequired("target");
            if (!targetResult.IsSuccess)
                return new ErrorResponse(targetResult.ErrorMessage);

            var go = ObjectResolver.ResolveGameObject(new JValue(targetResult.Value));
            if (go == null)
                return new ErrorResponse($"GameObject '{targetResult.Value}' not found.");

            var light = go.GetComponent<Light>();
            if (light == null)
                return new ErrorResponse($"No Light on '{go.name}'.");

            var data = new Dictionary<string, object>
            {
                ["name"] = go.name,
                ["instance_id"] = go.GetInstanceID(),
                ["type"] = light.type.ToString(),
                ["color"] = FormatColor(light.color),
                ["intensity"] = Math.Round(light.intensity, 4),
                ["range"] = Math.Round(light.range, 4),
                ["spot_angle"] = Math.Round(light.spotAngle, 4),
                ["inner_spot_angle"] = Math.Round(light.innerSpotAngle, 4),
                ["shadows"] = light.shadows.ToString(),
                ["shadow_strength"] = Math.Round(light.shadowStrength, 4),
                ["render_mode"] = light.renderMode.ToString(),
                ["culling_mask"] = light.cullingMask,
                ["enabled"] = light.enabled,
                ["baked_index"] = light.bakingOutput.lightmapBakeType.ToString(),
            };

            return new SuccessResponse($"Light on '{go.name}'.", data);
        }

        private static object SetLight(ToolParams p)
        {
            var targetResult = p.GetRequired("target");
            if (!targetResult.IsSuccess)
                return new ErrorResponse(targetResult.ErrorMessage);

            var go = ObjectResolver.ResolveGameObject(new JValue(targetResult.Value));
            if (go == null)
                return new ErrorResponse($"GameObject '{targetResult.Value}' not found.");

            var light = go.GetComponent<Light>();
            if (light == null)
                return new ErrorResponse($"No Light on '{go.name}'.");

            var props = ParseProps(p);
            if (props == null) return new ErrorResponse("'properties' parameter is required (valid JSON).");

            Undo.RecordObject(light, "MCP SetLight");

            if (props["color"] != null)
            {
                var col = VectorParsing.ParseColor(props["color"]);
                if (col.HasValue) light.color = col.Value;
            }
            if (props["intensity"] != null) light.intensity = props["intensity"].ToObject<float>();
            if (props["range"] != null) light.range = props["range"].ToObject<float>();
            if (props["spot_angle"] != null) light.spotAngle = props["spot_angle"].ToObject<float>();
            if (props["shadows"] != null)
            {
                if (Enum.TryParse<LightShadows>(props["shadows"].ToString(), true, out var shadowMode))
                    light.shadows = shadowMode;
            }
            if (props["enabled"] != null) light.enabled = props["enabled"].ToObject<bool>();

            EditorUtility.SetDirty(light);
            return new SuccessResponse($"Light on '{go.name}' updated.");
        }

        #endregion

        #region Lightmapping

        private static object Bake()
        {
            if (Lightmapping.isRunning)
                return new ErrorResponse("Lightmap bake is already in progress.");

            Lightmapping.BakeAsync();
            return new SuccessResponse("Lightmap bake started (async). Use get_bake_status to poll progress.");
        }

        private static object CancelBake()
        {
            if (!Lightmapping.isRunning)
                return new SuccessResponse("No bake in progress.");

            Lightmapping.Cancel();
            return new SuccessResponse("Lightmap bake cancelled.");
        }

        private static object GetBakeStatus()
        {
            return new SuccessResponse("Bake status.", new Dictionary<string, object>
            {
                ["is_running"] = Lightmapping.isRunning,
                ["build_progress"] = Math.Round(Lightmapping.buildProgress, 4),
            });
        }

        #endregion

        #region Probes

        private static object ListProbes(ToolParams p)
        {
            var pagination = PaginationRequest.FromParams(
                new JObject { ["page_size"] = p.GetInt("page_size"), ["cursor"] = p.GetInt("cursor") });

            var allItems = new List<Dictionary<string, object>>();

            // Light probe groups
            var lpgs = UnityEngine.Object.FindObjectsByType<LightProbeGroup>(FindObjectsSortMode.InstanceID);
            foreach (var lpg in lpgs)
            {
                allItems.Add(new Dictionary<string, object>
                {
                    ["name"] = lpg.gameObject.name,
                    ["instance_id"] = lpg.gameObject.GetInstanceID(),
                    ["type"] = "LightProbeGroup",
                    ["probe_count"] = lpg.probePositions.Length,
                });
            }

            // Reflection probes
            var rps = UnityEngine.Object.FindObjectsByType<ReflectionProbe>(FindObjectsSortMode.InstanceID);
            foreach (var rp in rps)
            {
                allItems.Add(new Dictionary<string, object>
                {
                    ["name"] = rp.gameObject.name,
                    ["instance_id"] = rp.gameObject.GetInstanceID(),
                    ["type"] = "ReflectionProbe",
                    ["mode"] = rp.mode.ToString(),
                    ["resolution"] = rp.resolution,
                    ["box_projection"] = rp.boxProjection,
                });
            }

            var page = PaginationResponse<Dictionary<string, object>>.Create(allItems, pagination);
            return new SuccessResponse($"Found {allItems.Count} probe(s).", page);
        }

        private static object GetProbe(ToolParams p)
        {
            var targetResult = p.GetRequired("target");
            if (!targetResult.IsSuccess)
                return new ErrorResponse(targetResult.ErrorMessage);

            var go = ObjectResolver.ResolveGameObject(new JValue(targetResult.Value));
            if (go == null)
                return new ErrorResponse($"GameObject '{targetResult.Value}' not found.");

            var lpg = go.GetComponent<LightProbeGroup>();
            if (lpg != null)
            {
                return new SuccessResponse($"LightProbeGroup on '{go.name}'.", new Dictionary<string, object>
                {
                    ["name"] = go.name,
                    ["type"] = "LightProbeGroup",
                    ["probe_count"] = lpg.probePositions.Length,
                });
            }

            var rp = go.GetComponent<ReflectionProbe>();
            if (rp != null)
            {
                return new SuccessResponse($"ReflectionProbe on '{go.name}'.", new Dictionary<string, object>
                {
                    ["name"] = go.name,
                    ["type"] = "ReflectionProbe",
                    ["mode"] = rp.mode.ToString(),
                    ["resolution"] = rp.resolution,
                    ["box_projection"] = rp.boxProjection,
                    ["importance"] = rp.importance,
                    ["intensity"] = Math.Round(rp.intensity, 4),
                    ["near_clip"] = Math.Round(rp.nearClipPlane, 4),
                    ["far_clip"] = Math.Round(rp.farClipPlane, 4),
                    ["hdr"] = rp.hdr,
                    ["bounds_center"] = $"{rp.center.x:F3},{rp.center.y:F3},{rp.center.z:F3}",
                    ["bounds_size"] = $"{rp.size.x:F3},{rp.size.y:F3},{rp.size.z:F3}",
                });
            }

            return new ErrorResponse($"No probe component found on '{go.name}'.");
        }

        #endregion

        #region Environment / RenderSettings

        private static object GetEnvironment()
        {
            var skybox = RenderSettings.skybox;
            return new SuccessResponse("Render environment settings.", new Dictionary<string, object>
            {
                ["ambient_mode"] = RenderSettings.ambientMode.ToString(),
                ["ambient_light"] = FormatColor(RenderSettings.ambientLight),
                ["ambient_sky_color"] = FormatColor(RenderSettings.ambientSkyColor),
                ["ambient_equator_color"] = FormatColor(RenderSettings.ambientEquatorColor),
                ["ambient_ground_color"] = FormatColor(RenderSettings.ambientGroundColor),
                ["ambient_intensity"] = Math.Round(RenderSettings.ambientIntensity, 4),
                ["fog"] = RenderSettings.fog,
                ["fog_color"] = FormatColor(RenderSettings.fogColor),
                ["fog_mode"] = RenderSettings.fogMode.ToString(),
                ["fog_density"] = Math.Round(RenderSettings.fogDensity, 6),
                ["fog_start"] = Math.Round(RenderSettings.fogStartDistance, 4),
                ["fog_end"] = Math.Round(RenderSettings.fogEndDistance, 4),
                ["skybox_material"] = skybox != null ? skybox.name : null,
                ["sun_source"] = RenderSettings.sun != null ? RenderSettings.sun.gameObject.name : null,
            });
        }

        private static object SetEnvironment(ToolParams p)
        {
            var props = ParseProps(p);
            if (props == null) return new ErrorResponse("'properties' parameter is required (valid JSON).");

            if (props["fog"] != null)
                RenderSettings.fog = props["fog"].ToObject<bool>();
            if (props["fog_color"] != null)
            {
                var col = VectorParsing.ParseColor(props["fog_color"]);
                if (col.HasValue) RenderSettings.fogColor = col.Value;
            }
            if (props["fog_density"] != null)
                RenderSettings.fogDensity = props["fog_density"].ToObject<float>();
            if (props["ambient_intensity"] != null)
                RenderSettings.ambientIntensity = props["ambient_intensity"].ToObject<float>();
            if (props["ambient_light"] != null)
            {
                var col = VectorParsing.ParseColor(props["ambient_light"]);
                if (col.HasValue) RenderSettings.ambientLight = col.Value;
            }

            return new SuccessResponse("Environment settings updated.");
        }

        #endregion

        #region Lightmap Settings

        private static object GetLightmapSettings()
        {
            return new SuccessResponse("Lightmap settings.", new Dictionary<string, object>
            {
                ["lightmapper"] = Lightmapping.lightingSettings != null
                    ? Lightmapping.lightingSettings.lightmapper.ToString()
                    : "Unknown",
                ["lightmap_resolution"] = Lightmapping.lightingSettings != null
                    ? Math.Round(Lightmapping.lightingSettings.lightmapResolution, 2)
                    : 0,
                ["lightmap_count"] = LightmapSettings.lightmaps.Length,
            });
        }

        #endregion

        #region Helpers

        private static string FormatColor(Color c) =>
            $"{c.r:F3},{c.g:F3},{c.b:F3},{c.a:F3}";

        private static JObject ParseProps(ToolParams p)
        {
            var propsToken = p.GetRaw("properties");
            if (propsToken == null) return null;

            if (propsToken.Type == JTokenType.String)
            {
                try { return JObject.Parse(propsToken.ToString()); }
                catch { return null; }
            }
            return propsToken as JObject;
        }

        #endregion
    }
}
