using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using MCPForUnity.Editor.Helpers;

namespace MCPForUnity.Editor.Tools.Physics
{
    internal static class PhysicsSettingsOps
    {
        public static object Ping(JObject @params)
        {
            var gravity3d = UnityEngine.Physics.gravity;
            var gravity2d = Physics2D.gravity;

#if UNITY_2022_2_OR_NEWER
            var simMode = UnityEngine.Physics.simulationMode.ToString();
#else
            var simMode = UnityEngine.Physics.autoSimulation ? "FixedUpdate" : "Script";
#endif

            return new
            {
                success = true,
                message = "Physics tool ready.",
                data = new
                {
                    gravity3d = new[] { gravity3d.x, gravity3d.y, gravity3d.z },
                    gravity2d = new[] { gravity2d.x, gravity2d.y },
                    simulationMode = simMode,
                    defaultSolverIterations = UnityEngine.Physics.defaultSolverIterations,
                    defaultSolverVelocityIterations = UnityEngine.Physics.defaultSolverVelocityIterations,
                    bounceThreshold = UnityEngine.Physics.bounceThreshold,
                    sleepThreshold = UnityEngine.Physics.sleepThreshold,
                    defaultContactOffset = UnityEngine.Physics.defaultContactOffset,
                    queriesHitTriggers = UnityEngine.Physics.queriesHitTriggers
                }
            };
        }

        public static object GetSettings(JObject @params)
        {
            var p = new ToolParams(@params);
            string dimension = (p.Get("dimension") ?? "3d").ToLowerInvariant();

            if (dimension == "2d")
            {
                var g = Physics2D.gravity;
                return new
                {
                    success = true,
                    message = "Physics2D settings retrieved.",
                    data = new
                    {
                        dimension = "2d",
                        gravity = new[] { g.x, g.y },
                        velocityIterations = Physics2D.velocityIterations,
                        positionIterations = Physics2D.positionIterations,
                        queriesHitTriggers = Physics2D.queriesHitTriggers,
                        queriesStartInColliders = Physics2D.queriesStartInColliders,
                        callbacksOnDisable = Physics2D.callbacksOnDisable,
                        autoSyncTransforms = Physics2D.autoSyncTransforms
                    }
                };
            }

            if (dimension != "3d")
                return new ErrorResponse($"Invalid dimension: '{dimension}'. Use '3d' or '2d'.");

            var g3 = UnityEngine.Physics.gravity;
#if UNITY_2022_2_OR_NEWER
            var simMode = UnityEngine.Physics.simulationMode.ToString();
#else
            var simMode = UnityEngine.Physics.autoSimulation ? "FixedUpdate" : "Script";
#endif

            return new
            {
                success = true,
                message = "Physics3D settings retrieved.",
                data = new
                {
                    dimension = "3d",
                    gravity = new[] { g3.x, g3.y, g3.z },
                    defaultContactOffset = UnityEngine.Physics.defaultContactOffset,
                    sleepThreshold = UnityEngine.Physics.sleepThreshold,
                    defaultSolverIterations = UnityEngine.Physics.defaultSolverIterations,
                    defaultSolverVelocityIterations = UnityEngine.Physics.defaultSolverVelocityIterations,
                    bounceThreshold = UnityEngine.Physics.bounceThreshold,
                    defaultMaxAngularSpeed = UnityEngine.Physics.defaultMaxAngularSpeed,
                    queriesHitTriggers = UnityEngine.Physics.queriesHitTriggers,
                    queriesHitBackfaces = UnityEngine.Physics.queriesHitBackfaces,
                    simulationMode = simMode
                }
            };
        }

        public static object SetSettings(JObject @params)
        {
            var p = new ToolParams(@params);
            string dimension = (p.Get("dimension") ?? "3d").ToLowerInvariant();
            var settings = p.GetRaw("settings") as JObject;

            if (settings == null || settings.Count == 0)
                return new ErrorResponse("'settings' parameter is required and must be a non-empty object.");

            if (dimension != "3d" && dimension != "2d")
                return new ErrorResponse($"Invalid dimension: '{dimension}'. Use '3d' or '2d'.");

            if (dimension == "2d")
                return SetSettings2D(settings);

            return SetSettings3D(settings);
        }

        private static readonly HashSet<string> Valid3DKeys = new HashSet<string>
        {
            "gravity", "defaultcontactoffset", "sleepthreshold",
            "defaultsolveriterations", "defaultsolvervelocityiterations",
            "bouncethreshold", "defaultmaxangularspeed",
            "querieshittriggers", "querieshitbackfaces", "simulationmode"
        };

        private static object SetSettings3D(JObject settings)
        {
            // Validate all keys before applying any changes
            var unknown = new List<string>();
            foreach (var prop in settings.Properties())
            {
                if (!Valid3DKeys.Contains(prop.Name.ToLowerInvariant()))
                    unknown.Add(prop.Name);
            }
            if (unknown.Count > 0)
                return new ErrorResponse(
                    $"Unknown 3D physics setting(s): {string.Join(", ", unknown)}.");

            var changed = new List<string>();

