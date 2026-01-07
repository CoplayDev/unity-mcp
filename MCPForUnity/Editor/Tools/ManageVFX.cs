using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using MCPForUnity.Editor.Helpers;
using UnityEngine;
using UnityEditor;

#if UNITY_VFX_GRAPH //Please enable the symbol in the project settings for VisualEffectGraph to work
using UnityEngine.VFX;
#endif

namespace MCPForUnity.Editor.Tools
{
    /// <summary>
    /// Tool for managing Unity VFX components:
    /// - ParticleSystem (legacy particle effects)
    /// - Visual Effect Graph (modern GPU particles, currently only support HDRP, other SRPs may not work)
    /// - LineRenderer (lines, bezier curves, shapes)
    /// - TrailRenderer (motion trails)
    /// - More to come based on demand and feedback!
    /// </summary>
    [McpForUnityTool("manage_vfx", AutoRegister = false)]
    public static class ManageVFX
    {
        public static object HandleCommand(JObject @params)
        {
            string action = @params["action"]?.ToString();
            if (string.IsNullOrEmpty(action))
            {
                return new { success = false, message = "Action is required" };
            }

            try
            {
                string actionLower = action.ToLowerInvariant();

                // Route to appropriate handler based on action prefix
                if (actionLower == "ping")
                {
                    return new { success = true, tool = "manage_vfx", components = new[] { "ParticleSystem", "VisualEffect", "LineRenderer", "TrailRenderer" } };
                }

                // ParticleSystem actions (particle_*)
                if (actionLower.StartsWith("particle_"))
                {
                    return HandleParticleSystemAction(@params, actionLower.Substring(9));
                }

                // VFX Graph actions (vfx_*)
                if (actionLower.StartsWith("vfx_"))
                {
                    return HandleVFXGraphAction(@params, actionLower.Substring(4));
                }

                // LineRenderer actions (line_*)
                if (actionLower.StartsWith("line_"))
                {
                    return HandleLineRendererAction(@params, actionLower.Substring(5));
                }

                // TrailRenderer actions (trail_*)
                if (actionLower.StartsWith("trail_"))
                {
                    return HandleTrailRendererAction(@params, actionLower.Substring(6));
                }

                return new { success = false, message = $"Unknown action: {action}. Actions must be prefixed with: particle_, vfx_, line_, or trail_" };
            }
            catch (Exception ex)
            {
                return new { success = false, message = ex.Message, stackTrace = ex.StackTrace };
            }
        }

        #region Common Helpers

        // Parsing delegates for use with RendererHelpers
        private static Color ParseColor(JToken token) => VectorParsing.ParseColorOrDefault(token);
        private static Vector3 ParseVector3(JToken token) => VectorParsing.ParseVector3OrDefault(token);
        private static Vector4 ParseVector4(JToken token) => VectorParsing.ParseVector4OrDefault(token);
        private static Gradient ParseGradient(JToken token) => VectorParsing.ParseGradientOrDefault(token);
        private static AnimationCurve ParseAnimationCurve(JToken token, float defaultValue = 1f) 
            => VectorParsing.ParseAnimationCurveOrDefault(token, defaultValue);

        // Object resolution - delegates to ObjectResolver
        private static GameObject FindTargetGameObject(JObject @params) 
            => ObjectResolver.ResolveGameObject(@params["target"], @params["searchMethod"]?.ToString());
        private static Material FindMaterialByPath(string path) 
            => ObjectResolver.ResolveMaterial(path);

        #endregion

        // ==================== PARTICLE SYSTEM ====================
        #region ParticleSystem

        private static object HandleParticleSystemAction(JObject @params, string action)
        {
            switch (action)
            {
                case "get_info": return ParticleGetInfo(@params);
                case "set_main": return ParticleSetMain(@params);
                case "set_emission": return ParticleSetEmission(@params);
                case "set_shape": return ParticleSetShape(@params);
                case "set_color_over_lifetime": return ParticleSetColorOverLifetime(@params);
                case "set_size_over_lifetime": return ParticleSetSizeOverLifetime(@params);
                case "set_velocity_over_lifetime": return ParticleSetVelocityOverLifetime(@params);
                case "set_noise": return ParticleSetNoise(@params);
                case "set_renderer": return ParticleSetRenderer(@params);
                case "enable_module": return ParticleEnableModule(@params);
                case "play": return ParticleControl(@params, "play");
                case "stop": return ParticleControl(@params, "stop");
                case "pause": return ParticleControl(@params, "pause");
                case "restart": return ParticleControl(@params, "restart");
                case "clear": return ParticleControl(@params, "clear");
                case "add_burst": return ParticleAddBurst(@params);
                case "clear_bursts": return ParticleClearBursts(@params);
                default:
                    return new { success = false, message = $"Unknown particle action: {action}. Valid: get_info, set_main, set_emission, set_shape, set_color_over_lifetime, set_size_over_lifetime, set_velocity_over_lifetime, set_noise, set_renderer, enable_module, play, stop, pause, restart, clear, add_burst, clear_bursts" };
            }
        }

        private static ParticleSystem FindParticleSystem(JObject @params)
        {
            GameObject go = FindTargetGameObject(@params);
            return go?.GetComponent<ParticleSystem>();
        }

        private static ParticleSystem.MinMaxCurve ParseMinMaxCurve(JToken token, float defaultValue = 1f)
        {
            if (token == null)
                return new ParticleSystem.MinMaxCurve(defaultValue);

            if (token.Type == JTokenType.Float || token.Type == JTokenType.Integer)
            {
                return new ParticleSystem.MinMaxCurve(token.ToObject<float>());
            }

            if (token is JObject obj)
            {
                string mode = obj["mode"]?.ToString()?.ToLowerInvariant() ?? "constant";

                switch (mode)
                {
                    case "constant":
                        float constant = obj["value"]?.ToObject<float>() ?? defaultValue;
                        return new ParticleSystem.MinMaxCurve(constant);

                    case "random_between_constants":
                    case "two_constants":
                        float min = obj["min"]?.ToObject<float>() ?? 0f;
                        float max = obj["max"]?.ToObject<float>() ?? 1f;
                        return new ParticleSystem.MinMaxCurve(min, max);

                    case "curve":
                        AnimationCurve curve = ParseAnimationCurve(obj, defaultValue);
                        return new ParticleSystem.MinMaxCurve(obj["multiplier"]?.ToObject<float>() ?? 1f, curve);

                    default:
                        return new ParticleSystem.MinMaxCurve(defaultValue);
                }
            }

            return new ParticleSystem.MinMaxCurve(defaultValue);
        }

        private static ParticleSystem.MinMaxGradient ParseMinMaxGradient(JToken token)
        {
            if (token == null)
                return new ParticleSystem.MinMaxGradient(Color.white);

            if (token is JArray arr && arr.Count >= 3)
            {
                return new ParticleSystem.MinMaxGradient(ParseColor(arr));
            }

            if (token is JObject obj)
            {
                string mode = obj["mode"]?.ToString()?.ToLowerInvariant() ?? "color";

                switch (mode)
                {
                    case "color":
                        return new ParticleSystem.MinMaxGradient(ParseColor(obj["color"]));

                    case "two_colors":
                        Color colorMin = ParseColor(obj["colorMin"]);
                        Color colorMax = ParseColor(obj["colorMax"]);
                        return new ParticleSystem.MinMaxGradient(colorMin, colorMax);

                    case "gradient":
                        return new ParticleSystem.MinMaxGradient(ParseGradient(obj));

                    default:
                        return new ParticleSystem.MinMaxGradient(Color.white);
                }
            }

            return new ParticleSystem.MinMaxGradient(Color.white);
        }

        private static object ParticleGetInfo(JObject @params)
        {
            ParticleSystem ps = FindParticleSystem(@params);
            if (ps == null)
            {
                return new { success = false, message = "ParticleSystem not found" };
            }

            var main = ps.main;
            var emission = ps.emission;
            var shape = ps.shape;
            var renderer = ps.GetComponent<ParticleSystemRenderer>();

