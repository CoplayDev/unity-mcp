using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using MCPForUnity.Editor.Helpers;
using UnityEngine;
using UnityEditor;

#if UNITY_VFX_GRAPH //Please enable the symbol in the project settings for VisualEffectGraph to work
using UnityEngine.VFX;
#endif

namespace MCPForUnity.Editor.Tools.Vfx
{
    /// <summary>
    /// Tool for managing Unity VFX components:
    /// - ParticleSystem (legacy particle effects)
    /// - Visual Effect Graph (modern GPU particles, currently only support HDRP, other SRPs may not work)
    /// - LineRenderer (lines, bezier curves, shapes)
    /// - TrailRenderer (motion trails)
    ///
    /// COMPONENT REQUIREMENTS:
    /// - particle_* actions require ParticleSystem component on target GameObject
    /// - vfx_* actions require VisualEffect component (+ com.unity.visualeffectgraph package)
    /// - line_* actions require LineRenderer component
    /// - trail_* actions require TrailRenderer component
    ///
    /// TARGETING:
    /// Use 'target' parameter with optional 'searchMethod':
    /// - by_name (default): "Fire" finds first GameObject named "Fire"
    /// - by_path: "Effects/Fire" finds GameObject at hierarchy path
    /// - by_id: "12345" finds GameObject by instance ID (most reliable)
    /// - by_tag: "Enemy" finds first GameObject with tag
    ///
    /// AUTOMATIC MATERIAL ASSIGNMENT:
    /// VFX components (ParticleSystem, LineRenderer, TrailRenderer) automatically receive
    /// appropriate default materials based on the active rendering pipeline when no material
    /// is explicitly specified:
    /// - Built-in Pipeline: Uses Unity's built-in Default-Particle.mat and Default-Line.mat
    /// - URP/HDRP: Creates materials with pipeline-appropriate unlit shaders
    /// - Materials are cached to avoid recreation
    /// - Explicit materialPath parameter always overrides auto-assignment
    /// - Auto-assigned materials are logged for transparency
    ///
    /// AVAILABLE ACTIONS:
    ///
    /// ParticleSystem (particle_*):
    ///   - particle_get_info: Get system info and current state
    ///   - particle_set_main: Set main module (duration, looping, startLifetime, startSpeed, startSize, startColor, gravityModifier, maxParticles, simulationSpace, playOnAwake, etc.)
    ///   - particle_set_emission: Set emission module (rateOverTime, rateOverDistance)
    ///   - particle_set_shape: Set shape module (shapeType, radius, angle, arc, position, rotation, scale)
    ///   - particle_set_color_over_lifetime: Set color gradient over particle lifetime
    ///   - particle_set_size_over_lifetime: Set size curve over particle lifetime
    ///   - particle_set_velocity_over_lifetime: Set velocity (x, y, z, speedModifier, space)
    ///   - particle_set_noise: Set noise turbulence (strength, frequency, scrollSpeed, damping, octaveCount, quality)
    ///   - particle_set_renderer: Set renderer (renderMode, material, sortMode, minParticleSize, maxParticleSize, etc.)
    ///   - particle_enable_module: Enable/disable modules by name
    ///   - particle_play/stop/pause/restart/clear: Playback control (withChildren optional)
    ///   - particle_add_burst: Add emission burst (time, count, cycles, interval, probability)
    ///   - particle_clear_bursts: Clear all bursts
    ///
    /// Visual Effect Graph (vfx_*):
    ///   Asset Management:
    ///   - vfx_create_asset: Create new VFX asset file (assetName, folderPath, template, overwrite)
    ///   - vfx_assign_asset: Assign VFX asset to VisualEffect component (target, assetPath)
    ///   - vfx_list_templates: List available VFX templates in project and packages
    ///   - vfx_list_assets: List all VFX assets (folder, search filters)
    ///   Runtime Control:
    ///   - vfx_get_info: Get VFX info including exposed parameters
    ///   - vfx_set_float/int/bool: Set exposed scalar parameters (parameter, value)
    ///   - vfx_set_vector2/vector3/vector4: Set exposed vector parameters (parameter, value as array)
    ///   - vfx_set_color: Set exposed color (parameter, color as [r,g,b,a])
    ///   - vfx_set_gradient: Set exposed gradient (parameter, gradient)
    ///   - vfx_set_texture: Set exposed texture (parameter, texturePath)
    ///   - vfx_set_mesh: Set exposed mesh (parameter, meshPath)
    ///   - vfx_set_curve: Set exposed animation curve (parameter, curve)
    ///   - vfx_send_event: Send event with attributes (eventName, position, velocity, color, size, lifetime)
    ///   - vfx_play/stop/pause/reinit: Playback control
    ///   - vfx_set_playback_speed: Set playback speed multiplier (playRate)
    ///   - vfx_set_seed: Set random seed (seed, resetSeedOnPlay)
    ///
    /// LineRenderer (line_*):
    ///   - line_get_info: Get line info (position count, width, color, etc.)
    ///   - line_set_positions: Set all positions (positions as [[x,y,z], ...])
    ///   - line_add_position: Add position at end (position as [x,y,z])
    ///   - line_set_position: Set specific position (index, position)
    ///   - line_set_width: Set width (width, startWidth, endWidth, widthCurve, widthMultiplier)
    ///   - line_set_color: Set color (color, gradient, startColor, endColor)
    ///   - line_set_material: Set material (materialPath)
    ///   - line_set_properties: Set renderer properties (loop, useWorldSpace, alignment, textureMode, numCornerVertices, numCapVertices, etc.)
    ///   - line_clear: Clear all positions
    ///   Shape Creation:
    ///   - line_create_line: Create simple line (start, end, segments)
    ///   - line_create_circle: Create circle (center, radius, segments, normal)
    ///   - line_create_arc: Create arc (center, radius, startAngle, endAngle, segments, normal)
    ///   - line_create_bezier: Create Bezier curve (start, end, controlPoint1, controlPoint2, segments)
    ///
    /// TrailRenderer (trail_*):
    ///   - trail_get_info: Get trail info
    ///   - trail_set_time: Set trail duration (time)
    ///   - trail_set_width: Set width (width, startWidth, endWidth, widthCurve, widthMultiplier)
    ///   - trail_set_color: Set color (color, gradient, startColor, endColor)
    ///   - trail_set_material: Set material (materialPath)
    ///   - trail_set_properties: Set properties (minVertexDistance, autodestruct, emitting, alignment, textureMode, etc.)
    ///   - trail_clear: Clear trail
    ///   - trail_emit: Emit point at current position (Unity 2021.1+)
    ///
    /// COMMON PARAMETERS:
    /// - target (string): GameObject identifier
    /// - searchMethod (string): "by_id" | "by_name" | "by_path" | "by_tag" | "by_layer"
    /// - materialPath (string): Asset path to material (e.g., "Assets/Materials/Fire.mat")
    /// - color (array): Color as [r, g, b, a] with values 0-1
    /// - position (array): 3D position as [x, y, z]
    /// - gradient (object): {colorKeys: [{color: [r,g,b,a], time: 0-1}], alphaKeys: [{alpha: 0-1, time: 0-1}]}
    /// - curve (object): {keys: [{time: 0-1, value: number, inTangent: number, outTangent: number}]}
    ///
    /// For full parameter details, refer to Unity documentation for each component type.
    /// </summary>
    [McpForUnityTool("manage_vfx", AutoRegister = false)]
    public static class ManageVFX
    {
        private static readonly Dictionary<string, string> ParamAliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "size_over_lifetime", "size" },
            { "start_color_line", "startColor" },
            { "sorting_layer_id", "sortingLayerID" },
            { "material", "materialPath" },
        };

        private static JObject NormalizeParams(JObject source)
        {
            if (source == null)
            {
                return new JObject();
            }

            var normalized = new JObject();
            var properties = ExtractProperties(source);
            if (properties != null)
            {
                foreach (var prop in properties.Properties())
                {
                    normalized[NormalizeKey(prop.Name, true)] = NormalizeToken(prop.Value);
                }
            }

            foreach (var prop in source.Properties())
            {
                if (string.Equals(prop.Name, "properties", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                normalized[NormalizeKey(prop.Name, true)] = NormalizeToken(prop.Value);
            }

            return normalized;
        }

        private static JObject ExtractProperties(JObject source)
        {
            if (source == null)
            {
                return null;
            }

            if (!source.TryGetValue("properties", StringComparison.OrdinalIgnoreCase, out var token))
            {
                return null;
            }

            if (token == null || token.Type == JTokenType.Null)
            {
                return null;
            }

            if (token is JObject obj)
            {
                return obj;
            }

            if (token.Type == JTokenType.String)
            {
                try
                {
                    return JToken.Parse(token.ToString()) as JObject;
                }
                catch (JsonException ex)
                {
                    throw new JsonException(  
                        $"Failed to parse 'properties' JSON string. Raw value: {token}",  
                        ex); 
                }
            }

            return null;
        }

        private static string NormalizeKey(string key, bool allowAliases)
        {
            if (string.IsNullOrEmpty(key))
            {
                return key;
            }
            if (string.Equals(key, "action", StringComparison.OrdinalIgnoreCase))
            {
                return "action";
            }
            if (allowAliases && ParamAliases.TryGetValue(key, out var alias))
            {
                return alias;
            }
            if (key.IndexOf('_') >= 0)
            {
                return ToCamelCase(key);
            }
            return key;
        }

        private static JToken NormalizeToken(JToken token)
        {
            if (token == null)
            {
                return null;
            }

            if (token is JObject obj)
            {
                var normalized = new JObject();
                foreach (var prop in obj.Properties())
                {
                    normalized[NormalizeKey(prop.Name, false)] = NormalizeToken(prop.Value);
                }
                return normalized;
            }

            if (token is JArray array)
            {
                var normalized = new JArray();
                foreach (var item in array)
                {
                    normalized.Add(NormalizeToken(item));
                }
                return normalized;
            }

            return token;
        }

        private static string ToCamelCase(string key) => StringCaseUtility.ToCamelCase(key);

        public static object HandleCommand(JObject @params)
        {
            JObject normalizedParams = NormalizeParams(@params);
            string action = normalizedParams["action"]?.ToString();
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
                    return HandleParticleSystemAction(normalizedParams, actionLower.Substring(9));
                }

                // VFX Graph actions (vfx_*)
                if (actionLower.StartsWith("vfx_"))
                {
                    return HandleVFXGraphAction(normalizedParams, actionLower.Substring(4));
                }

                // LineRenderer actions (line_*)
                if (actionLower.StartsWith("line_"))
                {
                    return HandleLineRendererAction(normalizedParams, actionLower.Substring(5));
                }

                // TrailRenderer actions (trail_*)
                if (actionLower.StartsWith("trail_"))
                {
                    return HandleTrailRendererAction(normalizedParams, actionLower.Substring(6));
                }

                return new { success = false, message = $"Unknown action: {action}. Actions must be prefixed with: particle_, vfx_, line_, or trail_" };
            }
            catch (Exception ex)
            {
                return new { success = false, message = ex.Message, stackTrace = ex.StackTrace };
            }
        }

        private static object HandleParticleSystemAction(JObject @params, string action)
        {
            switch (action)
            {
                case "get_info": return ParticleRead.GetInfo(@params);
                case "set_main": return ParticleWrite.SetMain(@params);
                case "set_emission": return ParticleWrite.SetEmission(@params);
                case "set_shape": return ParticleWrite.SetShape(@params);
                case "set_color_over_lifetime": return ParticleWrite.SetColorOverLifetime(@params);
                case "set_size_over_lifetime": return ParticleWrite.SetSizeOverLifetime(@params);
                case "set_velocity_over_lifetime": return ParticleWrite.SetVelocityOverLifetime(@params);
                case "set_noise": return ParticleWrite.SetNoise(@params);
                case "set_renderer": return ParticleWrite.SetRenderer(@params);
                case "enable_module": return ParticleControl.EnableModule(@params);
                case "play": return ParticleControl.Control(@params, "play");
                case "stop": return ParticleControl.Control(@params, "stop");
                case "pause": return ParticleControl.Control(@params, "pause");
                case "restart": return ParticleControl.Control(@params, "restart");
                case "clear": return ParticleControl.Control(@params, "clear");
                case "add_burst": return ParticleControl.AddBurst(@params);
                case "clear_bursts": return ParticleControl.ClearBursts(@params);
                default:
                    return new { success = false, message = $"Unknown particle action: {action}. Valid: get_info, set_main, set_emission, set_shape, set_color_over_lifetime, set_size_over_lifetime, set_velocity_over_lifetime, set_noise, set_renderer, enable_module, play, stop, pause, restart, clear, add_burst, clear_bursts" };
            }
        }

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
                case "create_asset": return VfxGraphAssets.CreateAsset(@params);
                case "assign_asset": return VfxGraphAssets.AssignAsset(@params);
                case "list_templates": return VfxGraphAssets.ListTemplates(@params);
                case "list_assets": return VfxGraphAssets.ListAssets(@params);

                // Runtime parameter control
                case "get_info": return VfxGraphRead.GetInfo(@params);
                case "set_float": return VfxGraphWrite.SetParameter<float>(@params, (vfx, n, v) => vfx.SetFloat(n, v));
                case "set_int": return VfxGraphWrite.SetParameter<int>(@params, (vfx, n, v) => vfx.SetInt(n, v));
                case "set_bool": return VfxGraphWrite.SetParameter<bool>(@params, (vfx, n, v) => vfx.SetBool(n, v));
                case "set_vector2": return VfxGraphWrite.SetVector(@params, 2);
                case "set_vector3": return VfxGraphWrite.SetVector(@params, 3);
                case "set_vector4": return VfxGraphWrite.SetVector(@params, 4);
                case "set_color": return VfxGraphWrite.SetColor(@params);
                case "set_gradient": return VfxGraphWrite.SetGradient(@params);
                case "set_texture": return VfxGraphWrite.SetTexture(@params);
                case "set_mesh": return VfxGraphWrite.SetMesh(@params);
                case "set_curve": return VfxGraphWrite.SetCurve(@params);
                case "send_event": return VfxGraphWrite.SendEvent(@params);
                case "play": return VfxGraphControl.Control(@params, "play");
                case "stop": return VfxGraphControl.Control(@params, "stop");
                case "pause": return VfxGraphControl.Control(@params, "pause");
                case "reinit": return VfxGraphControl.Control(@params, "reinit");
                case "set_playback_speed": return VfxGraphControl.SetPlaybackSpeed(@params);
                case "set_seed": return VfxGraphControl.SetSeed(@params);
                default:
                    return new { success = false, message = $"Unknown vfx action: {action}. Valid: create_asset, assign_asset, list_templates, list_assets, get_info, set_float, set_int, set_bool, set_vector2/3/4, set_color, set_gradient, set_texture, set_mesh, set_curve, send_event, play, stop, pause, reinit, set_playback_speed, set_seed" };
            }