            foreach (var prop in settings.Properties())
            {
                string key = prop.Name.ToLowerInvariant();
                switch (key)
                {
                    case "gravity":
                    {
                        var arr = prop.Value as JArray;
                        if (arr == null || arr.Count < 3)
                            return new ErrorResponse("3D gravity requires [x, y, z] array.");
                        UnityEngine.Physics.gravity = new Vector3(
                            arr[0].Value<float>(), arr[1].Value<float>(), arr[2].Value<float>());
                        changed.Add("gravity");
                        break;
                    }
                    case "defaultcontactoffset":
                        UnityEngine.Physics.defaultContactOffset = prop.Value.Value<float>();
                        changed.Add("defaultContactOffset");
                        break;
                    case "sleepthreshold":
                        UnityEngine.Physics.sleepThreshold = prop.Value.Value<float>();
                        changed.Add("sleepThreshold");
                        break;
                    case "defaultsolveriterations":
                        UnityEngine.Physics.defaultSolverIterations = prop.Value.Value<int>();
                        changed.Add("defaultSolverIterations");
                        break;
                    case "defaultsolvervelocityiterations":
                        UnityEngine.Physics.defaultSolverVelocityIterations = prop.Value.Value<int>();
                        changed.Add("defaultSolverVelocityIterations");
                        break;
                    case "bouncethreshold":
                        UnityEngine.Physics.bounceThreshold = prop.Value.Value<float>();
                        changed.Add("bounceThreshold");
                        break;
                    case "defaultmaxangularspeed":
                        UnityEngine.Physics.defaultMaxAngularSpeed = prop.Value.Value<float>();
                        changed.Add("defaultMaxAngularSpeed");
                        break;
                    case "querieshittriggers":
                        UnityEngine.Physics.queriesHitTriggers = prop.Value.Value<bool>();
                        changed.Add("queriesHitTriggers");
                        break;
                    case "querieshitbackfaces":
                        UnityEngine.Physics.queriesHitBackfaces = prop.Value.Value<bool>();
                        changed.Add("queriesHitBackfaces");
                        break;
                    case "simulationmode":
#if UNITY_2022_2_OR_NEWER
                    {
                        string modeStr = prop.Value.ToString();
                        if (System.Enum.TryParse<SimulationMode>(modeStr, true, out var mode))
                        {
                            UnityEngine.Physics.simulationMode = mode;
                            changed.Add("simulationMode");
                        }
                        else
                        {
                            return new ErrorResponse(
                                $"Invalid simulationMode: '{modeStr}'. Valid: FixedUpdate, Update, Script.");
                        }
                        break;
                    }
#else
                    {
                        string modeStr = prop.Value.ToString().ToLowerInvariant();
                        if (modeStr == "fixedupdate") UnityEngine.Physics.autoSimulation = true;
                        else if (modeStr == "script") UnityEngine.Physics.autoSimulation = false;
                        else return new ErrorResponse(
                            $"Invalid simulationMode: '{prop.Value}'. Valid: FixedUpdate, Script.");
                        changed.Add("simulationMode");
                        break;
                    }
#endif
                }
            }

            MarkDynamicsManagerDirty();

            return new
            {
                success = true,
                message = $"Updated {changed.Count} physics 3D setting(s).",
                data = new { changed }
            };
        }

        private static readonly HashSet<string> Valid2DKeys = new HashSet<string>
        {
            "gravity", "velocityiterations", "positioniterations",
            "querieshittriggers", "queriesstartincolliders",
            "callbacksondisable", "autosynctransforms"
        };

        private static object SetSettings2D(JObject settings)
        {
            // Validate all keys before applying any changes
            var unknown = new List<string>();
            foreach (var prop in settings.Properties())
            {
                if (!Valid2DKeys.Contains(prop.Name.ToLowerInvariant()))
                    unknown.Add(prop.Name);
            }
            if (unknown.Count > 0)
                return new ErrorResponse(
                    $"Unknown 2D physics setting(s): {string.Join(", ", unknown)}.");

            var changed = new List<string>();

            foreach (var prop in settings.Properties())
            {
                string key = prop.Name.ToLowerInvariant();
                switch (key)
                {
                    case "gravity":
                    {
                        var arr = prop.Value as JArray;
                        if (arr == null || arr.Count < 2)
                            return new ErrorResponse("2D gravity requires [x, y] array.");
                        Physics2D.gravity = new Vector2(
                            arr[0].Value<float>(), arr[1].Value<float>());
                        changed.Add("gravity");
                        break;
                    }
                    case "velocityiterations":
                        Physics2D.velocityIterations = prop.Value.Value<int>();
                        changed.Add("velocityIterations");
                        break;
                    case "positioniterations":
                        Physics2D.positionIterations = prop.Value.Value<int>();
                        changed.Add("positionIterations");
                        break;
                    case "querieshittriggers":
                        Physics2D.queriesHitTriggers = prop.Value.Value<bool>();
                        changed.Add("queriesHitTriggers");
                        break;
                    case "queriesstartincolliders":
                        Physics2D.queriesStartInColliders = prop.Value.Value<bool>();
                        changed.Add("queriesStartInColliders");
                        break;
                    case "callbacksondisable":
                        Physics2D.callbacksOnDisable = prop.Value.Value<bool>();
                        changed.Add("callbacksOnDisable");
                        break;
                    case "autosynctransforms":
                        Physics2D.autoSyncTransforms = prop.Value.Value<bool>();
                        changed.Add("autoSyncTransforms");
                        break;
                }
            }

            MarkPhysics2DSettingsDirty();

            return new
            {
                success = true,
                message = $"Updated {changed.Count} physics 2D setting(s).",
                data = new { changed }
            };
        }

        private static void MarkDynamicsManagerDirty()
        {
            var assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/DynamicsManager.asset");
            if (assets != null && assets.Length > 0)
                EditorUtility.SetDirty(assets[0]);
        }

        private static void MarkPhysics2DSettingsDirty()
        {
            var assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/Physics2DSettings.asset");
            if (assets != null && assets.Length > 0)
                EditorUtility.SetDirty(assets[0]);
        }
    }
}
