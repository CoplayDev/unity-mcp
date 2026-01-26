using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using MCPForUnity.Editor.Helpers;

namespace MCPForUnity.Editor.Tools.Vfx
{
    internal static class ParticleWrite
    {
        public static object SetMain(JObject @params)
        {
            ParticleSystem ps = ParticleCommon.FindParticleSystem(@params);
            if (ps == null) return new { success = false, message = "ParticleSystem not found" };

            // Ensure material is assigned before any configuration
            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            if (renderer != null)
            {
                RendererHelpers.EnsureMaterial(renderer);
            }

            // Stop particle system if it's playing and duration needs to be changed
            bool wasPlaying = ps.isPlaying;
            bool needsStop = @params["duration"] != null && wasPlaying;
            if (needsStop)
            {
                ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            }

            Undo.RecordObject(ps, "Set ParticleSystem Main");
            var main = ps.main;
            var changes = new List<string>();

            if (@params["duration"] != null) { main.duration = @params["duration"].ToObject<float>(); changes.Add("duration"); }
            if (@params["looping"] != null) { main.loop = @params["looping"].ToObject<bool>(); changes.Add("looping"); }
            if (@params["prewarm"] != null) { main.prewarm = @params["prewarm"].ToObject<bool>(); changes.Add("prewarm"); }
            if (@params["startDelay"] != null) { main.startDelay = ParticleCommon.ParseMinMaxCurve(@params["startDelay"], 0f); changes.Add("startDelay"); }
            if (@params["startLifetime"] != null) { main.startLifetime = ParticleCommon.ParseMinMaxCurve(@params["startLifetime"], 5f); changes.Add("startLifetime"); }
            if (@params["startSpeed"] != null) { main.startSpeed = ParticleCommon.ParseMinMaxCurve(@params["startSpeed"], 5f); changes.Add("startSpeed"); }
            if (@params["startSize"] != null) { main.startSize = ParticleCommon.ParseMinMaxCurve(@params["startSize"], 1f); changes.Add("startSize"); }
            if (@params["startRotation"] != null) { main.startRotation = ParticleCommon.ParseMinMaxCurve(@params["startRotation"], 0f); changes.Add("startRotation"); }
            if (@params["startColor"] != null) { main.startColor = ParticleCommon.ParseMinMaxGradient(@params["startColor"]); changes.Add("startColor"); }
            if (@params["gravityModifier"] != null) { main.gravityModifier = ParticleCommon.ParseMinMaxCurve(@params["gravityModifier"], 0f); changes.Add("gravityModifier"); }
            if (@params["simulationSpace"] != null && Enum.TryParse<ParticleSystemSimulationSpace>(@params["simulationSpace"].ToString(), true, out var simSpace)) { main.simulationSpace = simSpace; changes.Add("simulationSpace"); }
            if (@params["scalingMode"] != null && Enum.TryParse<ParticleSystemScalingMode>(@params["scalingMode"].ToString(), true, out var scaleMode)) { main.scalingMode = scaleMode; changes.Add("scalingMode"); }
            if (@params["playOnAwake"] != null) { main.playOnAwake = @params["playOnAwake"].ToObject<bool>(); changes.Add("playOnAwake"); }
            if (@params["maxParticles"] != null) { main.maxParticles = @params["maxParticles"].ToObject<int>(); changes.Add("maxParticles"); }

            EditorUtility.SetDirty(ps);

            // Restart particle system if it was playing
            if (needsStop && wasPlaying)
            {
                ps.Play(true);
                changes.Add("(restarted after duration change)");
            }

            return new { success = true, message = $"Updated: {string.Join(", ", changes)}" };
        }

        public static object SetEmission(JObject @params)
        {
            ParticleSystem ps = ParticleCommon.FindParticleSystem(@params);
            if (ps == null) return new { success = false, message = "ParticleSystem not found" };

            // Ensure material is assigned
            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            if (renderer != null)
            {
                RendererHelpers.EnsureMaterial(renderer);
            }