            return new
            {
                success = true,
                data = new
                {
                    gameObject = ps.gameObject.name,
                    isPlaying = ps.isPlaying,
                    isPaused = ps.isPaused,
                    particleCount = ps.particleCount,
                    main = new
                    {
                        duration = main.duration,
                        looping = main.loop,
                        startLifetime = main.startLifetime.constant,
                        startSpeed = main.startSpeed.constant,
                        startSize = main.startSize.constant,
                        gravityModifier = main.gravityModifier.constant,
                        simulationSpace = main.simulationSpace.ToString(),
                        maxParticles = main.maxParticles
                    },
                    emission = new
                    {
                        enabled = emission.enabled,
                        rateOverTime = emission.rateOverTime.constant,
                        burstCount = emission.burstCount
                    },
                    shape = new
                    {
                        enabled = shape.enabled,
                        shapeType = shape.shapeType.ToString(),
                        radius = shape.radius,
                        angle = shape.angle
                    },
                    renderer = renderer != null ? new { 
                        renderMode = renderer.renderMode.ToString(), 
                        sortMode = renderer.sortMode.ToString(),
                        material = renderer.sharedMaterial?.name,
                        trailMaterial = renderer.trailMaterial?.name,
                        minParticleSize = renderer.minParticleSize,
                        maxParticleSize = renderer.maxParticleSize,
                        // Shadows & lighting
                        shadowCastingMode = renderer.shadowCastingMode.ToString(),
                        receiveShadows = renderer.receiveShadows,
                        lightProbeUsage = renderer.lightProbeUsage.ToString(),
                        reflectionProbeUsage = renderer.reflectionProbeUsage.ToString(),
                        // Sorting
                        sortingOrder = renderer.sortingOrder,
                        sortingLayerName = renderer.sortingLayerName,
                        renderingLayerMask = renderer.renderingLayerMask
                    } : null
                }
            };
        }

        private static object ParticleSetMain(JObject @params)
        {
            ParticleSystem ps = FindParticleSystem(@params);
            if (ps == null) return new { success = false, message = "ParticleSystem not found" };

            Undo.RecordObject(ps, "Set ParticleSystem Main");
            var main = ps.main;
            var changes = new List<string>();

            if (@params["duration"] != null) { main.duration = @params["duration"].ToObject<float>(); changes.Add("duration"); }
            if (@params["looping"] != null) { main.loop = @params["looping"].ToObject<bool>(); changes.Add("looping"); }
            if (@params["prewarm"] != null) { main.prewarm = @params["prewarm"].ToObject<bool>(); changes.Add("prewarm"); }
            if (@params["startDelay"] != null) { main.startDelay = ParseMinMaxCurve(@params["startDelay"], 0f); changes.Add("startDelay"); }
            if (@params["startLifetime"] != null) { main.startLifetime = ParseMinMaxCurve(@params["startLifetime"], 5f); changes.Add("startLifetime"); }
            if (@params["startSpeed"] != null) { main.startSpeed = ParseMinMaxCurve(@params["startSpeed"], 5f); changes.Add("startSpeed"); }
            if (@params["startSize"] != null) { main.startSize = ParseMinMaxCurve(@params["startSize"], 1f); changes.Add("startSize"); }
            if (@params["startRotation"] != null) { main.startRotation = ParseMinMaxCurve(@params["startRotation"], 0f); changes.Add("startRotation"); }
            if (@params["startColor"] != null) { main.startColor = ParseMinMaxGradient(@params["startColor"]); changes.Add("startColor"); }
            if (@params["gravityModifier"] != null) { main.gravityModifier = ParseMinMaxCurve(@params["gravityModifier"], 0f); changes.Add("gravityModifier"); }
            if (@params["simulationSpace"] != null && Enum.TryParse<ParticleSystemSimulationSpace>(@params["simulationSpace"].ToString(), true, out var simSpace)) { main.simulationSpace = simSpace; changes.Add("simulationSpace"); }
            if (@params["scalingMode"] != null && Enum.TryParse<ParticleSystemScalingMode>(@params["scalingMode"].ToString(), true, out var scaleMode)) { main.scalingMode = scaleMode; changes.Add("scalingMode"); }
            if (@params["playOnAwake"] != null) { main.playOnAwake = @params["playOnAwake"].ToObject<bool>(); changes.Add("playOnAwake"); }
            if (@params["maxParticles"] != null) { main.maxParticles = @params["maxParticles"].ToObject<int>(); changes.Add("maxParticles"); }

            EditorUtility.SetDirty(ps);
            return new { success = true, message = $"Updated: {string.Join(", ", changes)}" };
        }

        private static object ParticleSetEmission(JObject @params)
        {
            ParticleSystem ps = FindParticleSystem(@params);
            if (ps == null) return new { success = false, message = "ParticleSystem not found" };

            Undo.RecordObject(ps, "Set ParticleSystem Emission");
            var emission = ps.emission;
            var changes = new List<string>();

            if (@params["enabled"] != null) { emission.enabled = @params["enabled"].ToObject<bool>(); changes.Add("enabled"); }
            if (@params["rateOverTime"] != null) { emission.rateOverTime = ParseMinMaxCurve(@params["rateOverTime"], 10f); changes.Add("rateOverTime"); }
            if (@params["rateOverDistance"] != null) { emission.rateOverDistance = ParseMinMaxCurve(@params["rateOverDistance"], 0f); changes.Add("rateOverDistance"); }

            EditorUtility.SetDirty(ps);
            return new { success = true, message = $"Updated emission: {string.Join(", ", changes)}" };
        }

        private static object ParticleSetShape(JObject @params)
        {
            ParticleSystem ps = FindParticleSystem(@params);
            if (ps == null) return new { success = false, message = "ParticleSystem not found" };

            Undo.RecordObject(ps, "Set ParticleSystem Shape");
            var shape = ps.shape;
            var changes = new List<string>();

            if (@params["enabled"] != null) { shape.enabled = @params["enabled"].ToObject<bool>(); changes.Add("enabled"); }
            if (@params["shapeType"] != null && Enum.TryParse<ParticleSystemShapeType>(@params["shapeType"].ToString(), true, out var shapeType)) { shape.shapeType = shapeType; changes.Add("shapeType"); }
            if (@params["radius"] != null) { shape.radius = @params["radius"].ToObject<float>(); changes.Add("radius"); }
            if (@params["radiusThickness"] != null) { shape.radiusThickness = @params["radiusThickness"].ToObject<float>(); changes.Add("radiusThickness"); }
            if (@params["angle"] != null) { shape.angle = @params["angle"].ToObject<float>(); changes.Add("angle"); }
            if (@params["arc"] != null) { shape.arc = @params["arc"].ToObject<float>(); changes.Add("arc"); }
            if (@params["position"] != null) { shape.position = ParseVector3(@params["position"]); changes.Add("position"); }
            if (@params["rotation"] != null) { shape.rotation = ParseVector3(@params["rotation"]); changes.Add("rotation"); }
            if (@params["scale"] != null) { shape.scale = ParseVector3(@params["scale"]); changes.Add("scale"); }

            EditorUtility.SetDirty(ps);
            return new { success = true, message = $"Updated shape: {string.Join(", ", changes)}" };
        }

        private static object ParticleSetColorOverLifetime(JObject @params)
        {
            ParticleSystem ps = FindParticleSystem(@params);
            if (ps == null) return new { success = false, message = "ParticleSystem not found" };

            Undo.RecordObject(ps, "Set ParticleSystem Color Over Lifetime");
            var col = ps.colorOverLifetime;
            var changes = new List<string>();

            if (@params["enabled"] != null) { col.enabled = @params["enabled"].ToObject<bool>(); changes.Add("enabled"); }
            if (@params["color"] != null) { col.color = ParseMinMaxGradient(@params["color"]); changes.Add("color"); }

            EditorUtility.SetDirty(ps);
            return new { success = true, message = $"Updated: {string.Join(", ", changes)}" };
        }

        private static object ParticleSetSizeOverLifetime(JObject @params)
        {
            ParticleSystem ps = FindParticleSystem(@params);
            if (ps == null) return new { success = false, message = "ParticleSystem not found" };

            Undo.RecordObject(ps, "Set ParticleSystem Size Over Lifetime");
            var sol = ps.sizeOverLifetime;
            var changes = new List<string>();

            // Auto-enable module if size properties are being set (unless explicitly disabled)
            bool hasSizeProperty = @params["size"] != null || @params["sizeX"] != null || 
                                   @params["sizeY"] != null || @params["sizeZ"] != null;
            if (hasSizeProperty && @params["enabled"] == null && !sol.enabled)
            {
                sol.enabled = true;
                changes.Add("enabled");
            }
            else if (@params["enabled"] != null)
            {
                sol.enabled = @params["enabled"].ToObject<bool>();
                changes.Add("enabled");
            }

            if (@params["separateAxes"] != null) { sol.separateAxes = @params["separateAxes"].ToObject<bool>(); changes.Add("separateAxes"); }
            if (@params["size"] != null) { sol.size = ParseMinMaxCurve(@params["size"], 1f); changes.Add("size"); }
            if (@params["sizeX"] != null) { sol.x = ParseMinMaxCurve(@params["sizeX"], 1f); changes.Add("sizeX"); }
            if (@params["sizeY"] != null) { sol.y = ParseMinMaxCurve(@params["sizeY"], 1f); changes.Add("sizeY"); }
            if (@params["sizeZ"] != null) { sol.z = ParseMinMaxCurve(@params["sizeZ"], 1f); changes.Add("sizeZ"); }

            EditorUtility.SetDirty(ps);
            return new { success = true, message = $"Updated: {string.Join(", ", changes)}" };
        }

