using System;
using System.Collections.Generic;
using System.Globalization;
using MCPForUnity.Editor.Helpers;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace MCPForUnity.Editor.Tools
{
    /// <summary>
    /// MCP tool for classic Unity 3D physics.
    /// Actions: raycast, raycast_all, overlap_sphere, overlap_box,
    ///          list_rigidbodies, get_rigidbody, set_rigidbody,
    ///          list_colliders, get_physics_settings, set_physics_settings.
    /// </summary>
    [McpForUnityTool("manage_physics", AutoRegister = true)]
    public static class ManagePhysics
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
                    "raycast"              => Raycast(p, singleHit: true),
                    "raycast_all"          => Raycast(p, singleHit: false),
                    "overlap_sphere"       => OverlapSphere(p),
                    "overlap_box"          => OverlapBox(p),
                    "list_rigidbodies"     => ListRigidbodies(p),
                    "get_rigidbody"        => GetRigidbody(p),
                    "set_rigidbody"        => SetRigidbody(p),
                    "list_colliders"       => ListColliders(p),
                    "get_physics_settings" => GetPhysicsSettings(),
                    "set_physics_settings" => SetPhysicsSettings(p),
                    _ => new ErrorResponse(
                        $"Unknown action: '{action}'. Supported: raycast, raycast_all, overlap_sphere, " +
                        "overlap_box, list_rigidbodies, get_rigidbody, set_rigidbody, list_colliders, " +
                        "get_physics_settings, set_physics_settings")
                };
            }
            catch (Exception e)
            {
                McpLog.Error($"[ManagePhysics] Action '{action}' failed: {e}");
                return new ErrorResponse($"Internal error: {e.Message}");
            }
        }

        #region Raycasting

        private static object Raycast(ToolParams p, bool singleHit)
        {
            if (!TryParseVector3(p.Get("origin"), out var origin))
                return new ErrorResponse("'origin' parameter is required (e.g. '0,10,0').");
            if (!TryParseVector3(p.Get("direction"), out var direction))
                return new ErrorResponse("'direction' parameter is required (e.g. '0,-1,0').");

            float maxDistance = p.GetFloat("max_distance") ?? 100f;
            int layerMask = p.GetInt("layer_mask") ?? -1;

            if (singleHit)
            {
                if (Physics.Raycast(origin, direction, out RaycastHit hit, maxDistance, layerMask))
                {
                    return new SuccessResponse("Raycast hit.", FormatHit(hit));
                }
                return new SuccessResponse("Raycast: no hit.", new Dictionary<string, object>
                {
                    ["hit"] = false,
                    ["origin"] = FormatVec3(origin),
                    ["direction"] = FormatVec3(direction),
                    ["max_distance"] = maxDistance
                });
            }

            // raycast_all
            var hits = Physics.RaycastAll(origin, direction, maxDistance, layerMask);
            var results = new List<object>();
            foreach (var hit in hits)
                results.Add(FormatHit(hit));

            return new SuccessResponse(
                $"Raycast all: {results.Count} hit(s).",
                new Dictionary<string, object>
                {
                    ["hit_count"] = results.Count,
                    ["origin"] = FormatVec3(origin),
                    ["direction"] = FormatVec3(direction),
                    ["max_distance"] = maxDistance,
                    ["hits"] = results
                });
        }

        private static Dictionary<string, object> FormatHit(RaycastHit hit)
        {
            return new Dictionary<string, object>
            {
                ["point"] = FormatVec3(hit.point),
                ["normal"] = FormatVec3(hit.normal),
                ["distance"] = Math.Round(hit.distance, 4),
                ["collider_name"] = hit.collider != null ? hit.collider.name : null,
                ["gameobject_name"] = hit.collider != null ? hit.collider.gameObject.name : null,
                ["layer"] = hit.collider != null ? hit.collider.gameObject.layer : -1,
            };
        }

        #endregion

        #region Overlap Queries

        private static object OverlapSphere(ToolParams p)
        {
            if (!TryParseVector3(p.Get("center"), out var center))
                return new ErrorResponse("'center' parameter is required (e.g. '0,0,0').");

            float radius = p.GetFloat("radius") ?? 5f;
            int layerMask = p.GetInt("layer_mask") ?? -1;

            var colliders = Physics.OverlapSphere(center, radius, layerMask);
            var pagination = PaginationRequest.FromParams(
                new JObject { ["page_size"] = p.GetInt("page_size"), ["cursor"] = p.GetInt("cursor") });

            var allItems = new List<Dictionary<string, object>>();
            foreach (var col in colliders)
            {
                allItems.Add(new Dictionary<string, object>
                {
                    ["gameobject_name"] = col.gameObject.name,
                    ["collider_type"] = col.GetType().Name,
                    ["layer"] = col.gameObject.layer,
                    ["instance_id"] = col.gameObject.GetInstanceID(),
                });
            }

            var page = PaginationResponse<Dictionary<string, object>>.Create(allItems, pagination);
            return new SuccessResponse(
                $"Overlap sphere at {FormatVec3(center)} radius {radius}: {allItems.Count} collider(s).",
                page);
        }

        private static object OverlapBox(ToolParams p)
        {
            if (!TryParseVector3(p.Get("center"), out var center))
                return new ErrorResponse("'center' parameter is required (e.g. '0,0,0').");
            if (!TryParseVector3(p.Get("half_extents"), out var halfExtents))
                return new ErrorResponse("'half_extents' parameter is required (e.g. '5,5,5').");

            int layerMask = p.GetInt("layer_mask") ?? -1;

            var colliders = Physics.OverlapBox(center, halfExtents, Quaternion.identity, layerMask);
            var pagination = PaginationRequest.FromParams(
                new JObject { ["page_size"] = p.GetInt("page_size"), ["cursor"] = p.GetInt("cursor") });

            var allItems = new List<Dictionary<string, object>>();
            foreach (var col in colliders)
            {
                allItems.Add(new Dictionary<string, object>
                {
                    ["gameobject_name"] = col.gameObject.name,
                    ["collider_type"] = col.GetType().Name,
                    ["layer"] = col.gameObject.layer,
                    ["instance_id"] = col.gameObject.GetInstanceID(),
                });
            }

            var page = PaginationResponse<Dictionary<string, object>>.Create(allItems, pagination);
            return new SuccessResponse(
                $"Overlap box at {FormatVec3(center)}: {allItems.Count} collider(s).",
                page);
        }

        #endregion

        #region Rigidbody Operations

        private static object ListRigidbodies(ToolParams p)
        {
            var rbs = UnityEngine.Object.FindObjectsByType<Rigidbody>(FindObjectsSortMode.InstanceID);
            var pagination = PaginationRequest.FromParams(
                new JObject { ["page_size"] = p.GetInt("page_size"), ["cursor"] = p.GetInt("cursor") });

            var allItems = new List<Dictionary<string, object>>();
            foreach (var rb in rbs)
            {
                var data = new Dictionary<string, object>
                {
                    ["name"] = rb.gameObject.name,
                    ["instance_id"] = rb.gameObject.GetInstanceID(),
                    ["mass"] = Math.Round(rb.mass, 4),
                    ["is_kinematic"] = rb.isKinematic,
                    ["use_gravity"] = rb.useGravity,
                    ["is_sleeping"] = rb.IsSleeping(),
                };
                if (Application.isPlaying)
                {
                    data["velocity"] = FormatVec3(rb.linearVelocity);
                    data["angular_velocity"] = FormatVec3(rb.angularVelocity);
                }
                allItems.Add(data);
            }

            var page = PaginationResponse<Dictionary<string, object>>.Create(allItems, pagination);
            return new SuccessResponse($"Found {rbs.Length} Rigidbody component(s).", page);
        }

        private static object GetRigidbody(ToolParams p)
        {
            var targetResult = p.GetRequired("target");
            if (!targetResult.IsSuccess)
                return new ErrorResponse(targetResult.ErrorMessage);

            var go = ObjectResolver.ResolveGameObject(new JValue(targetResult.Value));
            if (go == null)
                return new ErrorResponse($"GameObject '{targetResult.Value}' not found.");

            var rb = go.GetComponent<Rigidbody>();
            if (rb == null)
                return new ErrorResponse($"No Rigidbody on '{go.name}'.");

            var data = new Dictionary<string, object>
            {
                ["name"] = go.name,
                ["instance_id"] = go.GetInstanceID(),
                ["mass"] = Math.Round(rb.mass, 4),
                ["drag"] = Math.Round(rb.linearDamping, 4),
                ["angular_drag"] = Math.Round(rb.angularDamping, 4),
                ["is_kinematic"] = rb.isKinematic,
                ["use_gravity"] = rb.useGravity,
                ["interpolation"] = rb.interpolation.ToString(),
                ["collision_detection"] = rb.collisionDetectionMode.ToString(),
                ["constraints"] = rb.constraints.ToString(),
                ["is_sleeping"] = rb.IsSleeping(),
            };
            if (Application.isPlaying)
            {
                data["velocity"] = FormatVec3(rb.linearVelocity);
                data["angular_velocity"] = FormatVec3(rb.angularVelocity);
            }

            return new SuccessResponse($"Rigidbody on '{go.name}'.", data);
        }

        private static object SetRigidbody(ToolParams p)
        {
            var targetResult = p.GetRequired("target");
            if (!targetResult.IsSuccess)
                return new ErrorResponse(targetResult.ErrorMessage);

            var go = ObjectResolver.ResolveGameObject(new JValue(targetResult.Value));
            if (go == null)
                return new ErrorResponse($"GameObject '{targetResult.Value}' not found.");

            var rb = go.GetComponent<Rigidbody>();
            if (rb == null)
                return new ErrorResponse($"No Rigidbody on '{go.name}'.");

            var propsToken = p.GetRaw("properties");
            if (propsToken == null)
                return new ErrorResponse("'properties' parameter is required.");

            JObject props;
            if (propsToken.Type == JTokenType.String)
            {
                try { props = JObject.Parse(propsToken.ToString()); }
                catch { return new ErrorResponse("'properties' must be valid JSON."); }
            }
            else
            {
                props = propsToken as JObject;
                if (props == null) return new ErrorResponse("'properties' must be a JSON object.");
            }

            UnityEditor.Undo.RecordObject(rb, "MCP SetRigidbody");

            if (props["mass"] != null) rb.mass = props["mass"].ToObject<float>();
            if (props["drag"] != null) rb.linearDamping = props["drag"].ToObject<float>();
            if (props["angular_drag"] != null) rb.angularDamping = props["angular_drag"].ToObject<float>();
            if (props["is_kinematic"] != null) rb.isKinematic = props["is_kinematic"].ToObject<bool>();
            if (props["use_gravity"] != null) rb.useGravity = props["use_gravity"].ToObject<bool>();

            UnityEditor.EditorUtility.SetDirty(rb);
            return new SuccessResponse($"Rigidbody on '{go.name}' updated.");
        }

        #endregion

        #region Colliders

        private static object ListColliders(ToolParams p)
        {
            var cols = UnityEngine.Object.FindObjectsByType<Collider>(FindObjectsSortMode.InstanceID);
            var pagination = PaginationRequest.FromParams(
                new JObject { ["page_size"] = p.GetInt("page_size"), ["cursor"] = p.GetInt("cursor") });

            var allItems = new List<Dictionary<string, object>>();
            foreach (var col in cols)
            {
                allItems.Add(new Dictionary<string, object>
                {
                    ["gameobject_name"] = col.gameObject.name,
                    ["collider_type"] = col.GetType().Name,
                    ["is_trigger"] = col.isTrigger,
                    ["enabled"] = col.enabled,
                    ["instance_id"] = col.gameObject.GetInstanceID(),
                });
            }

            var page = PaginationResponse<Dictionary<string, object>>.Create(allItems, pagination);
            return new SuccessResponse($"Found {cols.Length} Collider component(s).", page);
        }

        #endregion

        #region Physics Settings

        private static object GetPhysicsSettings()
        {
            return new SuccessResponse("Physics settings.", new Dictionary<string, object>
            {
                ["gravity"] = FormatVec3(Physics.gravity),
                ["default_solver_iterations"] = Physics.defaultSolverIterations,
                ["default_solver_velocity_iterations"] = Physics.defaultSolverVelocityIterations,
                ["sleep_threshold"] = Math.Round(Physics.sleepThreshold, 6),
                ["bounce_threshold"] = Math.Round(Physics.bounceThreshold, 4),
                ["default_contact_offset"] = Math.Round(Physics.defaultContactOffset, 6),
                ["auto_simulation"] = Physics.simulationMode.ToString(),
                ["queries_hit_triggers"] = Physics.queriesHitTriggers,
            });
        }

        private static object SetPhysicsSettings(ToolParams p)
        {
            var propsToken = p.GetRaw("properties");
            if (propsToken == null)
                return new ErrorResponse("'properties' parameter is required.");

            JObject props;
            if (propsToken.Type == JTokenType.String)
            {
                try { props = JObject.Parse(propsToken.ToString()); }
                catch { return new ErrorResponse("'properties' must be valid JSON."); }
            }
            else
            {
                props = propsToken as JObject;
                if (props == null) return new ErrorResponse("'properties' must be a JSON object.");
            }

            if (props["gravity"] != null)
            {
                var grav = VectorParsing.ParseVector3(props["gravity"]);
                if (grav.HasValue) Physics.gravity = grav.Value;
            }
            if (props["default_solver_iterations"] != null)
                Physics.defaultSolverIterations = props["default_solver_iterations"].ToObject<int>();
            if (props["sleep_threshold"] != null)
                Physics.sleepThreshold = props["sleep_threshold"].ToObject<float>();
            if (props["bounce_threshold"] != null)
                Physics.bounceThreshold = props["bounce_threshold"].ToObject<float>();
            if (props["queries_hit_triggers"] != null)
                Physics.queriesHitTriggers = props["queries_hit_triggers"].ToObject<bool>();

            return new SuccessResponse("Physics settings updated.");
        }

        #endregion

        #region Helpers

        private static bool TryParseVector3(string str, out Vector3 result)
        {
            result = Vector3.zero;
            if (string.IsNullOrEmpty(str)) return false;

            var parts = str.Split(',');
            if (parts.Length != 3) return false;

            if (float.TryParse(parts[0].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float x) &&
                float.TryParse(parts[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float y) &&
                float.TryParse(parts[2].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float z))
            {
                result = new Vector3(x, y, z);
                return true;
            }
            return false;
        }

        private static string FormatVec3(Vector3 v) =>
            $"{v.x:F3},{v.y:F3},{v.z:F3}";

        #endregion
    }
}
