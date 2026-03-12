#if UNITY_AI_NAVIGATION
using System;
using System.Collections.Generic;
using System.Globalization;
using MCPForUnity.Editor.Helpers;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using Unity.AI.Navigation;

namespace MCPForUnity.Editor.Tools
{
    /// <summary>
    /// MCP tool for Unity AI Navigation.
    /// Actions: list_surfaces, bake, clear, list_agents, get_agent, set_agent_destination,
    ///          list_obstacles, sample_position, calculate_path.
    /// Requires com.unity.ai.navigation package.
    /// </summary>
    [McpForUnityTool("manage_navigation", AutoRegister = true)]
    public static class ManageNavigation
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
                    "list_surfaces"          => ListSurfaces(p),
                    "bake"                   => Bake(p),
                    "clear"                  => Clear(p),
                    "list_agents"            => ListAgents(p),
                    "get_agent"              => GetAgent(p),
                    "set_agent_destination"  => SetAgentDestination(p),
                    "list_obstacles"         => ListObstacles(p),
                    "sample_position"        => SamplePosition(p),
                    "calculate_path"         => CalculatePath(p),
                    _ => new ErrorResponse(
                        $"Unknown action: '{action}'. Supported: list_surfaces, bake, clear, " +
                        "list_agents, get_agent, set_agent_destination, list_obstacles, " +
                        "sample_position, calculate_path")
                };
            }
            catch (Exception e)
            {
                McpLog.Error($"[ManageNavigation] Action '{action}' failed: {e}");
                return new ErrorResponse($"Internal error: {e.Message}");
            }
        }

        #region Surfaces

        private static object ListSurfaces(ToolParams p)
        {
            var surfaces = UnityEngine.Object.FindObjectsByType<NavMeshSurface>(FindObjectsSortMode.InstanceID);
            var pagination = PaginationRequest.FromParams(
                new JObject { ["page_size"] = p.GetInt("page_size"), ["cursor"] = p.GetInt("cursor") });

            var allItems = new List<Dictionary<string, object>>();
            foreach (var surface in surfaces)
            {
                allItems.Add(new Dictionary<string, object>
                {
                    ["name"] = surface.gameObject.name,
                    ["instance_id"] = surface.gameObject.GetInstanceID(),
                    ["agent_type_id"] = surface.agentTypeID,
                    ["collect_objects"] = surface.collectObjects.ToString(),
                    ["use_geometry"] = surface.useGeometry.ToString(),
                });
            }

            var page = PaginationResponse<Dictionary<string, object>>.Create(allItems, pagination);
            return new SuccessResponse($"Found {surfaces.Length} NavMeshSurface(s).", page);
        }

        private static object Bake(ToolParams p)
        {
            var targetResult = p.GetRequired("target");
            if (!targetResult.IsSuccess)
                return new ErrorResponse(targetResult.ErrorMessage);

            var go = ObjectResolver.ResolveGameObject(new JValue(targetResult.Value));
            if (go == null)
                return new ErrorResponse($"GameObject '{targetResult.Value}' not found.");

            var surface = go.GetComponent<NavMeshSurface>();
            if (surface == null)
                return new ErrorResponse($"No NavMeshSurface on '{go.name}'.");

            surface.BuildNavMesh();
            return new SuccessResponse($"NavMesh baked for '{go.name}'.");
        }

        private static object Clear(ToolParams p)
        {
            var targetResult = p.GetRequired("target");
            if (!targetResult.IsSuccess)
                return new ErrorResponse(targetResult.ErrorMessage);

            var go = ObjectResolver.ResolveGameObject(new JValue(targetResult.Value));
            if (go == null)
                return new ErrorResponse($"GameObject '{targetResult.Value}' not found.");

            var surface = go.GetComponent<NavMeshSurface>();
            if (surface == null)
                return new ErrorResponse($"No NavMeshSurface on '{go.name}'.");

            surface.RemoveData();
            return new SuccessResponse($"NavMesh cleared for '{go.name}'.");
        }

        #endregion

        #region Agents

        private static object ListAgents(ToolParams p)
        {
            var agents = UnityEngine.Object.FindObjectsByType<NavMeshAgent>(FindObjectsSortMode.InstanceID);
            var pagination = PaginationRequest.FromParams(
                new JObject { ["page_size"] = p.GetInt("page_size"), ["cursor"] = p.GetInt("cursor") });

            var allItems = new List<Dictionary<string, object>>();
            foreach (var agent in agents)
            {
                allItems.Add(new Dictionary<string, object>
                {
                    ["name"] = agent.gameObject.name,
                    ["instance_id"] = agent.gameObject.GetInstanceID(),
                    ["speed"] = Math.Round(agent.speed, 4),
                    ["radius"] = Math.Round(agent.radius, 4),
                    ["is_on_nav_mesh"] = agent.isOnNavMesh,
                    ["enabled"] = agent.enabled,
                });
            }

            var page = PaginationResponse<Dictionary<string, object>>.Create(allItems, pagination);
            return new SuccessResponse($"Found {agents.Length} NavMeshAgent(s).", page);
        }

        private static object GetAgent(ToolParams p)
        {
            var targetResult = p.GetRequired("target");
            if (!targetResult.IsSuccess)
                return new ErrorResponse(targetResult.ErrorMessage);

            var go = ObjectResolver.ResolveGameObject(new JValue(targetResult.Value));
            if (go == null)
                return new ErrorResponse($"GameObject '{targetResult.Value}' not found.");

            var agent = go.GetComponent<NavMeshAgent>();
            if (agent == null)
                return new ErrorResponse($"No NavMeshAgent on '{go.name}'.");

            var data = new Dictionary<string, object>
            {
                ["name"] = go.name,
                ["speed"] = Math.Round(agent.speed, 4),
                ["angular_speed"] = Math.Round(agent.angularSpeed, 4),
                ["acceleration"] = Math.Round(agent.acceleration, 4),
                ["stopping_distance"] = Math.Round(agent.stoppingDistance, 4),
                ["radius"] = Math.Round(agent.radius, 4),
                ["height"] = Math.Round(agent.height, 4),
                ["is_on_nav_mesh"] = agent.isOnNavMesh,
                ["has_path"] = agent.hasPath,
                ["path_pending"] = agent.pathPending,
                ["path_status"] = agent.pathStatus.ToString(),
                ["enabled"] = agent.enabled,
            };

            if (agent.isOnNavMesh)
            {
                data["remaining_distance"] = Math.Round(agent.remainingDistance, 4);
                data["destination"] = FormatVec3(agent.destination);
                data["velocity"] = FormatVec3(agent.velocity);
            }

            return new SuccessResponse($"NavMeshAgent on '{go.name}'.", data);
        }

        private static object SetAgentDestination(ToolParams p)
        {
            if (!Application.isPlaying)
                return new ErrorResponse("set_agent_destination requires Play mode.");

            var targetResult = p.GetRequired("target");
            if (!targetResult.IsSuccess)
                return new ErrorResponse(targetResult.ErrorMessage);

            if (!TryParseVector3(p.Get("position"), out var position))
                return new ErrorResponse("'position' parameter is required as 'x,y,z'.");

            var go = ObjectResolver.ResolveGameObject(new JValue(targetResult.Value));
            if (go == null)
                return new ErrorResponse($"GameObject '{targetResult.Value}' not found.");

            var agent = go.GetComponent<NavMeshAgent>();
            if (agent == null)
                return new ErrorResponse($"No NavMeshAgent on '{go.name}'.");

            agent.SetDestination(position);
            return new SuccessResponse($"Agent '{go.name}' destination set to {FormatVec3(position)}.");
        }

        #endregion

        #region Obstacles

        private static object ListObstacles(ToolParams p)
        {
            var obstacles = UnityEngine.Object.FindObjectsByType<NavMeshObstacle>(FindObjectsSortMode.InstanceID);
            var pagination = PaginationRequest.FromParams(
                new JObject { ["page_size"] = p.GetInt("page_size"), ["cursor"] = p.GetInt("cursor") });

            var allItems = new List<Dictionary<string, object>>();
            foreach (var obs in obstacles)
            {
                allItems.Add(new Dictionary<string, object>
                {
                    ["name"] = obs.gameObject.name,
                    ["instance_id"] = obs.gameObject.GetInstanceID(),
                    ["shape"] = obs.shape.ToString(),
                    ["carving"] = obs.carving,
                    ["enabled"] = obs.enabled,
                });
            }

            var page = PaginationResponse<Dictionary<string, object>>.Create(allItems, pagination);
            return new SuccessResponse($"Found {obstacles.Length} NavMeshObstacle(s).", page);
        }

        #endregion

        #region Path Queries

        private static object SamplePosition(ToolParams p)
        {
            if (!TryParseVector3(p.Get("position"), out var position))
                return new ErrorResponse("'position' parameter is required as 'x,y,z'.");

            float maxDistance = p.GetFloat("max_distance") ?? 10f;

            if (NavMesh.SamplePosition(position, out NavMeshHit hit, maxDistance, NavMesh.AllAreas))
            {
                return new SuccessResponse("Nearest NavMesh point found.", new Dictionary<string, object>
                {
                    ["position"] = FormatVec3(hit.position),
                    ["distance"] = Math.Round(hit.distance, 4),
                    ["mask"] = hit.mask,
                    ["hit"] = true,
                });
            }

            return new SuccessResponse("No NavMesh point within range.", new Dictionary<string, object>
            {
                ["hit"] = false,
                ["max_distance"] = maxDistance,
            });
        }

        private static object CalculatePath(ToolParams p)
        {
            if (!TryParseVector3(p.Get("start"), out var start))
                return new ErrorResponse("'start' parameter is required as 'x,y,z'.");
            if (!TryParseVector3(p.Get("end"), out var end))
                return new ErrorResponse("'end' parameter is required as 'x,y,z'.");

            int areaMask = p.GetInt("area_mask") ?? NavMesh.AllAreas;

            var path = new NavMeshPath();
            bool found = NavMesh.CalculatePath(start, end, areaMask, path);

            var corners = new List<string>();
            foreach (var corner in path.corners)
                corners.Add(FormatVec3(corner));

            return new SuccessResponse(
                found ? $"Path found with {path.corners.Length} corners." : "No valid path found.",
                new Dictionary<string, object>
                {
                    ["status"] = path.status.ToString(),
                    ["corner_count"] = path.corners.Length,
                    ["corners"] = corners,
                });
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

        private static string FormatVec3(Vector3 v) => $"{v.x:F3},{v.y:F3},{v.z:F3}";

        #endregion
    }
}
#endif
