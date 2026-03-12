using System;
using System.Collections.Generic;
using System.Globalization;
using MCPForUnity.Editor.Helpers;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace MCPForUnity.Editor.Tools
{
    /// <summary>
    /// MCP tool for Unity 2D physics.
    /// Actions: raycast, raycast_all, overlap_circle, overlap_box,
    ///          list_rigidbodies, get_rigidbody, list_colliders, get_physics2d_settings.
    /// </summary>
    [McpForUnityTool("manage_physics2d", AutoRegister = true)]
    public static class ManagePhysics2D
    {
        public static object HandleCommand(JObject @params)
        {
            if (@params == null) return new ErrorResponse("Parameters cannot be null.");
            var p = new ToolParams(@params);
            var actionResult = p.GetRequired("action");
            if (!actionResult.IsSuccess) return new ErrorResponse(actionResult.ErrorMessage);
            string action = actionResult.Value.ToLowerInvariant();

            try
            {
                return action switch
                {
                    "raycast"                => Raycast2D(p, singleHit: true),
                    "raycast_all"            => Raycast2D(p, singleHit: false),
                    "overlap_circle"         => OverlapCircle(p),
                    "overlap_box"            => OverlapBox(p),
                    "list_rigidbodies"       => ListRigidbodies(p),
                    "get_rigidbody"          => GetRigidbody(p),
                    "list_colliders"         => ListColliders(p),
                    "get_physics2d_settings" => GetPhysics2DSettings(),
                    _ => new ErrorResponse($"Unknown action: '{action}'.")
                };
            }
            catch (Exception e)
            {
                McpLog.Error($"[ManagePhysics2D] Action '{action}' failed: {e}");
                return new ErrorResponse($"Internal error: {e.Message}");
            }
        }

        #region Raycasting

        private static object Raycast2D(ToolParams p, bool singleHit)
        {
            if (!TryParseVector2(p.Get("origin"), out var origin))
                return new ErrorResponse("'origin' parameter is required (e.g. '0,10').");
            if (!TryParseVector2(p.Get("direction"), out var direction))
                return new ErrorResponse("'direction' parameter is required (e.g. '0,-1').");

            float maxDistance = p.GetFloat("max_distance") ?? 100f;
            int layerMask = p.GetInt("layer_mask") ?? -1;

            if (singleHit)
            {
                var hit = Physics2D.Raycast(origin, direction, maxDistance, layerMask);
                if (hit.collider != null)
                {
                    return new SuccessResponse("2D Raycast hit.", FormatHit2D(hit));
                }
                return new SuccessResponse("2D Raycast: no hit.", new Dictionary<string, object>
                {
                    ["hit"] = false,
                    ["origin"] = FormatVec2(origin),
                    ["direction"] = FormatVec2(direction),
                });
            }

            var hits = Physics2D.RaycastAll(origin, direction, maxDistance, layerMask);
            var results = new List<object>();
            foreach (var hit in hits)
                results.Add(FormatHit2D(hit));

            return new SuccessResponse($"2D Raycast all: {results.Count} hit(s).", new Dictionary<string, object>
            {
                ["hit_count"] = results.Count,
                ["hits"] = results,
            });
        }

        private static Dictionary<string, object> FormatHit2D(RaycastHit2D hit)
        {
            return new Dictionary<string, object>
            {
                ["point"] = FormatVec2(hit.point),
                ["normal"] = FormatVec2(hit.normal),
                ["distance"] = Math.Round(hit.distance, 4),
                ["collider_name"] = hit.collider != null ? hit.collider.name : null,
                ["gameobject_name"] = hit.collider != null ? hit.collider.gameObject.name : null,
                ["layer"] = hit.collider != null ? hit.collider.gameObject.layer : -1,
            };
        }

        #endregion

        #region Overlap Queries

        private static object OverlapCircle(ToolParams p)
        {
            if (!TryParseVector2(p.Get("center"), out var center))
                return new ErrorResponse("'center' parameter is required (e.g. '0,0').");

            float radius = p.GetFloat("radius") ?? 5f;
            int layerMask = p.GetInt("layer_mask") ?? -1;

            var colliders = Physics2D.OverlapCircleAll(center, radius, layerMask);
            return FormatOverlapResults(colliders, p, $"Overlap circle at {FormatVec2(center)} radius {radius}");
        }

        private static object OverlapBox(ToolParams p)
        {
            if (!TryParseVector2(p.Get("center"), out var center))
                return new ErrorResponse("'center' parameter is required (e.g. '0,0').");
            if (!TryParseVector2(p.Get("size"), out var size))
                return new ErrorResponse("'size' parameter is required (e.g. '10,10').");

            float angle = p.GetFloat("angle") ?? 0f;
            int layerMask = p.GetInt("layer_mask") ?? -1;

            var colliders = Physics2D.OverlapBoxAll(center, size, angle, layerMask);
            return FormatOverlapResults(colliders, p, $"Overlap box at {FormatVec2(center)}");
        }

        private static object FormatOverlapResults(Collider2D[] colliders, ToolParams p, string label)
        {
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
            return new SuccessResponse($"{label}: {allItems.Count} collider(s).", page);
        }

        #endregion

        #region Rigidbody / Collider Operations

        private static object ListRigidbodies(ToolParams p)
        {
            var rbs = UnityEngine.Object.FindObjectsByType<Rigidbody2D>(FindObjectsSortMode.InstanceID);
            var pagination = PaginationRequest.FromParams(
                new JObject { ["page_size"] = p.GetInt("page_size"), ["cursor"] = p.GetInt("cursor") });

            var allItems = new List<Dictionary<string, object>>();
            foreach (var rb in rbs)
            {
                var data = new Dictionary<string, object>
                {
                    ["name"] = rb.gameObject.name,
                    ["instance_id"] = rb.gameObject.GetInstanceID(),
                    ["body_type"] = rb.bodyType.ToString(),
                    ["mass"] = Math.Round(rb.mass, 4),
                    ["gravity_scale"] = Math.Round(rb.gravityScale, 4),
                    ["simulated"] = rb.simulated,
                };
                if (Application.isPlaying)
                {
                    data["velocity"] = FormatVec2(rb.linearVelocity);
                    data["angular_velocity"] = Math.Round(rb.angularVelocity, 4);
                }
                allItems.Add(data);
            }

            var page = PaginationResponse<Dictionary<string, object>>.Create(allItems, pagination);
            return new SuccessResponse($"Found {rbs.Length} Rigidbody2D(s).", page);
        }

        private static object GetRigidbody(ToolParams p)
        {
            var targetResult = p.GetRequired("target");
            if (!targetResult.IsSuccess) return new ErrorResponse(targetResult.ErrorMessage);

            var go = ObjectResolver.ResolveGameObject(new JValue(targetResult.Value));
            if (go == null) return new ErrorResponse($"GameObject '{targetResult.Value}' not found.");

            var rb = go.GetComponent<Rigidbody2D>();
            if (rb == null) return new ErrorResponse($"No Rigidbody2D on '{go.name}'.");

            var data = new Dictionary<string, object>
            {
                ["name"] = go.name,
                ["body_type"] = rb.bodyType.ToString(),
                ["mass"] = Math.Round(rb.mass, 4),
                ["linear_damping"] = Math.Round(rb.linearDamping, 4),
                ["angular_damping"] = Math.Round(rb.angularDamping, 4),
                ["gravity_scale"] = Math.Round(rb.gravityScale, 4),
                ["collision_detection"] = rb.collisionDetectionMode.ToString(),
                ["simulated"] = rb.simulated,
                ["constraints"] = rb.constraints.ToString(),
            };
            if (Application.isPlaying)
            {
                data["velocity"] = FormatVec2(rb.linearVelocity);
                data["angular_velocity"] = Math.Round(rb.angularVelocity, 4);
            }

            return new SuccessResponse($"Rigidbody2D on '{go.name}'.", data);
        }

        private static object ListColliders(ToolParams p)
        {
            var cols = UnityEngine.Object.FindObjectsByType<Collider2D>(FindObjectsSortMode.InstanceID);
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
            return new SuccessResponse($"Found {cols.Length} Collider2D(s).", page);
        }

        #endregion

        #region Physics2D Settings

        private static object GetPhysics2DSettings()
        {
            return new SuccessResponse("Physics2D settings.", new Dictionary<string, object>
            {
                ["gravity"] = FormatVec2(Physics2D.gravity),
                ["default_contact_offset"] = Math.Round(Physics2D.defaultContactOffset, 6),
                ["velocity_iterations"] = Physics2D.velocityIterations,
                ["position_iterations"] = Physics2D.positionIterations,
                ["queries_hit_triggers"] = Physics2D.queriesHitTriggers,
                ["queries_start_in_colliders"] = Physics2D.queriesStartInColliders,
            });
        }

        #endregion

        #region Helpers

        private static bool TryParseVector2(string str, out Vector2 result)
        {
            result = Vector2.zero;
            if (string.IsNullOrEmpty(str)) return false;
            var parts = str.Split(',');
            if (parts.Length != 2) return false;
            if (float.TryParse(parts[0].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float x) &&
                float.TryParse(parts[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float y))
            {
                result = new Vector2(x, y);
                return true;
            }
            return false;
        }

        private static string FormatVec2(Vector2 v) => $"{v.x:F3},{v.y:F3}";

        #endregion
    }
}