        private static object ParticleSetVelocityOverLifetime(JObject @params)
        {
            ParticleSystem ps = FindParticleSystem(@params);
            if (ps == null) return new { success = false, message = "ParticleSystem not found" };

            Undo.RecordObject(ps, "Set ParticleSystem Velocity Over Lifetime");
            var vol = ps.velocityOverLifetime;
            var changes = new List<string>();

            if (@params["enabled"] != null) { vol.enabled = @params["enabled"].ToObject<bool>(); changes.Add("enabled"); }
            if (@params["space"] != null && Enum.TryParse<ParticleSystemSimulationSpace>(@params["space"].ToString(), true, out var space)) { vol.space = space; changes.Add("space"); }
            if (@params["x"] != null) { vol.x = ParseMinMaxCurve(@params["x"], 0f); changes.Add("x"); }
            if (@params["y"] != null) { vol.y = ParseMinMaxCurve(@params["y"], 0f); changes.Add("y"); }
            if (@params["z"] != null) { vol.z = ParseMinMaxCurve(@params["z"], 0f); changes.Add("z"); }
            if (@params["speedModifier"] != null) { vol.speedModifier = ParseMinMaxCurve(@params["speedModifier"], 1f); changes.Add("speedModifier"); }

            EditorUtility.SetDirty(ps);
            return new { success = true, message = $"Updated: {string.Join(", ", changes)}" };
        }

        private static object ParticleSetNoise(JObject @params)
        {
            ParticleSystem ps = FindParticleSystem(@params);
            if (ps == null) return new { success = false, message = "ParticleSystem not found" };

            Undo.RecordObject(ps, "Set ParticleSystem Noise");
            var noise = ps.noise;
            var changes = new List<string>();

            if (@params["enabled"] != null) { noise.enabled = @params["enabled"].ToObject<bool>(); changes.Add("enabled"); }
            if (@params["strength"] != null) { noise.strength = ParseMinMaxCurve(@params["strength"], 1f); changes.Add("strength"); }
            if (@params["frequency"] != null) { noise.frequency = @params["frequency"].ToObject<float>(); changes.Add("frequency"); }
            if (@params["scrollSpeed"] != null) { noise.scrollSpeed = ParseMinMaxCurve(@params["scrollSpeed"], 0f); changes.Add("scrollSpeed"); }
            if (@params["damping"] != null) { noise.damping = @params["damping"].ToObject<bool>(); changes.Add("damping"); }
            if (@params["octaveCount"] != null) { noise.octaveCount = @params["octaveCount"].ToObject<int>(); changes.Add("octaveCount"); }
            if (@params["quality"] != null && Enum.TryParse<ParticleSystemNoiseQuality>(@params["quality"].ToString(), true, out var quality)) { noise.quality = quality; changes.Add("quality"); }

            EditorUtility.SetDirty(ps);
            return new { success = true, message = $"Updated noise: {string.Join(", ", changes)}" };
        }

        private static object ParticleSetRenderer(JObject @params)
        {
            ParticleSystem ps = FindParticleSystem(@params);
            if (ps == null) return new { success = false, message = "ParticleSystem not found" };

            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            if (renderer == null) return new { success = false, message = "ParticleSystemRenderer not found" };

            Undo.RecordObject(renderer, "Set ParticleSystem Renderer");
            var changes = new List<string>();

            // ParticleSystem-specific render modes
            if (@params["renderMode"] != null && Enum.TryParse<ParticleSystemRenderMode>(@params["renderMode"].ToString(), true, out var renderMode)) { renderer.renderMode = renderMode; changes.Add("renderMode"); }
            if (@params["sortMode"] != null && Enum.TryParse<ParticleSystemSortMode>(@params["sortMode"].ToString(), true, out var sortMode)) { renderer.sortMode = sortMode; changes.Add("sortMode"); }
            
            // Particle size limits
            if (@params["minParticleSize"] != null) { renderer.minParticleSize = @params["minParticleSize"].ToObject<float>(); changes.Add("minParticleSize"); }
            if (@params["maxParticleSize"] != null) { renderer.maxParticleSize = @params["maxParticleSize"].ToObject<float>(); changes.Add("maxParticleSize"); }
            
            // Stretched billboard settings
            if (@params["lengthScale"] != null) { renderer.lengthScale = @params["lengthScale"].ToObject<float>(); changes.Add("lengthScale"); }
            if (@params["velocityScale"] != null) { renderer.velocityScale = @params["velocityScale"].ToObject<float>(); changes.Add("velocityScale"); }
            if (@params["cameraVelocityScale"] != null) { renderer.cameraVelocityScale = @params["cameraVelocityScale"].ToObject<float>(); changes.Add("cameraVelocityScale"); }
            if (@params["normalDirection"] != null) { renderer.normalDirection = @params["normalDirection"].ToObject<float>(); changes.Add("normalDirection"); }
            
            // Alignment and pivot
            if (@params["alignment"] != null && Enum.TryParse<ParticleSystemRenderSpace>(@params["alignment"].ToString(), true, out var alignment)) { renderer.alignment = alignment; changes.Add("alignment"); }
            if (@params["pivot"] != null) { renderer.pivot = ParseVector3(@params["pivot"]); changes.Add("pivot"); }
            if (@params["flip"] != null) { renderer.flip = ParseVector3(@params["flip"]); changes.Add("flip"); }
            if (@params["allowRoll"] != null) { renderer.allowRoll = @params["allowRoll"].ToObject<bool>(); changes.Add("allowRoll"); }
            
            //special case for particle system renderer
            if (@params["shadowBias"] != null) { renderer.shadowBias = @params["shadowBias"].ToObject<float>(); changes.Add("shadowBias"); }
            
            // Common Renderer properties (shadows, lighting, probes, sorting)
            RendererHelpers.ApplyCommonRendererProperties(renderer, @params, changes);
            
            // Material
            if (@params["materialPath"] != null)
            {
                var findInst = new JObject { ["find"] = @params["materialPath"].ToString() };
                Material mat = ManageGameObject.FindObjectByInstruction(findInst, typeof(Material)) as Material;
                if (mat != null) { renderer.sharedMaterial = mat; changes.Add("material"); }
            }
            if (@params["trailMaterialPath"] != null)
            {
                var findInst = new JObject { ["find"] = @params["trailMaterialPath"].ToString() };
                Material mat = ManageGameObject.FindObjectByInstruction(findInst, typeof(Material)) as Material;
                if (mat != null) { renderer.trailMaterial = mat; changes.Add("trailMaterial"); }
            }

            EditorUtility.SetDirty(renderer);
            return new { success = true, message = $"Updated renderer: {string.Join(", ", changes)}" };
        }

        private static object ParticleEnableModule(JObject @params)
        {
            ParticleSystem ps = FindParticleSystem(@params);
            if (ps == null) return new { success = false, message = "ParticleSystem not found" };

            string moduleName = @params["module"]?.ToString()?.ToLowerInvariant();
            bool enabled = @params["enabled"]?.ToObject<bool>() ?? true;

            if (string.IsNullOrEmpty(moduleName)) return new { success = false, message = "Module name required" };

            Undo.RecordObject(ps, $"Toggle {moduleName}");

            switch (moduleName.Replace("_", ""))
            {
                case "emission": var em = ps.emission; em.enabled = enabled; break;
                case "shape": var sh = ps.shape; sh.enabled = enabled; break;
                case "coloroverlifetime": var col = ps.colorOverLifetime; col.enabled = enabled; break;
                case "sizeoverlifetime": var sol = ps.sizeOverLifetime; sol.enabled = enabled; break;
                case "velocityoverlifetime": var vol = ps.velocityOverLifetime; vol.enabled = enabled; break;
                case "noise": var n = ps.noise; n.enabled = enabled; break;
                case "collision": var coll = ps.collision; coll.enabled = enabled; break;
                case "trails": var tr = ps.trails; tr.enabled = enabled; break;
                case "lights": var li = ps.lights; li.enabled = enabled; break;
                default: return new { success = false, message = $"Unknown module: {moduleName}" };
            }

            EditorUtility.SetDirty(ps);
            return new { success = true, message = $"Module '{moduleName}' {(enabled ? "enabled" : "disabled")}" };
        }

        private static object ParticleControl(JObject @params, string action)
        {
            ParticleSystem ps = FindParticleSystem(@params);
            if (ps == null) return new { success = false, message = "ParticleSystem not found" };

            bool withChildren = @params["withChildren"]?.ToObject<bool>() ?? true;

            switch (action)
            {
                case "play": ps.Play(withChildren); break;
                case "stop": ps.Stop(withChildren, ParticleSystemStopBehavior.StopEmitting); break;
                case "pause": ps.Pause(withChildren); break;
                case "restart": ps.Stop(withChildren, ParticleSystemStopBehavior.StopEmittingAndClear); ps.Play(withChildren); break;
                case "clear": ps.Clear(withChildren); break;
            }

            return new { success = true, message = $"ParticleSystem {action}" };
        }

