#if UNITY_BEHAVIOR
using System;
using System.Collections.Generic;
using MCPForUnity.Editor.Helpers;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using Unity.Behavior;

namespace MCPForUnity.Editor.Tools
{
    [McpForUnityTool("manage_behavior", AutoRegister = true)]
    public static class ManageBehavior
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
                    "list_agents"    => ListAgents(p),
                    "get_agent"      => GetAgent(p),
                    "list_variables" => ListVariables(p),
                    "get_variable"   => GetVariable(p),
                    "set_variable"   => SetVariable(p),
                    _ => new ErrorResponse($"Unknown action: '{action}'.")
                };
            }
            catch (Exception e)
            {
                McpLog.Error($"[ManageBehavior] Action '{action}' failed: {e}");
                return new ErrorResponse($"Internal error: {e.Message}");
            }
        }

        private static object ListAgents(ToolParams p)
        {
            var agents = UnityEngine.Object.FindObjectsByType<BehaviorGraphAgent>(FindObjectsSortMode.InstanceID);
            var pagination = PaginationRequest.FromParams(
                new JObject { ["page_size"] = p.GetInt("page_size"), ["cursor"] = p.GetInt("cursor") });

            var allItems = new List<Dictionary<string, object>>();
            foreach (var agent in agents)
            {
                allItems.Add(new Dictionary<string, object>
                {
                    ["name"] = agent.gameObject.name,
                    ["instance_id"] = agent.gameObject.GetInstanceID(),
                    ["has_graph"] = agent.Graph != null,
                    ["graph_name"] = agent.Graph != null ? agent.Graph.name : null,
                });
            }

            var page = PaginationResponse<Dictionary<string, object>>.Create(allItems, pagination);
            return new SuccessResponse($"Found {agents.Length} BehaviorGraphAgent(s).", page);
        }

        private static object GetAgent(ToolParams p)
        {
            var agent = ResolveAgent(p);
            if (agent == null) return new ErrorResponse("BehaviorGraphAgent not found.");

            return new SuccessResponse($"BehaviorGraphAgent '{agent.gameObject.name}'.", new Dictionary<string, object>
            {
                ["name"] = agent.gameObject.name,
                ["has_graph"] = agent.Graph != null,
                ["graph_name"] = agent.Graph != null ? agent.Graph.name : null,
                ["enabled"] = agent.enabled,
            });
        }

        private static object ListVariables(ToolParams p)
        {
            var agent = ResolveAgent(p);
            if (agent == null) return new ErrorResponse("BehaviorGraphAgent not found.");
            if (agent.Graph == null) return new ErrorResponse("Agent has no behavior graph assigned.");

            var blackboard = agent.Graph.BlackboardReference;
            if (blackboard == null) return new ErrorResponse("No blackboard found on graph.");

            var variables = new List<Dictionary<string, object>>();
            foreach (var variable in blackboard.Blackboard)
            {
                variables.Add(new Dictionary<string, object>
                {
                    ["name"] = variable.Name,
                    ["type"] = variable.Type?.Name ?? "Unknown",
                    ["id"] = variable.GUID.ToString(),
                });
            }

            return new SuccessResponse($"Found {variables.Count} variable(s).", new Dictionary<string, object>
            {
                ["variables"] = variables,
            });
        }

        private static object GetVariable(ToolParams p)
        {
            var agent = ResolveAgent(p);
            if (agent == null) return new ErrorResponse("BehaviorGraphAgent not found.");
            if (agent.Graph == null) return new ErrorResponse("Agent has no behavior graph assigned.");

            var nameResult = p.GetRequired("variable_name");
            if (!nameResult.IsSuccess) return new ErrorResponse(nameResult.ErrorMessage);

            var blackboard = agent.Graph.BlackboardReference;
            if (blackboard == null) return new ErrorResponse("No blackboard found on graph.");

            BlackboardVariable found = null;
            foreach (var variable in blackboard.Blackboard)
            {
                if (string.Equals(variable.Name, nameResult.Value, StringComparison.OrdinalIgnoreCase))
                {
                    found = variable;
                    break;
                }
            }

            if (found == null) return new ErrorResponse($"Variable '{nameResult.Value}' not found.");

            return new SuccessResponse($"Variable '{found.Name}'.", new Dictionary<string, object>
            {
                ["name"] = found.Name,
                ["type"] = found.Type?.Name ?? "Unknown",
                ["value"] = found.ObjectValue?.ToString(),
                ["id"] = found.GUID.ToString(),
            });
        }

        private static object SetVariable(ToolParams p)
        {
            var agent = ResolveAgent(p);
            if (agent == null) return new ErrorResponse("BehaviorGraphAgent not found.");
            if (agent.Graph == null) return new ErrorResponse("Agent has no behavior graph assigned.");

            if (!Application.isPlaying)
                return new ErrorResponse("set_variable requires Play mode.");

            var nameResult = p.GetRequired("variable_name");
            if (!nameResult.IsSuccess) return new ErrorResponse(nameResult.ErrorMessage);
            var valueResult = p.GetRequired("value");
            if (!valueResult.IsSuccess) return new ErrorResponse(valueResult.ErrorMessage);

            var blackboard = agent.Graph.BlackboardReference;
            if (blackboard == null) return new ErrorResponse("No blackboard found on graph.");

            BlackboardVariable found = null;
            foreach (var variable in blackboard.Blackboard)
            {
                if (string.Equals(variable.Name, nameResult.Value, StringComparison.OrdinalIgnoreCase))
                {
                    found = variable;
                    break;
                }
            }

            if (found == null) return new ErrorResponse($"Variable '{nameResult.Value}' not found.");

            // Attempt to set value based on type
            try
            {
                if (found.Type == typeof(float))
                    found.ObjectValue = float.Parse(valueResult.Value, System.Globalization.CultureInfo.InvariantCulture);
                else if (found.Type == typeof(int))
                    found.ObjectValue = int.Parse(valueResult.Value);
                else if (found.Type == typeof(bool))
                    found.ObjectValue = bool.Parse(valueResult.Value);
                else if (found.Type == typeof(string))
                    found.ObjectValue = valueResult.Value;
                else
                    return new ErrorResponse($"Cannot set variable of type '{found.Type?.Name}' from string.");
            }
            catch (Exception e)
            {
                return new ErrorResponse($"Failed to parse value: {e.Message}");
            }

            return new SuccessResponse($"Variable '{found.Name}' set to '{valueResult.Value}'.");
        }

        private static BehaviorGraphAgent ResolveAgent(ToolParams p)
        {
            string target = p.Get("target");
            if (string.IsNullOrEmpty(target)) return null;
            var go = ObjectResolver.ResolveGameObject(new JValue(target));
            return go != null ? go.GetComponent<BehaviorGraphAgent>() : null;
        }
    }
}
#endif
