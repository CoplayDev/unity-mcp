#if UNITY_PHYSICS
using System;
using System.Collections.Generic;
using MCPForUnity.Editor.Helpers;
using Newtonsoft.Json.Linq;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;

namespace MCPForUnity.Editor.Tools
{
    /// <summary>
    /// MCP tool for Unity DOTS Physics debugging.
    /// Actions: get_physics_world, raycast, overlap_aabb, list_colliders, get_body
    /// Requires com.unity.physics package.
    /// </summary>
    [McpForUnityTool("manage_dots_physics", AutoRegister = true)]
    public static class ManageDotsPhysics
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
                    "get_physics_world" => GetPhysicsWorld(p),
                    "raycast"           => Raycast(p),
                    "overlap_aabb"      => OverlapAabb(p),
                    "list_colliders"    => ListColliders(p),
                    "get_body"          => GetBody(p),
                    _ => new ErrorResponse(
                        $"Unknown action: '{action}'. Supported: get_physics_world, raycast, " +
                        "overlap_aabb, list_colliders, get_body")
                };
            }
            catch (Exception e)
            {
                McpLog.Error($"[ManageDotsPhysics] Action '{action}' failed: {e}");
                return new ErrorResponse($"Internal error: {e.Message}");
            }
        }

        #region Physics World Info

        private static object GetPhysicsWorld(ToolParams p)
        {
            var world = ResolveWorld(p);
            if (world == null)
                return new ErrorResponse("World not found.");

            var em = world.EntityManager;

            try
            {
                using var query = em.CreateEntityQuery(typeof(PhysicsWorldSingleton));
                if (query.IsEmpty)
                    return new ErrorResponse("PhysicsWorldSingleton not found. Is the Physics simulation running?");

                var singleton = query.GetSingleton<PhysicsWorldSingleton>();
                var physicsWorld = singleton.PhysicsWorld;

                return new SuccessResponse("Physics world info.", new Dictionary<string, object>
                {
                    ["world"]              = world.Name,
                    ["num_static_bodies"]  = physicsWorld.NumStaticBodies,
                    ["num_dynamic_bodies"] = physicsWorld.NumDynamicBodies,
                    ["num_bodies"]         = physicsWorld.NumBodies,
                    ["num_joints"]         = physicsWorld.NumJoints,
                });
            }
            catch (Exception e)
            {
                return new ErrorResponse($"Failed to read physics world: {e.Message}");
            }
        }

        #endregion

        #region Queries

        private static object Raycast(ToolParams p)
        {
            var world = ResolveWorld(p);
            if (world == null)
                return new ErrorResponse("World not found.");

            // Parse origin and direction
            string originStr = p.Get("origin");
            string directionStr = p.Get("direction");
            if (string.IsNullOrEmpty(originStr))
                return new ErrorResponse("'origin' parameter is required (e.g. '0,1,0').");
            if (string.IsNullOrEmpty(directionStr))
                return new ErrorResponse("'direction' parameter is required (e.g. '0,-1,0').");

            float maxDistance = p.GetFloat("max_distance") ?? 100f;

            if (!TryParseFloat3(originStr, out float3 origin))
                return new ErrorResponse($"Invalid origin format: '{originStr}'. Expected 'x,y,z'.");
            if (!TryParseFloat3(directionStr, out float3 direction))
                return new ErrorResponse($"Invalid direction format: '{directionStr}'. Expected 'x,y,z'.");

            var em = world.EntityManager;

            try
            {
                using var query = em.CreateEntityQuery(typeof(PhysicsWorldSingleton));
                if (query.IsEmpty)
                    return new ErrorResponse("PhysicsWorldSingleton not found.");

                var singleton = query.GetSingleton<PhysicsWorldSingleton>();
                var collisionWorld = singleton.CollisionWorld;

                float3 end = origin + math.normalize(direction) * maxDistance;

                var input = new RaycastInput
                {
                    Start = origin,
                    End = end,
                    Filter = CollisionFilter.Default
                };

                // Collect all hits
                var hits = new NativeList<RaycastHit>(Allocator.Temp);
                bool hasHits = collisionWorld.CastRay(input, ref hits);

                var results = new List<object>();
                for (int i = 0; i < hits.Length; i++)
                {
                    var hit = hits[i];
                    results.Add(new Dictionary<string, object>
                    {
                        ["entity_index"]    = hit.Entity.Index,
                        ["entity_version"]  = hit.Entity.Version,
                        ["fraction"]        = System.Math.Round(hit.Fraction, 4),
                        ["position"]        = FormatFloat3(hit.Position),
                        ["surface_normal"]  = FormatFloat3(hit.SurfaceNormal),
                        ["body_index"]      = hit.RigidBodyIndex,
                    });
                }
                hits.Dispose();

                return new SuccessResponse(
                    $"Raycast from {FormatFloat3(origin)} dir {FormatFloat3(direction)}: {results.Count} hit(s).",
                    new Dictionary<string, object>
                    {
                        ["hit_count"]    = results.Count,
                        ["origin"]       = FormatFloat3(origin),
                        ["direction"]    = FormatFloat3(direction),
                        ["max_distance"] = maxDistance,
                        ["hits"]         = results
                    });
            }
            catch (Exception e)
            {
                return new ErrorResponse($"Raycast failed: {e.Message}");
            }
        }

        private static object OverlapAabb(ToolParams p)
        {
            var world = ResolveWorld(p);
            if (world == null)
                return new ErrorResponse("World not found.");

            string minStr = p.Get("min");
            string maxStr = p.Get("max");
            if (string.IsNullOrEmpty(minStr))
                return new ErrorResponse("'min' parameter is required (e.g. '-5,-5,-5').");
            if (string.IsNullOrEmpty(maxStr))
                return new ErrorResponse("'max' parameter is required (e.g. '5,5,5').");

            if (!TryParseFloat3(minStr, out float3 aabbMin))
                return new ErrorResponse($"Invalid min format: '{minStr}'. Expected 'x,y,z'.");
            if (!TryParseFloat3(maxStr, out float3 aabbMax))
                return new ErrorResponse($"Invalid max format: '{maxStr}'. Expected 'x,y,z'.");

            var em = world.EntityManager;

            try
            {
                using var query = em.CreateEntityQuery(typeof(PhysicsWorldSingleton));
                if (query.IsEmpty)
                    return new ErrorResponse("PhysicsWorldSingleton not found.");

                var singleton = query.GetSingleton<PhysicsWorldSingleton>();
                var collisionWorld = singleton.CollisionWorld;

                var input = new OverlapAabbInput
                {
                    Aabb = new Aabb { Min = aabbMin, Max = aabbMax },
                    Filter = CollisionFilter.Default
                };

                var bodyIndices = new NativeList<int>(Allocator.Temp);
                collisionWorld.OverlapAabb(input, ref bodyIndices);

                int pageSize = p.GetInt("page_size") ?? 20;
                int sampleCount = System.Math.Min(pageSize, bodyIndices.Length);

                var results = new List<object>();
                for (int i = 0; i < sampleCount; i++)
                {
                    int bodyIdx = bodyIndices[i];
                    var body = collisionWorld.Bodies[bodyIdx];
                    results.Add(new Dictionary<string, object>
                    {
                        ["body_index"]     = bodyIdx,
                        ["entity_index"]   = body.Entity.Index,
                        ["entity_version"] = body.Entity.Version,
                    });
                }
                int totalHits = bodyIndices.Length;
                bodyIndices.Dispose();

                return new SuccessResponse(
                    $"AABB overlap [{FormatFloat3(aabbMin)}] to [{FormatFloat3(aabbMax)}]: {totalHits} body(ies).",
                    new Dictionary<string, object>
                    {
                        ["total_hits"] = totalHits,
                        ["returned"]   = results.Count,
                        ["bodies"]     = results
                    });
            }
            catch (Exception e)
            {
                return new ErrorResponse($"Overlap AABB failed: {e.Message}");
            }
        }

        #endregion

        #region Body Inspection

        private static object ListColliders(ToolParams p)
        {
            var world = ResolveWorld(p);
            if (world == null)
                return new ErrorResponse("World not found.");

            var em = world.EntityManager;
            int pageSize = p.GetInt("page_size") ?? 20;
            pageSize = System.Math.Clamp(pageSize, 1, 100);

            using var query = em.CreateEntityQuery(typeof(PhysicsCollider));
            int totalCount = query.CalculateEntityCount();

            var entities = query.ToEntityArray(Allocator.Temp);
            int sampleCount = System.Math.Min(pageSize, entities.Length);

            var results = new List<object>();
            for (int i = 0; i < sampleCount; i++)
            {
                var entity = entities[i];
                var data = new Dictionary<string, object>
                {
                    ["entity_index"]   = entity.Index,
                    ["entity_version"] = entity.Version,
                };

                try
                {
                    var collider = em.GetComponentData<PhysicsCollider>(entity);
                    if (collider.IsValid)
                    {
                        data["collider_type"] = collider.Value.Value.Type.ToString();
                    }
                }
                catch
                {
                    data["collider_type"] = "<unreadable>";
                }

                results.Add(data);
            }
            entities.Dispose();

            return new SuccessResponse(
                $"Found {totalCount} entities with PhysicsCollider in '{world.Name}'.",
                new Dictionary<string, object>
                {
                    ["total_count"] = totalCount,
                    ["returned"]    = results.Count,
                    ["colliders"]   = results
                });
        }

        private static object GetBody(ToolParams p)
        {
            var world = ResolveWorld(p);
            if (world == null)
                return new ErrorResponse("World not found.");

            int? bodyIndex = p.GetInt("body_index");
            if (bodyIndex == null)
                return new ErrorResponse("'body_index' parameter is required.");

            var em = world.EntityManager;

            try
            {
                using var query = em.CreateEntityQuery(typeof(PhysicsWorldSingleton));
                if (query.IsEmpty)
                    return new ErrorResponse("PhysicsWorldSingleton not found.");

                var singleton = query.GetSingleton<PhysicsWorldSingleton>();
                var physicsWorld = singleton.PhysicsWorld;

                if (bodyIndex.Value < 0 || bodyIndex.Value >= physicsWorld.NumBodies)
                    return new ErrorResponse($"Body index {bodyIndex} out of range (0-{physicsWorld.NumBodies - 1}).");

                var body = physicsWorld.Bodies[bodyIndex.Value];
                bool isDynamic = bodyIndex.Value < physicsWorld.NumDynamicBodies;

                var result = new Dictionary<string, object>
                {
                    ["body_index"]     = bodyIndex.Value,
                    ["entity_index"]   = body.Entity.Index,
                    ["entity_version"] = body.Entity.Version,
                    ["is_dynamic"]     = isDynamic,
                    ["position"]       = FormatFloat3(body.WorldFromBody.pos),
                    ["rotation"]       = FormatQuat(body.WorldFromBody.rot),
                };

                if (body.Collider.IsCreated)
                {
                    result["collider_type"] = body.Collider.Value.Type.ToString();
                }

                if (isDynamic)
                {
                    var motionVelocity = physicsWorld.MotionVelocities[bodyIndex.Value];
                    result["linear_velocity"]  = FormatFloat3(motionVelocity.LinearVelocity);
                    result["angular_velocity"] = FormatFloat3(motionVelocity.AngularVelocity);
                    result["inverse_mass"]     = System.Math.Round(motionVelocity.InverseMass, 6);
                }

                return new SuccessResponse($"Physics body {bodyIndex} details.", result);
            }
            catch (Exception e)
            {
                return new ErrorResponse($"Failed to get body: {e.Message}");
            }
        }

        #endregion

        #region Helpers

        private static World ResolveWorld(ToolParams p)
        {
            string worldName = p.Get("world");
            if (string.IsNullOrEmpty(worldName))
                return World.DefaultGameObjectInjectionWorld;

            foreach (var w in World.All)
            {
                if (string.Equals(w.Name, worldName, StringComparison.OrdinalIgnoreCase))
                    return w;
            }
            return null;
        }

        private static bool TryParseFloat3(string str, out float3 result)
        {
            result = float3.zero;
            if (string.IsNullOrEmpty(str)) return false;

            var parts = str.Split(',');
            if (parts.Length != 3) return false;

            if (float.TryParse(parts[0].Trim(), System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float x) &&
                float.TryParse(parts[1].Trim(), System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float y) &&
                float.TryParse(parts[2].Trim(), System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float z))
            {
                result = new float3(x, y, z);
                return true;
            }
            return false;
        }

        private static string FormatFloat3(float3 v) =>
            $"{v.x:F3},{v.y:F3},{v.z:F3}";

        private static string FormatQuat(quaternion q) =>
            $"{q.value.x:F3},{q.value.y:F3},{q.value.z:F3},{q.value.w:F3}";

        #endregion
    }
}
#endif