        private static object ParticleAddBurst(JObject @params)
        {
            ParticleSystem ps = FindParticleSystem(@params);
            if (ps == null) return new { success = false, message = "ParticleSystem not found" };

            Undo.RecordObject(ps, "Add Burst");
            var emission = ps.emission;

            float time = @params["time"]?.ToObject<float>() ?? 0f;
            short minCount = (short)(@params["minCount"]?.ToObject<int>() ?? @params["count"]?.ToObject<int>() ?? 30);
            short maxCount = (short)(@params["maxCount"]?.ToObject<int>() ?? @params["count"]?.ToObject<int>() ?? 30);
            int cycles = @params["cycles"]?.ToObject<int>() ?? 1;
            float interval = @params["interval"]?.ToObject<float>() ?? 0.01f;

            var burst = new ParticleSystem.Burst(time, minCount, maxCount, cycles, interval);
            burst.probability = @params["probability"]?.ToObject<float>() ?? 1f;

            int idx = emission.burstCount;
            var bursts = new ParticleSystem.Burst[idx + 1];
            emission.GetBursts(bursts);
            bursts[idx] = burst;
            emission.SetBursts(bursts);

            EditorUtility.SetDirty(ps);
            return new { success = true, message = $"Added burst at t={time}", burstIndex = idx };
        }

        private static object ParticleClearBursts(JObject @params)
        {
            ParticleSystem ps = FindParticleSystem(@params);
            if (ps == null) return new { success = false, message = "ParticleSystem not found" };

            Undo.RecordObject(ps, "Clear Bursts");
            var emission = ps.emission;
            int count = emission.burstCount;
            emission.SetBursts(new ParticleSystem.Burst[0]);

            EditorUtility.SetDirty(ps);
            return new { success = true, message = $"Cleared {count} bursts" };
        }

        #endregion

        // ==================== VFX GRAPH ====================
        #region VFX Graph

        private static object HandleVFXGraphAction(JObject @params, string action)
        {
#if !UNITY_VFX_GRAPH
            return new { success = false, message = "VFX Graph package (com.unity.visualeffectgraph) not installed" };
#else
            switch (action)
            {
                // Asset management
                case "create_asset": return VFXCreateAsset(@params);
                case "assign_asset": return VFXAssignAsset(@params);
                case "list_templates": return VFXListTemplates(@params);
                case "list_assets": return VFXListAssets(@params);
                
                // Runtime parameter control
                case "get_info": return VFXGetInfo(@params);
                case "set_float": return VFXSetParameter<float>(@params, (vfx, n, v) => vfx.SetFloat(n, v));
                case "set_int": return VFXSetParameter<int>(@params, (vfx, n, v) => vfx.SetInt(n, v));
                case "set_bool": return VFXSetParameter<bool>(@params, (vfx, n, v) => vfx.SetBool(n, v));
                case "set_vector2": return VFXSetVector(@params, 2);
                case "set_vector3": return VFXSetVector(@params, 3);
                case "set_vector4": return VFXSetVector(@params, 4);
                case "set_color": return VFXSetColor(@params);
                case "set_gradient": return VFXSetGradient(@params);
                case "set_texture": return VFXSetTexture(@params);
                case "set_mesh": return VFXSetMesh(@params);
                case "set_curve": return VFXSetCurve(@params);
                case "send_event": return VFXSendEvent(@params);
                case "play": return VFXControl(@params, "play");
                case "stop": return VFXControl(@params, "stop");
                case "pause": return VFXControl(@params, "pause");
                case "reinit": return VFXControl(@params, "reinit");
                case "set_playback_speed": return VFXSetPlaybackSpeed(@params);
                case "set_seed": return VFXSetSeed(@params);
                default:
                    return new { success = false, message = $"Unknown vfx action: {action}. Valid: create_asset, assign_asset, list_templates, list_assets, get_info, set_float, set_int, set_bool, set_vector2/3/4, set_color, set_gradient, set_texture, set_mesh, set_curve, send_event, play, stop, pause, reinit, set_playback_speed, set_seed" };
            }
#endif
        }

#if UNITY_VFX_GRAPH
        private static VisualEffect FindVisualEffect(JObject @params)
        {
            GameObject go = FindTargetGameObject(@params);
            return go?.GetComponent<VisualEffect>();
        }

        /// <summary>
        /// Creates a new VFX Graph asset file from a template
        /// </summary>
        private static object VFXCreateAsset(JObject @params)
        {
            string assetName = @params["assetName"]?.ToString();
            string folderPath = @params["folderPath"]?.ToString() ?? "Assets/VFX";
            string template = @params["template"]?.ToString() ?? "empty";
            
            if (string.IsNullOrEmpty(assetName))
                return new { success = false, message = "assetName is required" };
            
            // Ensure folder exists
            if (!AssetDatabase.IsValidFolder(folderPath))
            {
                string[] folders = folderPath.Split('/');
                string currentPath = folders[0];
                for (int i = 1; i < folders.Length; i++)
                {
                    string newPath = currentPath + "/" + folders[i];
                    if (!AssetDatabase.IsValidFolder(newPath))
                    {
                        AssetDatabase.CreateFolder(currentPath, folders[i]);
                    }
                    currentPath = newPath;
                }
            }
            
            string assetPath = $"{folderPath}/{assetName}.vfx";
            
            // Check if asset already exists
            if (AssetDatabase.LoadAssetAtPath<UnityEngine.VFX.VisualEffectAsset>(assetPath) != null)
            {
                bool overwrite = @params["overwrite"]?.ToObject<bool>() ?? false;
                if (!overwrite)
                    return new { success = false, message = $"Asset already exists at {assetPath}. Set overwrite=true to replace." };
                AssetDatabase.DeleteAsset(assetPath);
            }
            
            // Find and copy template
            string templatePath = FindVFXTemplate(template);
            UnityEngine.VFX.VisualEffectAsset newAsset = null;
            
