using System;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using MCPForUnity.Editor.Helpers;

namespace MCPForUnity.Editor.Tools.Vfx
{
    internal static class ParticleControl
    {
        public static object EnableModule(JObject @params)
        {
            ParticleSystem ps = ParticleCommon.FindParticleSystem(@params);
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

        public static object Control(JObject @params, string action)
        {
            ParticleSystem ps = ParticleCommon.FindParticleSystem(@params);
            if (ps == null) return new { success = false, message = "ParticleSystem not found" };

            // Ensure material is assigned before playing
            if (action == "play" || action == "restart")
            {
                var renderer = ps.GetComponent<ParticleSystemRenderer>();
                if (renderer != null)
                {
                    RendererHelpers.EnsureMaterial(renderer);
                }
            }

            bool withChildren = @params["withChildren"]?.ToObject<bool>() ?? true;

            switch (action)
            {
                case "play": ps.Play(withChildren); break;
                case "stop": ps.Stop(withChildren, ParticleSystemStopBehavior.StopEmitting); break;
                case "pause": ps.Pause(withChildren); break;
                case "restart": ps.Stop(withChildren, ParticleSystemStopBehavior.StopEmittingAndClear); ps.Play(withChildren); break;
                case "clear": ps.Clear(withChildren); break;
                default: return new { success = false, message = $"Unknown action: {action}" };
            }

            return new { success = true, message = $"ParticleSystem {action}" };
        }

        public static object AddBurst(JObject @params)
        {
            ParticleSystem ps = ParticleCommon.FindParticleSystem(@params);
            if (ps == null) return new { success = false, message = "ParticleSystem not found" };

            // Ensure material is assigned
            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            if (renderer != null)
            {
                RendererHelpers.EnsureMaterial(renderer);
            }

            Undo.RecordObject(ps, "Add Burst");
            var emission = ps.emission;

            float time = @params["time"]?.ToObject<float>() ?? 0f;
            int minCountRaw = @params["minCount"]?.ToObject<int>() ?? @params["count"]?.ToObject<int>() ?? 30;
            int maxCountRaw = @params["maxCount"]?.ToObject<int>() ?? @params["count"]?.ToObject<int>() ?? 30;
            short minCount = (short)Math.Clamp(minCountRaw, 0, short.MaxValue);
            short maxCount = (short)Math.Clamp(maxCountRaw, 0, short.MaxValue);
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

        public static object ClearBursts(JObject @params)
        {
            ParticleSystem ps = ParticleCommon.FindParticleSystem(@params);
            if (ps == null) return new { success = false, message = "ParticleSystem not found" };

            Undo.RecordObject(ps, "Clear Bursts");
            var emission = ps.emission;
            int count = emission.burstCount;
            emission.SetBursts(new ParticleSystem.Burst[0]);

            EditorUtility.SetDirty(ps);
            return new { success = true, message = $"Cleared {count} bursts" };
        }
    }
}