            Undo.RecordObject(ps, "Set ParticleSystem Emission");
            var emission = ps.emission;
            var changes = new List<string>();

            if (@params["enabled"] != null) { emission.enabled = @params["enabled"].ToObject<bool>(); changes.Add("enabled"); }
            if (@params["rateOverTime"] != null) { emission.rateOverTime = ParticleCommon.ParseMinMaxCurve(@params["rateOverTime"], 10f); changes.Add("rateOverTime"); }
            if (@params["rateOverDistance"] != null) { emission.rateOverDistance = ParticleCommon.ParseMinMaxCurve(@params["rateOverDistance"], 0f); changes.Add("rateOverDistance"); }

            EditorUtility.SetDirty(ps);
            return new { success = true, message = $"Updated emission: {string.Join(", ", changes)}" };
        }

        public static object SetShape(JObject @params)
        {
            ParticleSystem ps = ParticleCommon.FindParticleSystem(@params);
            if (ps == null) return new { success = false, message = "ParticleSystem not found" };

            // Ensure material is assigned
            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            if (renderer != null)
            {
                RendererHelpers.EnsureMaterial(renderer);
            }

            Undo.RecordObject(ps, "Set ParticleSystem Shape");
            var shape = ps.shape;
            var changes = new List<string>();

            if (@params["enabled"] != null) { shape.enabled = @params["enabled"].ToObject<bool>(); changes.Add("enabled"); }
            if (@params["shapeType"] != null && Enum.TryParse<ParticleSystemShapeType>(@params["shapeType"].ToString(), true, out var shapeType)) { shape.shapeType = shapeType; changes.Add("shapeType"); }
            if (@params["radius"] != null) { shape.radius = @params["radius"].ToObject<float>(); changes.Add("radius"); }
            if (@params["radiusThickness"] != null) { shape.radiusThickness = @params["radiusThickness"].ToObject<float>(); changes.Add("radiusThickness"); }
            if (@params["angle"] != null) { shape.angle = @params["angle"].ToObject<float>(); changes.Add("angle"); }
            if (@params["arc"] != null) { shape.arc = @params["arc"].ToObject<float>(); changes.Add("arc"); }
            if (@params["position"] != null) { shape.position = ManageVfxCommon.ParseVector3(@params["position"]); changes.Add("position"); }
            if (@params["rotation"] != null) { shape.rotation = ManageVfxCommon.ParseVector3(@params["rotation"]); changes.Add("rotation"); }
            if (@params["scale"] != null) { shape.scale = ManageVfxCommon.ParseVector3(@params["scale"]); changes.Add("scale"); }

            EditorUtility.SetDirty(ps);
            return new { success = true, message = $"Updated shape: {string.Join(", ", changes)}" };
        }

        public static object SetColorOverLifetime(JObject @params)
        {
            ParticleSystem ps = ParticleCommon.FindParticleSystem(@params);
            if (ps == null) return new { success = false, message = "ParticleSystem not found" };

            // Ensure material is assigned
            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            if (renderer != null)
            {
                RendererHelpers.EnsureMaterial(renderer);
            }

            Undo.RecordObject(ps, "Set ParticleSystem Color Over Lifetime");
            var col = ps.colorOverLifetime;
            var changes = new List<string>();

            if (@params["enabled"] != null) { col.enabled = @params["enabled"].ToObject<bool>(); changes.Add("enabled"); }
            if (@params["color"] != null) { col.color = ParticleCommon.ParseMinMaxGradient(@params["color"]); changes.Add("color"); }

            EditorUtility.SetDirty(ps);
            return new { success = true, message = $"Updated: {string.Join(", ", changes)}" };
        }

        public static object SetSizeOverLifetime(JObject @params)
        {
            ParticleSystem ps = ParticleCommon.FindParticleSystem(@params);
            if (ps == null) return new { success = false, message = "ParticleSystem not found" };

            // Ensure material is assigned
            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            if (renderer != null)
            {
                RendererHelpers.EnsureMaterial(renderer);
            }

