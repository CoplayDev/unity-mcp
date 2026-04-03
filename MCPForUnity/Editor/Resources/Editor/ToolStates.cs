using System;
using System.Linq;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Services;
using Newtonsoft.Json.Linq;

namespace MCPForUnity.Editor.Resources.Editor
{
    /// <summary>
    /// Returns the enabled/disabled state of all discovered tools, grouped by group name.
    /// Used by the Python server (especially in stdio mode) to sync tool visibility.
    /// </summary>
    [McpForUnityResource("get_tool_states")]
    public static class ToolStates
    {
        public static object HandleCommand(JObject @params)
        {
            try
            {
                var discovery = MCPServiceLocator.ToolDiscovery;
                var allTools = discovery.DiscoverAllTools();

                var toolsArray = new JArray();
                foreach (var tool in allTools)
                {
                    var paramsArray = new JArray();
                    if (tool.Parameters != null)
                    {
                        foreach (var p in tool.Parameters)
                        {
                            paramsArray.Add(new JObject
                            {
                                ["name"] = p.Name,
                                ["description"] = p.Description,
                                ["type"] = p.Type,
                                ["required"] = p.Required,
                                ["default_value"] = p.DefaultValue
                            });
                        }
                    }

                    toolsArray.Add(new JObject
                    {
                        ["name"] = tool.Name,
                        ["group"] = tool.Group ?? "core",
                        ["enabled"] = discovery.IsToolEnabled(tool.Name),
                        ["description"] = tool.Description,
                        ["auto_register"] = tool.AutoRegister,
                        ["is_built_in"] = tool.IsBuiltIn,
                        ["structured_output"] = tool.StructuredOutput,
                        ["requires_polling"] = tool.RequiresPolling,
                        ["poll_action"] = tool.PollAction,
                        ["max_poll_seconds"] = tool.MaxPollSeconds,
                        ["parameters"] = paramsArray
                    });
                }

                var groups = allTools
                    .GroupBy(t => t.Group ?? "core")
                    .Select(g => new JObject
                    {
                        ["name"] = g.Key,
                        ["enabled_count"] = g.Count(t => discovery.IsToolEnabled(t.Name)),
                        ["total_count"] = g.Count()
                    });

                var result = new JObject
                {
                    ["tools"] = toolsArray,
                    ["groups"] = new JArray(groups)
                };

                return new SuccessResponse("Retrieved tool states.", result);
            }
            catch (Exception e)
            {
                return new ErrorResponse($"Failed to retrieve tool states: {e.Message}");
            }
        }
    }
}