            if (!string.IsNullOrEmpty(templatePath) && System.IO.File.Exists(templatePath))
            {
                // templatePath is a full filesystem path, need to copy file directly
                // Get the full destination path
                string projectRoot = System.IO.Path.GetDirectoryName(Application.dataPath);
                string fullDestPath = System.IO.Path.Combine(projectRoot, assetPath);
                
                // Ensure directory exists
                string destDir = System.IO.Path.GetDirectoryName(fullDestPath);
                if (!System.IO.Directory.Exists(destDir))
                    System.IO.Directory.CreateDirectory(destDir);
                
                // Copy the file
                System.IO.File.Copy(templatePath, fullDestPath, true);
                AssetDatabase.Refresh();
                newAsset = AssetDatabase.LoadAssetAtPath<UnityEngine.VFX.VisualEffectAsset>(assetPath);
            }
            else
            {
                // Create empty VFX asset using reflection to access internal API
                // Note: Develop in Progress, TODO:// Find authenticated way to create VFX asset
                try
                {
                    // Try to use VisualEffectAssetEditorUtility.CreateNewAsset if available
                    var utilityType = System.Type.GetType("UnityEditor.VFX.VisualEffectAssetEditorUtility, Unity.VisualEffectGraph.Editor");
                    if (utilityType != null)
                    {
                        var createMethod = utilityType.GetMethod("CreateNewAsset", BindingFlags.Public | BindingFlags.Static);
                        if (createMethod != null)
                        {
                            createMethod.Invoke(null, new object[] { assetPath });
                            AssetDatabase.Refresh();
                            newAsset = AssetDatabase.LoadAssetAtPath<UnityEngine.VFX.VisualEffectAsset>(assetPath);
                        }
                    }
                    
                    // Fallback: Create a ScriptableObject-based asset
                    if (newAsset == null)
                    {
                        // Try direct creation via internal constructor
                        var resourceType = System.Type.GetType("UnityEditor.VFX.VisualEffectResource, Unity.VisualEffectGraph.Editor");
                        if (resourceType != null)
                        {
                            var createMethod = resourceType.GetMethod("CreateNewAsset", BindingFlags.Public | BindingFlags.Static | BindingFlags.NonPublic);
                            if (createMethod != null)
                            {
                                var resource = createMethod.Invoke(null, new object[] { assetPath });
                                AssetDatabase.Refresh();
                                newAsset = AssetDatabase.LoadAssetAtPath<UnityEngine.VFX.VisualEffectAsset>(assetPath);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    return new { success = false, message = $"Failed to create VFX asset: {ex.Message}" };
                }
            }
            
            if (newAsset == null)
            {
                return new { success = false, message = "Failed to create VFX asset. Try using a template from list_templates." };
            }
            
            return new 
            { 
                success = true, 
                message = $"Created VFX asset: {assetPath}",
                data = new
                {
                    assetPath = assetPath,
                    assetName = newAsset.name,
                    template = template
                }
            };
        }
        
        /// <summary>
        /// Finds VFX template path by name
        /// </summary>
        private static string FindVFXTemplate(string templateName)
        {
            // Get the actual filesystem path for the VFX Graph package using PackageManager API
            var packageInfo = UnityEditor.PackageManager.PackageInfo.FindForAssetPath("Packages/com.unity.visualeffectgraph");
            
            var searchPaths = new List<string>();
            
            if (packageInfo != null)
            {
                // Use the resolved path from PackageManager (handles Library/PackageCache paths)
                searchPaths.Add(System.IO.Path.Combine(packageInfo.resolvedPath, "Editor/Templates"));
                searchPaths.Add(System.IO.Path.Combine(packageInfo.resolvedPath, "Samples"));
            }
            
            // Also search project-local paths
            searchPaths.Add("Assets/VFX/Templates");
            
            string[] templatePatterns = new[]
            {
                $"{templateName}.vfx",
                $"VFX{templateName}.vfx",
                $"Simple{templateName}.vfx",
                $"{templateName}VFX.vfx"
            };
            
            foreach (string basePath in searchPaths)
            {
                if (!System.IO.Directory.Exists(basePath)) continue;
                
                foreach (string pattern in templatePatterns)
                {
                    string[] files = System.IO.Directory.GetFiles(basePath, pattern, System.IO.SearchOption.AllDirectories);
                    if (files.Length > 0)
                        return files[0];
                }
                
                // Also search by partial match
                try
                {
                    string[] allVfxFiles = System.IO.Directory.GetFiles(basePath, "*.vfx", System.IO.SearchOption.AllDirectories);
                    foreach (string file in allVfxFiles)
                    {
                        if (System.IO.Path.GetFileNameWithoutExtension(file).ToLower().Contains(templateName.ToLower()))
                            return file;
                    }
                }
                catch { }
            }
            
            // Search in project assets
            string[] guids = AssetDatabase.FindAssets("t:VisualEffectAsset " + templateName);
            if (guids.Length > 0)
            {
                return AssetDatabase.GUIDToAssetPath(guids[0]);
            }
            
            return null;
        }
        
        /// <summary>
        /// Assigns a VFX asset to a VisualEffect component
        /// </summary>
        private static object VFXAssignAsset(JObject @params)
        {
            VisualEffect vfx = FindVisualEffect(@params);
            if (vfx == null) return new { success = false, message = "VisualEffect component not found" };
            
            string assetPath = @params["assetPath"]?.ToString();
            if (string.IsNullOrEmpty(assetPath))
                return new { success = false, message = "assetPath is required" };
            
            // Normalize path
            if (!assetPath.StartsWith("Assets/") && !assetPath.StartsWith("Packages/"))
                assetPath = "Assets/" + assetPath;
            if (!assetPath.EndsWith(".vfx"))
                assetPath += ".vfx";
            
            var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.VFX.VisualEffectAsset>(assetPath);
            if (asset == null)
            {
                // Try searching by name
                string searchName = System.IO.Path.GetFileNameWithoutExtension(assetPath);
                string[] guids = AssetDatabase.FindAssets($"t:VisualEffectAsset {searchName}");
                if (guids.Length > 0)
                {
                    assetPath = AssetDatabase.GUIDToAssetPath(guids[0]);
                    asset = AssetDatabase.LoadAssetAtPath<UnityEngine.VFX.VisualEffectAsset>(assetPath);
                }
            }
            
            if (asset == null)
                return new { success = false, message = $"VFX asset not found: {assetPath}" };
            
            Undo.RecordObject(vfx, "Assign VFX Asset");
            vfx.visualEffectAsset = asset;
            EditorUtility.SetDirty(vfx);
            
            return new 
            { 
                success = true, 
                message = $"Assigned VFX asset '{asset.name}' to {vfx.gameObject.name}",
                data = new
                {
                    gameObject = vfx.gameObject.name,
                    assetName = asset.name,
                    assetPath = assetPath
                }
            };
        }
        
        /// <summary>
        /// Lists available VFX templates
        /// </summary>
        private static object VFXListTemplates(JObject @params)
        {
            var templates = new List<object>();
            
            // Get the actual filesystem path for the VFX Graph package using PackageManager API
            var packageInfo = UnityEditor.PackageManager.PackageInfo.FindForAssetPath("Packages/com.unity.visualeffectgraph");
            
            var searchPaths = new List<string>();
            
            if (packageInfo != null)
            {
                // Use the resolved path from PackageManager (handles Library/PackageCache paths)
                searchPaths.Add(System.IO.Path.Combine(packageInfo.resolvedPath, "Editor/Templates"));
                searchPaths.Add(System.IO.Path.Combine(packageInfo.resolvedPath, "Samples"));
            }
            
            // Also search project-local paths
            searchPaths.Add("Assets/VFX/Templates");
            searchPaths.Add("Assets/VFX");
            
            // Precompute normalized package path for comparison
            string normalizedPackagePath = null;
            if (packageInfo != null)
            {
                normalizedPackagePath = packageInfo.resolvedPath.Replace("\\", "/");
            }
            
            // Precompute the Assets base path for converting absolute paths to project-relative
            string assetsBasePath = Application.dataPath.Replace("\\", "/");
            
            foreach (string basePath in searchPaths)
            {
                if (!System.IO.Directory.Exists(basePath)) continue;
                
                try
                {
                    string[] vfxFiles = System.IO.Directory.GetFiles(basePath, "*.vfx", System.IO.SearchOption.AllDirectories);
                    foreach (string file in vfxFiles)
                    {
                        string absolutePath = file.Replace("\\", "/");
                        string name = System.IO.Path.GetFileNameWithoutExtension(file);
                        bool isPackage = normalizedPackagePath != null && absolutePath.StartsWith(normalizedPackagePath);
                        
                        // Convert absolute path to project-relative path
                        string projectRelativePath;
                        if (isPackage)
                        {
                            // For package paths, convert to Packages/... format
                            projectRelativePath = "Packages/" + packageInfo.name + absolutePath.Substring(normalizedPackagePath.Length);
                        }
                        else if (absolutePath.StartsWith(assetsBasePath))
                        {
                            // For project assets, convert to Assets/... format
                            projectRelativePath = "Assets" + absolutePath.Substring(assetsBasePath.Length);
                        }
                        else
                        {
                            // Fallback: use the absolute path if we can't determine the relative path
                            projectRelativePath = absolutePath;
                        }
                        
                        templates.Add(new { name = name, path = projectRelativePath, source = isPackage ? "package" : "project" });
                    }
                }
                catch { }
            }
            
            // Also search project assets
            string[] guids = AssetDatabase.FindAssets("t:VisualEffectAsset");
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!templates.Any(t => ((dynamic)t).path == path))
                {
                    string name = System.IO.Path.GetFileNameWithoutExtension(path);
                    templates.Add(new { name = name, path = path, source = "project" });
                }
            }
            
            return new 
            { 
                success = true, 
                data = new
                {
                    count = templates.Count,
                    templates = templates
                }
            };
        }
        
        /// <summary>
        /// Lists all VFX assets in the project
        /// </summary>
        private static object VFXListAssets(JObject @params)
        {
            string searchFolder = @params["folder"]?.ToString();
            string searchPattern = @params["search"]?.ToString();
            
            string filter = "t:VisualEffectAsset";
            if (!string.IsNullOrEmpty(searchPattern))
                filter += " " + searchPattern;
            
            string[] guids;
            if (!string.IsNullOrEmpty(searchFolder))
                guids = AssetDatabase.FindAssets(filter, new[] { searchFolder });
            else
                guids = AssetDatabase.FindAssets(filter);
            
            var assets = new List<object>();
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.VFX.VisualEffectAsset>(path);
                if (asset != null)
                {
                    assets.Add(new 
                    { 
                        name = asset.name, 
                        path = path,
                        guid = guid
                    });
                }
            }
            
            return new 
            { 
                success = true, 
                data = new
                {
                    count = assets.Count,
                    assets = assets
                }
            };
        }

        private static object VFXGetInfo(JObject @params)
        {
            VisualEffect vfx = FindVisualEffect(@params);
            if (vfx == null) return new { success = false, message = "VisualEffect not found" };

            return new
            {
                success = true,
                data = new
                {
                    gameObject = vfx.gameObject.name,
                    assetName = vfx.visualEffectAsset?.name ?? "None",
                    aliveParticleCount = vfx.aliveParticleCount,
                    culled = vfx.culled,
                    pause = vfx.pause,
                    playRate = vfx.playRate,
                    startSeed = vfx.startSeed
                }
            };
        }

        private static object VFXSetParameter<T>(JObject @params, Action<VisualEffect, string, T> setter)
        {
            VisualEffect vfx = FindVisualEffect(@params);
            if (vfx == null) return new { success = false, message = "VisualEffect not found" };

            string param = @params["parameter"]?.ToString();
            if (string.IsNullOrEmpty(param)) return new { success = false, message = "Parameter name required" };

