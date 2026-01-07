using Newtonsoft.Json.Linq;
using System.Linq;
using UnityEngine;

namespace MCPForUnity.Editor.Tools.ManageVfx
{
    internal static class ParticleRead
    {
        public static object GetInfo(JObject @params)
        {
            ParticleSystem ps = ParticleCommon.FindParticleSystem(@params);
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
                    renderer = renderer != null ? new
                    {
                        renderMode = renderer.renderMode.ToString(),
                        sortMode = renderer.sortMode.ToString(),
                        material = renderer.sharedMaterial?.name,
                        trailMaterial = renderer.trailMaterial?.name,
                        minParticleSize = renderer.minParticleSize,
                        maxParticleSize = renderer.maxParticleSize,
                        shadowCastingMode = renderer.shadowCastingMode.ToString(),
                        receiveShadows = renderer.receiveShadows,
                        lightProbeUsage = renderer.lightProbeUsage.ToString(),
                        reflectionProbeUsage = renderer.reflectionProbeUsage.ToString(),
                        sortingOrder = renderer.sortingOrder,
                        sortingLayerName = renderer.sortingLayerName,
                        renderingLayerMask = renderer.renderingLayerMask
                    } : null
                }
            };
        }
    }
}