            Undo.RecordObject(ps, "Set ParticleSystem Size Over Lifetime");
            var sol = ps.sizeOverLifetime;
            var changes = new List<string>();

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
            if (@params["size"] != null) { sol.size = ParticleCommon.ParseMinMaxCurve(@params["size"], 1f); changes.Add("size"); }
            if (@params["sizeX"] != null) { sol.x = ParticleCommon.ParseMinMaxCurve(@params["sizeX"], 1f); changes.Add("sizeX"); }
            if (@params["sizeY"] != null) { sol.y = ParticleCommon.ParseMinMaxCurve(@params["sizeY"], 1f); changes.Add("sizeY"); }
            if (@params["sizeZ"] != null) { sol.z = ParticleCommon.ParseMinMaxCurve(@params["sizeZ"], 1f); changes.Add("sizeZ"); }

            EditorUtility.SetDirty(ps);
            return new { success = true, message = $"Updated: {string.Join(", ", changes)}" };
        }

        public static object SetVelocityOverLifetime(JObject @params)
        {
            ParticleSystem ps = ParticleCommon.FindParticleSystem(@params);
            if (ps == null) return new { success = false, message = "ParticleSystem not found" };

            // Ensure material is assigned
            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            if (renderer != null)
            {
                RendererHelpers.EnsureMaterial(renderer);
            }

            Undo.RecordObject(ps, "Set ParticleSystem Velocity Over Lifetime");
            var vol = ps.velocityOverLifetime;
            var changes = new List<string>();

            if (@params["enabled"] != null) { vol.enabled = @params["enabled"].ToObject<bool>(); changes.Add("enabled"); }
            if (@params["space"] != null && Enum.TryParse<ParticleSystemSimulationSpace>(@params["space"].ToString(), true, out var space)) { vol.space = space; changes.Add("space"); }
            if (@params["x"] != null) { vol.x = ParticleCommon.ParseMinMaxCurve(@params["x"], 0f); changes.Add("x"); }
            if (@params["y"] != null) { vol.y = ParticleCommon.ParseMinMaxCurve(@params["y"], 0f); changes.Add("y"); }
            if (@params["z"] != null) { vol.z = ParticleCommon.ParseMinMaxCurve(@params["z"], 0f); changes.Add("z"); }
            if (@params["speedModifier"] != null) { vol.speedModifier = ParticleCommon.ParseMinMaxCurve(@params["speedModifier"], 1f); changes.Add("speedModifier"); }

            EditorUtility.SetDirty(ps);
            return new { success = true, message = $"Updated: {string.Join(", ", changes)}" };
        }

        public static object SetNoise(JObject @params)
        {
            ParticleSystem ps = ParticleCommon.FindParticleSystem(@params);
            if (ps == null) return new { success = false, message = "ParticleSystem not found" };

            // Ensure material is assigned
            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            if (renderer != null)
            {
                RendererHelpers.EnsureMaterial(renderer);
            }

            Undo.RecordObject(ps, "Set ParticleSystem Noise");
            var noise = ps.noise;
            var changes = new List<string>();

            if (@params["enabled"] != null) { noise.enabled = @params["enabled"].ToObject<bool>(); changes.Add("enabled"); }
            if (@params["strength"] != null) { noise.strength = ParticleCommon.ParseMinMaxCurve(@params["strength"], 1f); changes.Add("strength"); }
            if (@params["frequency"] != null) { noise.frequency = @params["frequency"].ToObject<float>(); changes.Add("frequency"); }
            if (@params["scrollSpeed"] != null) { noise.scrollSpeed = ParticleCommon.ParseMinMaxCurve(@params["scrollSpeed"], 0f); changes.Add("scrollSpeed"); }
            if (@params["damping"] != null) { noise.damping = @params["damping"].ToObject<bool>(); changes.Add("damping"); }
            if (@params["octaveCount"] != null) { noise.octaveCount = @params["octaveCount"].ToObject<int>(); changes.Add("octaveCount"); }
            if (@params["quality"] != null && Enum.TryParse<ParticleSystemNoiseQuality>(@params["quality"].ToString(), true, out var quality)) { noise.quality = quality; changes.Add("quality"); }