            JToken valueToken = @params["value"];
            if (valueToken == null) return new { success = false, message = "Value required" };

            Undo.RecordObject(vfx, $"Set VFX {param}");
            T value = valueToken.ToObject<T>();
            setter(vfx, param, value);
            EditorUtility.SetDirty(vfx);

            return new { success = true, message = $"Set {param} = {value}" };
        }

        private static object VFXSetVector(JObject @params, int dims)
        {
            VisualEffect vfx = FindVisualEffect(@params);
            if (vfx == null) return new { success = false, message = "VisualEffect not found" };

            string param = @params["parameter"]?.ToString();
            if (string.IsNullOrEmpty(param)) return new { success = false, message = "Parameter name required" };

            Vector4 vec = ParseVector4(@params["value"]);
            Undo.RecordObject(vfx, $"Set VFX {param}");

            switch (dims)
            {
                case 2: vfx.SetVector2(param, new Vector2(vec.x, vec.y)); break;
                case 3: vfx.SetVector3(param, new Vector3(vec.x, vec.y, vec.z)); break;
                case 4: vfx.SetVector4(param, vec); break;
            }

            EditorUtility.SetDirty(vfx);
            return new { success = true, message = $"Set {param}" };
        }

        private static object VFXSetColor(JObject @params)
        {
            VisualEffect vfx = FindVisualEffect(@params);
            if (vfx == null) return new { success = false, message = "VisualEffect not found" };

            string param = @params["parameter"]?.ToString();
            if (string.IsNullOrEmpty(param)) return new { success = false, message = "Parameter name required" };

            Color color = ParseColor(@params["value"]);
            Undo.RecordObject(vfx, $"Set VFX Color {param}");
            vfx.SetVector4(param, new Vector4(color.r, color.g, color.b, color.a));
            EditorUtility.SetDirty(vfx);

            return new { success = true, message = $"Set color {param}" };
        }

        private static object VFXSetGradient(JObject @params)
        {
            VisualEffect vfx = FindVisualEffect(@params);
            if (vfx == null) return new { success = false, message = "VisualEffect not found" };

            string param = @params["parameter"]?.ToString();
            if (string.IsNullOrEmpty(param)) return new { success = false, message = "Parameter name required" };

            Gradient gradient = ParseGradient(@params["gradient"]);
            Undo.RecordObject(vfx, $"Set VFX Gradient {param}");
            vfx.SetGradient(param, gradient);
            EditorUtility.SetDirty(vfx);

            return new { success = true, message = $"Set gradient {param}" };
        }

        private static object VFXSetTexture(JObject @params)
        {
            VisualEffect vfx = FindVisualEffect(@params);
            if (vfx == null) return new { success = false, message = "VisualEffect not found" };

            string param = @params["parameter"]?.ToString();
            string path = @params["texturePath"]?.ToString();
            if (string.IsNullOrEmpty(param) || string.IsNullOrEmpty(path)) return new { success = false, message = "Parameter and texturePath required" };

            var findInst = new JObject { ["find"] = path };
            Texture tex = ManageGameObject.FindObjectByInstruction(findInst, typeof(Texture)) as Texture;
            if (tex == null) return new { success = false, message = $"Texture not found: {path}" };

            Undo.RecordObject(vfx, $"Set VFX Texture {param}");
            vfx.SetTexture(param, tex);
            EditorUtility.SetDirty(vfx);

            return new { success = true, message = $"Set texture {param} = {tex.name}" };
        }

        private static object VFXSetMesh(JObject @params)
        {
            VisualEffect vfx = FindVisualEffect(@params);
            if (vfx == null) return new { success = false, message = "VisualEffect not found" };

            string param = @params["parameter"]?.ToString();
            string path = @params["meshPath"]?.ToString();
            if (string.IsNullOrEmpty(param) || string.IsNullOrEmpty(path)) return new { success = false, message = "Parameter and meshPath required" };

            var findInst = new JObject { ["find"] = path };
            Mesh mesh = ManageGameObject.FindObjectByInstruction(findInst, typeof(Mesh)) as Mesh;
            if (mesh == null) return new { success = false, message = $"Mesh not found: {path}" };

            Undo.RecordObject(vfx, $"Set VFX Mesh {param}");
            vfx.SetMesh(param, mesh);
            EditorUtility.SetDirty(vfx);

            return new { success = true, message = $"Set mesh {param} = {mesh.name}" };
        }

        private static object VFXSetCurve(JObject @params)
        {
            VisualEffect vfx = FindVisualEffect(@params);
            if (vfx == null) return new { success = false, message = "VisualEffect not found" };

            string param = @params["parameter"]?.ToString();
            if (string.IsNullOrEmpty(param)) return new { success = false, message = "Parameter name required" };

            AnimationCurve curve = ParseAnimationCurve(@params["curve"], 1f);
            Undo.RecordObject(vfx, $"Set VFX Curve {param}");
            vfx.SetAnimationCurve(param, curve);
            EditorUtility.SetDirty(vfx);

            return new { success = true, message = $"Set curve {param}" };
        }

        private static object VFXSendEvent(JObject @params)
        {
            VisualEffect vfx = FindVisualEffect(@params);
            if (vfx == null) return new { success = false, message = "VisualEffect not found" };

            string eventName = @params["eventName"]?.ToString();
            if (string.IsNullOrEmpty(eventName)) return new { success = false, message = "Event name required" };

            VFXEventAttribute attr = vfx.CreateVFXEventAttribute();
            if (@params["position"] != null) attr.SetVector3("position", ParseVector3(@params["position"]));
            if (@params["velocity"] != null) attr.SetVector3("velocity", ParseVector3(@params["velocity"]));
            if (@params["color"] != null) { var c = ParseColor(@params["color"]); attr.SetVector3("color", new Vector3(c.r, c.g, c.b)); }
            if (@params["size"] != null) attr.SetFloat("size", @params["size"].ToObject<float>());
            if (@params["lifetime"] != null) attr.SetFloat("lifetime", @params["lifetime"].ToObject<float>());

            vfx.SendEvent(eventName, attr);
            return new { success = true, message = $"Sent event '{eventName}'" };
        }

        private static object VFXControl(JObject @params, string action)
        {
            VisualEffect vfx = FindVisualEffect(@params);
            if (vfx == null) return new { success = false, message = "VisualEffect not found" };

            switch (action)
            {
                case "play": vfx.Play(); break;
                case "stop": vfx.Stop(); break;
                case "pause": vfx.pause = !vfx.pause; break;
                case "reinit": vfx.Reinit(); break;
            }

            return new { success = true, message = $"VFX {action}", isPaused = vfx.pause };
        }

        private static object VFXSetPlaybackSpeed(JObject @params)
        {
            VisualEffect vfx = FindVisualEffect(@params);
            if (vfx == null) return new { success = false, message = "VisualEffect not found" };

            float rate = @params["playRate"]?.ToObject<float>() ?? 1f;
            Undo.RecordObject(vfx, "Set VFX Play Rate");
            vfx.playRate = rate;
            EditorUtility.SetDirty(vfx);

            return new { success = true, message = $"Set play rate = {rate}" };
        }

        private static object VFXSetSeed(JObject @params)
        {
            VisualEffect vfx = FindVisualEffect(@params);
            if (vfx == null) return new { success = false, message = "VisualEffect not found" };

            uint seed = @params["seed"]?.ToObject<uint>() ?? 0;
            bool resetOnPlay = @params["resetSeedOnPlay"]?.ToObject<bool>() ?? true;

            Undo.RecordObject(vfx, "Set VFX Seed");
            vfx.startSeed = seed;
            vfx.resetSeedOnPlay = resetOnPlay;
            EditorUtility.SetDirty(vfx);

            return new { success = true, message = $"Set seed = {seed}" };
        }
#endif

        #endregion

        // ==================== LINE RENDERER ====================
        #region LineRenderer

        private static object HandleLineRendererAction(JObject @params, string action)
        {
            switch (action)
            {
                case "get_info": return LineGetInfo(@params);
                case "set_positions": return LineSetPositions(@params);
                case "add_position": return LineAddPosition(@params);
                case "set_position": return LineSetPosition(@params);
                case "set_width": return LineSetWidth(@params);
                case "set_color": return LineSetColor(@params);
                case "set_material": return LineSetMaterial(@params);
                case "set_properties": return LineSetProperties(@params);
                case "clear": return LineClear(@params);
                case "create_line": return LineCreateLine(@params);
                case "create_circle": return LineCreateCircle(@params);
                case "create_arc": return LineCreateArc(@params);
                case "create_bezier": return LineCreateBezier(@params);
                default:
                    return new { success = false, message = $"Unknown line action: {action}. Valid: get_info, set_positions, add_position, set_position, set_width, set_color, set_material, set_properties, clear, create_line, create_circle, create_arc, create_bezier" };
            }
        }

