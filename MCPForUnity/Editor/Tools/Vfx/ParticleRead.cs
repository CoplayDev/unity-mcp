using Newtonsoft.Json.Linq;
using System.Linq;
using UnityEngine;

namespace MCPForUnity.Editor.Tools.Vfx
{
    internal static class ParticleRead
    {
        private static object SerializeAnimationCurve(AnimationCurve curve)
        {
            if (curve == null)
            {
                return null;
            }

            return new
            {
                keys = curve.keys.Select(k => new
                {
                    time = k.time,
                    value = k.value,
                    inTangent = k.inTangent,
                    outTangent = k.outTangent
                }).ToArray()
            };
        }

        private static object SerializeMinMaxCurve(ParticleSystem.MinMaxCurve curve)
        {
            switch (curve.mode)
            {
                case ParticleSystemCurveMode.Constant:
                    return new
                    {
                        mode = "constant",
                        value = curve.constant
                    };

                case ParticleSystemCurveMode.TwoConstants:
                    return new
                    {
                        mode = "two_constants",
                        min = curve.constantMin,
                        max = curve.constantMax
                    };

                case ParticleSystemCurveMode.Curve:
                    return new
                    {
                        mode = "curve",
                        multiplier = curve.curveMultiplier,
                        keys = curve.curve.keys.Select(k => new
                        {
                            time = k.time,
                            value = k.value,
                            inTangent = k.inTangent,
                            outTangent = k.outTangent
                        }).ToArray()
                    };

                case ParticleSystemCurveMode.TwoCurves:
                    return new
                    {
                        mode = "curve",
                        multiplier = curve.curveMultiplier,
                        keys = curve.curveMax.keys.Select(k => new
                        {
                            time = k.time,
                            value = k.value,
                            inTangent = k.inTangent,
                            outTangent = k.outTangent
                        }).ToArray(),
                        originalMode = "two_curves",
                        curveMin = SerializeAnimationCurve(curve.curveMin),
                        curveMax = SerializeAnimationCurve(curve.curveMax)
                    };

                default:
                    return new
                    {
                        mode = "constant",
                        value = curve.constant
                    };
            }
        }

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
                        startLifetime = SerializeMinMaxCurve(main.startLifetime),
                        startSpeed = SerializeMinMaxCurve(main.startSpeed),
                        startSize = SerializeMinMaxCurve(main.startSize),
                        gravityModifier = SerializeMinMaxCurve(main.gravityModifier),
                        simulationSpace = main.simulationSpace.ToString(),
                        maxParticles = main.maxParticles
                    },
                    emission = new
                    {
                        enabled = emission.enabled,
                        rateOverTime = SerializeMinMaxCurve(emission.rateOverTime),
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