            EditorUtility.SetDirty(ps);
            return new { success = true, message = $"Updated noise: {string.Join(", ", changes)}" };
        }

        public static object SetRenderer(JObject @params)
        {
            ParticleSystem ps = ParticleCommon.FindParticleSystem(@params);
            if (ps == null) return new { success = false, message = "ParticleSystem not found" };

            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            if (renderer == null) return new { success = false, message = "ParticleSystemRenderer not found" };

            // Ensure material is set before any other operations
            RendererHelpers.EnsureMaterial(renderer);

            Undo.RecordObject(renderer, "Set ParticleSystem Renderer");
            var changes = new List<string>();

            if (@params["renderMode"] != null && Enum.TryParse<ParticleSystemRenderMode>(@params["renderMode"].ToString(), true, out var renderMode)) { renderer.renderMode = renderMode; changes.Add("renderMode"); }
            if (@params["sortMode"] != null && Enum.TryParse<ParticleSystemSortMode>(@params["sortMode"].ToString(), true, out var sortMode)) { renderer.sortMode = sortMode; changes.Add("sortMode"); }

            if (@params["minParticleSize"] != null) { renderer.minParticleSize = @params["minParticleSize"].ToObject<float>(); changes.Add("minParticleSize"); }
            if (@params["maxParticleSize"] != null) { renderer.maxParticleSize = @params["maxParticleSize"].ToObject<float>(); changes.Add("maxParticleSize"); }

            if (@params["lengthScale"] != null) { renderer.lengthScale = @params["lengthScale"].ToObject<float>(); changes.Add("lengthScale"); }
            if (@params["velocityScale"] != null) { renderer.velocityScale = @params["velocityScale"].ToObject<float>(); changes.Add("velocityScale"); }
            if (@params["cameraVelocityScale"] != null) { renderer.cameraVelocityScale = @params["cameraVelocityScale"].ToObject<float>(); changes.Add("cameraVelocityScale"); }
            if (@params["normalDirection"] != null) { renderer.normalDirection = @params["normalDirection"].ToObject<float>(); changes.Add("normalDirection"); }

            if (@params["alignment"] != null && Enum.TryParse<ParticleSystemRenderSpace>(@params["alignment"].ToString(), true, out var alignment)) { renderer.alignment = alignment; changes.Add("alignment"); }
            if (@params["pivot"] != null) { renderer.pivot = ManageVfxCommon.ParseVector3(@params["pivot"]); changes.Add("pivot"); }
            if (@params["flip"] != null) { renderer.flip = ManageVfxCommon.ParseVector3(@params["flip"]); changes.Add("flip"); }
            if (@params["allowRoll"] != null) { renderer.allowRoll = @params["allowRoll"].ToObject<bool>(); changes.Add("allowRoll"); }

            if (@params["shadowBias"] != null) { renderer.shadowBias = @params["shadowBias"].ToObject<float>(); changes.Add("shadowBias"); }

            RendererHelpers.ApplyCommonRendererProperties(renderer, @params, changes);

            if (@params["materialPath"] != null)
            {
                string matPath = @params["materialPath"].ToString();
                var findInst = new JObject { ["find"] = matPath };
                Material mat = ObjectResolver.Resolve(findInst, typeof(Material)) as Material;
                if (mat != null)
                {
                    renderer.sharedMaterial = mat;
                    changes.Add($"material={mat.name}");
                }
                else
                {
                    McpLog.Warn($"Material not found at path: {matPath}. Keeping existing material.");
                }
            }

            if (@params["trailMaterialPath"] != null)
            {
                var findInst = new JObject { ["find"] = @params["trailMaterialPath"].ToString() };
                Material mat = ObjectResolver.Resolve(findInst, typeof(Material)) as Material;
                if (mat != null) { renderer.trailMaterial = mat; changes.Add("trailMaterial"); }
            }

            EditorUtility.SetDirty(renderer);
            return new { success = true, message = $"Updated renderer: {string.Join(", ", changes)}" };
        }
    }
}