        private static LineRenderer FindLineRenderer(JObject @params)
        {
            GameObject go = FindTargetGameObject(@params);
            return go?.GetComponent<LineRenderer>();
        }

        private static object LineGetInfo(JObject @params)
        {
            LineRenderer lr = FindLineRenderer(@params);
            if (lr == null) return new { success = false, message = "LineRenderer not found" };

            var positions = new Vector3[lr.positionCount];
            lr.GetPositions(positions);

            return new
            {
                success = true,
                data = new
                {
                    gameObject = lr.gameObject.name,
                    positionCount = lr.positionCount,
                    positions = positions.Select(p => new { x = p.x, y = p.y, z = p.z }).ToArray(),
                    startWidth = lr.startWidth,
                    endWidth = lr.endWidth,
                    loop = lr.loop,
                    useWorldSpace = lr.useWorldSpace,
                    alignment = lr.alignment.ToString(),
                    textureMode = lr.textureMode.ToString(),
                    numCornerVertices = lr.numCornerVertices,
                    numCapVertices = lr.numCapVertices,
                    generateLightingData = lr.generateLightingData,
                    material = lr.sharedMaterial?.name,
                    // Shadows & lighting
                    shadowCastingMode = lr.shadowCastingMode.ToString(),
                    receiveShadows = lr.receiveShadows,
                    lightProbeUsage = lr.lightProbeUsage.ToString(),
                    reflectionProbeUsage = lr.reflectionProbeUsage.ToString(),
                    // Sorting
                    sortingOrder = lr.sortingOrder,
                    sortingLayerName = lr.sortingLayerName,
                    renderingLayerMask = lr.renderingLayerMask
                }
            };
        }

        private static object LineSetPositions(JObject @params)
        {
            LineRenderer lr = FindLineRenderer(@params);
            if (lr == null) return new { success = false, message = "LineRenderer not found" };

            JArray posArr = @params["positions"] as JArray;
            if (posArr == null) return new { success = false, message = "Positions array required" };

            var positions = posArr.Select(p => ParseVector3(p)).ToArray();

            Undo.RecordObject(lr, "Set Line Positions");
            lr.positionCount = positions.Length;
            lr.SetPositions(positions);
            EditorUtility.SetDirty(lr);

            return new { success = true, message = $"Set {positions.Length} positions" };
        }

        private static object LineAddPosition(JObject @params)
        {
            LineRenderer lr = FindLineRenderer(@params);
            if (lr == null) return new { success = false, message = "LineRenderer not found" };

            Vector3 pos = ParseVector3(@params["position"]);

            Undo.RecordObject(lr, "Add Line Position");
            int idx = lr.positionCount;
            lr.positionCount = idx + 1;
            lr.SetPosition(idx, pos);
            EditorUtility.SetDirty(lr);

            return new { success = true, message = $"Added position at index {idx}", index = idx };
        }

        private static object LineSetPosition(JObject @params)
        {
            LineRenderer lr = FindLineRenderer(@params);
            if (lr == null) return new { success = false, message = "LineRenderer not found" };

            int index = @params["index"]?.ToObject<int>() ?? -1;
            if (index < 0 || index >= lr.positionCount) return new { success = false, message = $"Invalid index {index}" };

            Vector3 pos = ParseVector3(@params["position"]);

            Undo.RecordObject(lr, "Set Line Position");
            lr.SetPosition(index, pos);
            EditorUtility.SetDirty(lr);

            return new { success = true, message = $"Set position at index {index}" };
        }

        private static object LineSetWidth(JObject @params)
        {
            LineRenderer lr = FindLineRenderer(@params);
            if (lr == null) return new { success = false, message = "LineRenderer not found" };

            Undo.RecordObject(lr, "Set Line Width");
            var changes = new List<string>();

            RendererHelpers.ApplyWidthProperties(@params, changes,
                v => lr.startWidth = v, v => lr.endWidth = v,
                v => lr.widthCurve = v, v => lr.widthMultiplier = v,
                ParseAnimationCurve);

            EditorUtility.SetDirty(lr);
            return new { success = true, message = $"Updated: {string.Join(", ", changes)}" };
        }

        private static object LineSetColor(JObject @params)
        {
            LineRenderer lr = FindLineRenderer(@params);
            if (lr == null) return new { success = false, message = "LineRenderer not found" };

            Undo.RecordObject(lr, "Set Line Color");
            var changes = new List<string>();

            RendererHelpers.ApplyColorProperties(@params, changes,
                v => lr.startColor = v, v => lr.endColor = v,
                v => lr.colorGradient = v,
                ParseColor, ParseGradient, fadeEndAlpha: false);

            EditorUtility.SetDirty(lr);
            return new { success = true, message = $"Updated: {string.Join(", ", changes)}" };
        }

        private static object LineSetMaterial(JObject @params)
        {
            LineRenderer lr = FindLineRenderer(@params);
            return RendererHelpers.SetRendererMaterial(lr, @params, "Set Line Material", FindMaterialByPath);
        }

        private static object LineSetProperties(JObject @params)
        {
            LineRenderer lr = FindLineRenderer(@params);
            if (lr == null) return new { success = false, message = "LineRenderer not found" };

            Undo.RecordObject(lr, "Set Line Properties");
            var changes = new List<string>();

            // Line-specific properties
            RendererHelpers.ApplyLineTrailProperties(@params, changes,
                v => lr.loop = v, v => lr.useWorldSpace = v,
                v => lr.numCornerVertices = v, v => lr.numCapVertices = v,
                v => lr.alignment = v, v => lr.textureMode = v,
                v => lr.generateLightingData = v);
            
            // Common Renderer properties (shadows, lighting, probes, sorting)
            RendererHelpers.ApplyCommonRendererProperties(lr, @params, changes);

            EditorUtility.SetDirty(lr);
            return new { success = true, message = $"Updated: {string.Join(", ", changes)}" };
        }

        private static object LineClear(JObject @params)
        {
            LineRenderer lr = FindLineRenderer(@params);
            if (lr == null) return new { success = false, message = "LineRenderer not found" };

            int count = lr.positionCount;
            Undo.RecordObject(lr, "Clear Line");
            lr.positionCount = 0;
            EditorUtility.SetDirty(lr);

            return new { success = true, message = $"Cleared {count} positions" };
        }

        private static object LineCreateLine(JObject @params)
        {
            LineRenderer lr = FindLineRenderer(@params);
            if (lr == null) return new { success = false, message = "LineRenderer not found" };

            Vector3 start = ParseVector3(@params["start"]);
            Vector3 end = ParseVector3(@params["end"]);

            Undo.RecordObject(lr, "Create Line");
            lr.positionCount = 2;
            lr.SetPosition(0, start);
            lr.SetPosition(1, end);
            EditorUtility.SetDirty(lr);

            return new { success = true, message = "Created line" };
        }

        private static object LineCreateCircle(JObject @params)
        {
            LineRenderer lr = FindLineRenderer(@params);
            if (lr == null) return new { success = false, message = "LineRenderer not found" };

            Vector3 center = ParseVector3(@params["center"]);
            float radius = @params["radius"]?.ToObject<float>() ?? 1f;
            int segments = @params["segments"]?.ToObject<int>() ?? 32;
            Vector3 normal = @params["normal"] != null ? ParseVector3(@params["normal"]).normalized : Vector3.up;

            Vector3 right = Vector3.Cross(normal, Vector3.forward);
            if (right.sqrMagnitude < 0.001f) right = Vector3.Cross(normal, Vector3.up);
            right = right.normalized;
            Vector3 forward = Vector3.Cross(right, normal).normalized;

            Undo.RecordObject(lr, "Create Circle");
            lr.positionCount = segments;
            lr.loop = true;

            for (int i = 0; i < segments; i++)
            {
                float angle = (float)i / segments * Mathf.PI * 2f;
                Vector3 point = center + (right * Mathf.Cos(angle) + forward * Mathf.Sin(angle)) * radius;
                lr.SetPosition(i, point);
            }

            EditorUtility.SetDirty(lr);
            return new { success = true, message = $"Created circle with {segments} segments" };
        }

        private static object LineCreateArc(JObject @params)
        {
            LineRenderer lr = FindLineRenderer(@params);
            if (lr == null) return new { success = false, message = "LineRenderer not found" };

            Vector3 center = ParseVector3(@params["center"]);
            float radius = @params["radius"]?.ToObject<float>() ?? 1f;
            float startAngle = (@params["startAngle"]?.ToObject<float>() ?? 0f) * Mathf.Deg2Rad;
            float endAngle = (@params["endAngle"]?.ToObject<float>() ?? 180f) * Mathf.Deg2Rad;
            int segments = @params["segments"]?.ToObject<int>() ?? 16;
            Vector3 normal = @params["normal"] != null ? ParseVector3(@params["normal"]).normalized : Vector3.up;