#endif
        }


        #endregion

        private static object HandleLineRendererAction(JObject @params, string action)
        {
            switch (action)
            {
                case "get_info": return LineRead.GetInfo(@params);
                case "set_positions": return LineWrite.SetPositions(@params);
                case "add_position": return LineWrite.AddPosition(@params);
                case "set_position": return LineWrite.SetPosition(@params);
                case "set_width": return LineWrite.SetWidth(@params);
                case "set_color": return LineWrite.SetColor(@params);
                case "set_material": return LineWrite.SetMaterial(@params);
                case "set_properties": return LineWrite.SetProperties(@params);
                case "clear": return LineWrite.Clear(@params);
                case "create_line": return LineCreate.CreateLine(@params);
                case "create_circle": return LineCreate.CreateCircle(@params);
                case "create_arc": return LineCreate.CreateArc(@params);
                case "create_bezier": return LineCreate.CreateBezier(@params);
                default:
                    return new { success = false, message = $"Unknown line action: {action}. Valid: get_info, set_positions, add_position, set_position, set_width, set_color, set_material, set_properties, clear, create_line, create_circle, create_arc, create_bezier" };
            }
        }

        private static object HandleTrailRendererAction(JObject @params, string action)
        {
            switch (action)
            {
                case "get_info": return TrailRead.GetInfo(@params);
                case "set_time": return TrailWrite.SetTime(@params);
                case "set_width": return TrailWrite.SetWidth(@params);
                case "set_color": return TrailWrite.SetColor(@params);
                case "set_material": return TrailWrite.SetMaterial(@params);
                case "set_properties": return TrailWrite.SetProperties(@params);
                case "clear": return TrailControl.Clear(@params);
                case "emit": return TrailControl.Emit(@params);
                default:
                    return new { success = false, message = $"Unknown trail action: {action}. Valid: get_info, set_time, set_width, set_color, set_material, set_properties, clear, emit" };
            }
        }
    }
}
