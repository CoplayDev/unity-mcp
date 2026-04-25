using System.Collections.Generic;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Runtime.Helpers;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace MCPForUnity.Editor.Tools.Physics
{
    internal static class PhysicsSimulationOps
    {
        public static object SimulateStep(JObject @params)
        {
            var p = new ToolParams(@params);
            string dimension = (p.Get("dimension") ?? "3d").ToLowerInvariant();
            int steps = Mathf.Clamp(p.GetInt("steps") ?? 1, 1, 100);
            float stepSize = p.GetFloat("step_size") ?? Time.fixedDeltaTime;
            string targetStr = p.Get("target");
            string searchMethod = p.Get("search_method");

            if (dimension != "3d" && dimension != "2d")
                return new ErrorResponse($"Invalid dimension: '{dimension}'. Use '3d' or '2d'.");

            if (dimension == "2d")
            {
                Physics2D.SyncTransforms();
                for (int i = 0; i < steps; i++)
                    Physics2D.Simulate(stepSize);
            }
            else
            {
                UnityEngine.Physics.SyncTransforms();
#if UNITY_2022_2_OR_NEWER
                var prevMode = UnityEngine.Physics.simulationMode;
                if (prevMode != SimulationMode.Script)
                    UnityEngine.Physics.simulationMode = SimulationMode.Script;
                try
                {
                    for (int i = 0; i < steps; i++)
                        UnityEngine.Physics.Simulate(stepSize);
                }
                finally
                {
                    UnityEngine.Physics.simulationMode = prevMode;
                }
#else
                bool wasAuto = UnityEngine.Physics.autoSimulation;
                if (wasAuto)
                    UnityEngine.Physics.autoSimulation = false;
                try
                {
                    for (int i = 0; i < steps; i++)
                        UnityEngine.Physics.Simulate(stepSize);
                }
                finally
                {
                    UnityEngine.Physics.autoSimulation = wasAuto;
                }
#endif
            }

            // Collect rigidbody states after simulation
            List<object> rigidbodies;
            if (!string.IsNullOrEmpty(targetStr))
            {
                rigidbodies = CollectTargetRigidbody(targetStr, searchMethod, dimension);
            }
            else
            {
                rigidbodies = CollectActiveRigidbodies(dimension);
            }

            return new
            {
                success = true,
                message = $"Executed {steps} physics step(s) ({dimension.ToUpper()}, step_size={stepSize:F4}s).",
                data = new
                {
                    steps_executed = steps,
                    step_size = stepSize,
                    dimension,
                    rigidbodies
                }
            };
        }

        private static List<object> CollectTargetRigidbody(string targetStr, string searchMethod, string dimension)
        {
            var results = new List<object>();
            var go = GameObjectLookup.FindByTarget(JToken.FromObject(targetStr), searchMethod ?? "by_name");
            if (go == null)
                return results;

            if (dimension == "2d")
            {
                var rb2d = go.GetComponent<Rigidbody2D>();
                if (rb2d != null)
                {
                    results.Add(new
                    {
                        name = go.name,
                        instanceID = go.GetInstanceID(),
                        position = new[] { rb2d.position.x, rb2d.position.y },
#if UNITY_6000_0_OR_NEWER
                        velocity = new[] { rb2d.linearVelocity.x, rb2d.linearVelocity.y },
#else
                        velocity = new[] { rb2d.velocity.x, rb2d.velocity.y },
#endif
                        angularVelocity = rb2d.angularVelocity
                    });
                }
            }
            else
            {
                var rb = go.GetComponent<Rigidbody>();
                if (rb != null)
                {
                    results.Add(new
                    {
                        name = go.name,
                        instanceID = go.GetInstanceID(),
                        position = new[] { rb.position.x, rb.position.y, rb.position.z },
#if UNITY_6000_0_OR_NEWER
                        velocity = new[] { rb.linearVelocity.x, rb.linearVelocity.y, rb.linearVelocity.z },
#else
                        velocity = new[] { rb.velocity.x, rb.velocity.y, rb.velocity.z },
#endif
                        angularVelocity = new[] { rb.angularVelocity.x, rb.angularVelocity.y, rb.angularVelocity.z }
                    });
                }
            }

            return results;
        }

        private static List<object> CollectActiveRigidbodies(string dimension)
        {
            var results = new List<object>();
            const int maxResults = 50;

            if (dimension == "2d")
            {
                var allRb2d = UnityFindObjectsCompat.FindAll<Rigidbody2D>();
                foreach (var rb2d in allRb2d)
                {
                    if (results.Count >= maxResults) break;
                    if (rb2d.bodyType == RigidbodyType2D.Static) continue;
                    if (rb2d.IsSleeping()) continue;

                    results.Add(new
                    {
                        name = rb2d.gameObject.name,
                        instanceID = rb2d.gameObject.GetInstanceID(),
                        position = new[] { rb2d.position.x, rb2d.position.y },
#if UNITY_6000_0_OR_NEWER
                        velocity = new[] { rb2d.linearVelocity.x, rb2d.linearVelocity.y },
#else
                        velocity = new[] { rb2d.velocity.x, rb2d.velocity.y },
#endif
                        angularVelocity = rb2d.angularVelocity
                    });
                }
            }
            else
            {
                var allRb = UnityFindObjectsCompat.FindAll<Rigidbody>();
                foreach (var rb in allRb)
                {
                    if (results.Count >= maxResults) break;
                    if (rb.isKinematic) continue;
                    if (rb.IsSleeping()) continue;

                    results.Add(new
                    {
                        name = rb.gameObject.name,
                        instanceID = rb.gameObject.GetInstanceID(),
                        position = new[] { rb.position.x, rb.position.y, rb.position.z },
#if UNITY_6000_0_OR_NEWER
                        velocity = new[] { rb.linearVelocity.x, rb.linearVelocity.y, rb.linearVelocity.z },
#else
                        velocity = new[] { rb.velocity.x, rb.velocity.y, rb.velocity.z },
#endif
                        angularVelocity = new[] { rb.angularVelocity.x, rb.angularVelocity.y, rb.angularVelocity.z }
                    });
                }
            }

            return results;
        }
    }
}