            Vector3 right = Vector3.Cross(normal, Vector3.forward);
            if (right.sqrMagnitude < 0.001f) right = Vector3.Cross(normal, Vector3.up);
            right = right.normalized;
            Vector3 forward = Vector3.Cross(right, normal).normalized;

            Undo.RecordObject(lr, "Create Arc");
            lr.positionCount = segments + 1;
            lr.loop = false;

            for (int i = 0; i <= segments; i++)
            {
                float t = (float)i / segments;
                float angle = Mathf.Lerp(startAngle, endAngle, t);
                Vector3 point = center + (right * Mathf.Cos(angle) + forward * Mathf.Sin(angle)) * radius;
                lr.SetPosition(i, point);
            }

            EditorUtility.SetDirty(lr);
            return new { success = true, message = $"Created arc with {segments} segments" };
        }

        private static object LineCreateBezier(JObject @params)
        {
            LineRenderer lr = FindLineRenderer(@params);
            if (lr == null) return new { success = false, message = "LineRenderer not found" };

            Vector3 start = ParseVector3(@params["start"]);
            Vector3 end = ParseVector3(@params["end"]);
            Vector3 cp1 = ParseVector3(@params["controlPoint1"] ?? @params["control1"]);
            Vector3 cp2 = @params["controlPoint2"] != null || @params["control2"] != null
                ? ParseVector3(@params["controlPoint2"] ?? @params["control2"])
                : cp1;
            int segments = @params["segments"]?.ToObject<int>() ?? 32;
            bool isQuadratic = @params["controlPoint2"] == null && @params["control2"] == null;

            Undo.RecordObject(lr, "Create Bezier");
            lr.positionCount = segments + 1;
            lr.loop = false;

            for (int i = 0; i <= segments; i++)
            {
                float t = (float)i / segments;
                Vector3 point;

                if (isQuadratic)
                {
                    float u = 1 - t;
                    point = u * u * start + 2 * u * t * cp1 + t * t * end;
                }
                else
                {
                    float u = 1 - t;
                    point = u * u * u * start + 3 * u * u * t * cp1 + 3 * u * t * t * cp2 + t * t * t * end;
                }

                lr.SetPosition(i, point);
            }

            EditorUtility.SetDirty(lr);
            return new { success = true, message = $"Created {(isQuadratic ? "quadratic" : "cubic")} Bezier" };
        }

        #endregion

        // ==================== TRAIL RENDERER ====================
        #region TrailRenderer

        private static object HandleTrailRendererAction(JObject @params, string action)
        {
            switch (action)
            {
                case "get_info": return TrailGetInfo(@params);
                case "set_time": return TrailSetTime(@params);
                case "set_width": return TrailSetWidth(@params);
                case "set_color": return TrailSetColor(@params);
                case "set_material": return TrailSetMaterial(@params);
                case "set_properties": return TrailSetProperties(@params);
                case "clear": return TrailClear(@params);
                case "emit": return TrailEmit(@params);
                default:
                    return new { success = false, message = $"Unknown trail action: {action}. Valid: get_info, set_time, set_width, set_color, set_material, set_properties, clear, emit" };
            }
        }

        private static TrailRenderer FindTrailRenderer(JObject @params)
        {
            GameObject go = FindTargetGameObject(@params);
            return go?.GetComponent<TrailRenderer>();
        }

        private static object TrailGetInfo(JObject @params)
        {
            TrailRenderer tr = FindTrailRenderer(@params);
            if (tr == null) return new { success = false, message = "TrailRenderer not found" };

            return new
            {
                success = true,
                data = new
                {
                    gameObject = tr.gameObject.name,
                    time = tr.time,
                    startWidth = tr.startWidth,
                    endWidth = tr.endWidth,
                    minVertexDistance = tr.minVertexDistance,
                    emitting = tr.emitting,
                    autodestruct = tr.autodestruct,
                    positionCount = tr.positionCount,
                    alignment = tr.alignment.ToString(),
                    textureMode = tr.textureMode.ToString(),
                    numCornerVertices = tr.numCornerVertices,
                    numCapVertices = tr.numCapVertices,
                    generateLightingData = tr.generateLightingData,
                    material = tr.sharedMaterial?.name,
                    // Shadows & lighting
                    shadowCastingMode = tr.shadowCastingMode.ToString(),
                    receiveShadows = tr.receiveShadows,
                    lightProbeUsage = tr.lightProbeUsage.ToString(),
                    reflectionProbeUsage = tr.reflectionProbeUsage.ToString(),
                    // Sorting
                    sortingOrder = tr.sortingOrder,
                    sortingLayerName = tr.sortingLayerName,
                    renderingLayerMask = tr.renderingLayerMask
                }
            };
        }

        private static object TrailSetTime(JObject @params)
        {
            TrailRenderer tr = FindTrailRenderer(@params);
            if (tr == null) return new { success = false, message = "TrailRenderer not found" };

            float time = @params["time"]?.ToObject<float>() ?? 5f;

            Undo.RecordObject(tr, "Set Trail Time");
            tr.time = time;
            EditorUtility.SetDirty(tr);

            return new { success = true, message = $"Set trail time to {time}s" };
        }

        private static object TrailSetWidth(JObject @params)
        {
            TrailRenderer tr = FindTrailRenderer(@params);
            if (tr == null) return new { success = false, message = "TrailRenderer not found" };

            Undo.RecordObject(tr, "Set Trail Width");
            var changes = new List<string>();

            RendererHelpers.ApplyWidthProperties(@params, changes,
                v => tr.startWidth = v, v => tr.endWidth = v,
                v => tr.widthCurve = v, v => tr.widthMultiplier = v,
                ParseAnimationCurve);

            EditorUtility.SetDirty(tr);
            return new { success = true, message = $"Updated: {string.Join(", ", changes)}" };
        }

        private static object TrailSetColor(JObject @params)
        {
            TrailRenderer tr = FindTrailRenderer(@params);
            if (tr == null) return new { success = false, message = "TrailRenderer not found" };

            Undo.RecordObject(tr, "Set Trail Color");
            var changes = new List<string>();

            RendererHelpers.ApplyColorProperties(@params, changes,
                v => tr.startColor = v, v => tr.endColor = v,
                v => tr.colorGradient = v,
                ParseColor, ParseGradient, fadeEndAlpha: true);

            EditorUtility.SetDirty(tr);
            return new { success = true, message = $"Updated: {string.Join(", ", changes)}" };
        }

        private static object TrailSetMaterial(JObject @params)
        {
            TrailRenderer tr = FindTrailRenderer(@params);
            return RendererHelpers.SetRendererMaterial(tr, @params, "Set Trail Material", FindMaterialByPath);
        }

        private static object TrailSetProperties(JObject @params)
        {
            TrailRenderer tr = FindTrailRenderer(@params);
            if (tr == null) return new { success = false, message = "TrailRenderer not found" };

            Undo.RecordObject(tr, "Set Trail Properties");
            var changes = new List<string>();

            // Trail-specific properties (not shared with LineRenderer)
            if (@params["minVertexDistance"] != null) { tr.minVertexDistance = @params["minVertexDistance"].ToObject<float>(); changes.Add("minVertexDistance"); }
            if (@params["autodestruct"] != null) { tr.autodestruct = @params["autodestruct"].ToObject<bool>(); changes.Add("autodestruct"); }
            if (@params["emitting"] != null) { tr.emitting = @params["emitting"].ToObject<bool>(); changes.Add("emitting"); }
            
            // Shared Line/Trail properties
            RendererHelpers.ApplyLineTrailProperties(@params, changes,
                null, null, // Trail doesn't have loop or useWorldSpace
                v => tr.numCornerVertices = v, v => tr.numCapVertices = v,
                v => tr.alignment = v, v => tr.textureMode = v,
                v => tr.generateLightingData = v);
            
            // Common Renderer properties (shadows, lighting, probes, sorting)
            RendererHelpers.ApplyCommonRendererProperties(tr, @params, changes);

            EditorUtility.SetDirty(tr);
            return new { success = true, message = $"Updated: {string.Join(", ", changes)}" };
        }

        private static object TrailClear(JObject @params)
        {
            TrailRenderer tr = FindTrailRenderer(@params);
            if (tr == null) return new { success = false, message = "TrailRenderer not found" };

            Undo.RecordObject(tr, "Clear Trail");
            tr.Clear();
            return new { success = true, message = "Trail cleared" };
        }

        private static object TrailEmit(JObject @params)
        {
            TrailRenderer tr = FindTrailRenderer(@params);
            if (tr == null) return new { success = false, message = "TrailRenderer not found" };

#if UNITY_2021_1_OR_NEWER
            Vector3 pos = ParseVector3(@params["position"]);
            tr.AddPosition(pos);
            return new { success = true, message = $"Emitted at ({pos.x}, {pos.y}, {pos.z})" };
#else
            return new { success = false, message = "AddPosition requires Unity 2021.1+" };
#endif
        }

        #endregion
    }
}
