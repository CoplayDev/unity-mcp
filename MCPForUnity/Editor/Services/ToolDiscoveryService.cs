using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Tools;
using UnityEditor;

namespace MCPForUnity.Editor.Services
{
    public class ToolDiscoveryService : IToolDiscoveryService
    {
        private Dictionary<string, ToolMetadata> _cachedTools;

        public List<ToolMetadata> DiscoverAllTools()
        {
            if (_cachedTools != null)
            {
                return _cachedTools.Values.ToList();
            }

            _cachedTools = new Dictionary<string, ToolMetadata>();

            // Scan all assemblies for [McpForUnityTool] attributes
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();

            foreach (var assembly in assemblies)
            {
                try
                {
                    var types = assembly.GetTypes();

                    foreach (var type in types)
                    {
                        var toolAttr = type.GetCustomAttribute<McpForUnityToolAttribute>();
                        if (toolAttr == null)
                            continue;

                        var metadata = ExtractToolMetadata(type, toolAttr);
                        if (metadata != null)
                        {
                            _cachedTools[metadata.Name] = metadata;
                        }
                    }
                }
                catch (Exception ex)
                {
                    // Skip assemblies that can't be reflected
                    McpLog.Info($"Skipping assembly {assembly.FullName}: {ex.Message}");
                }
            }

            McpLog.Info($"Discovered {_cachedTools.Count} MCP tools via reflection");
            return _cachedTools.Values.ToList();
        }

        public ToolMetadata GetToolMetadata(string toolName)
        {
            if (_cachedTools == null)
            {
                DiscoverAllTools();
            }

            return _cachedTools.TryGetValue(toolName, out var metadata) ? metadata : null;
        }

        private ToolMetadata ExtractToolMetadata(Type type, McpForUnityToolAttribute toolAttr)
        {
            try
            {
                // Get tool name
                string toolName = toolAttr.Name;
                if (string.IsNullOrEmpty(toolName))
                {
                    // Derive from class name: CaptureScreenshotTool -> capture_screenshot
                    toolName = ConvertToSnakeCase(type.Name.Replace("Tool", ""));
                }

                // Get description
                string description = toolAttr.Description ?? $"Tool: {toolName}";

                // Extract parameters
                var parameters = ExtractParameters(type);

                return new ToolMetadata
                {
                    Name = toolName,
                    Description = description,
                    StructuredOutput = toolAttr.StructuredOutput,
                    Parameters = parameters,
                    ClassName = type.Name,
                    Namespace = type.Namespace ?? "",
                    AutoRegister = toolAttr.AutoRegister,
                    RequiresPolling = toolAttr.RequiresPolling,
                    PollAction = string.IsNullOrEmpty(toolAttr.PollAction) ? "status" : toolAttr.PollAction
                };
            }
            catch (Exception ex)
            {
                McpLog.Error($"Failed to extract metadata for {type.Name}: {ex.Message}");
                return null;
            }
        }

        private List<ParameterMetadata> ExtractParameters(Type type)
        {
            var parameters = new List<ParameterMetadata>();

            // Look for nested Parameters class
            var parametersType = type.GetNestedType("Parameters");
            if (parametersType == null)
            {
                return parameters;
            }

            // Get all properties with [ToolParameter]
            var properties = parametersType.GetProperties(BindingFlags.Public | BindingFlags.Instance);

            foreach (var prop in properties)
            {
                var paramAttr = prop.GetCustomAttribute<ToolParameterAttribute>();
                if (paramAttr == null)
                    continue;

                string paramName = prop.Name;
                string paramType = GetParameterType(prop.PropertyType);

                parameters.Add(new ParameterMetadata
                {
                    Name = paramName,
                    Description = paramAttr.Description,
                    Type = paramType,
                    Required = paramAttr.Required,
                    DefaultValue = paramAttr.DefaultValue
                });
            }

            return parameters;
        }

        private string GetParameterType(Type type)
        {
            // Handle nullable types
            if (Nullable.GetUnderlyingType(type) != null)
            {
                type = Nullable.GetUnderlyingType(type);
            }

            // Map C# types to JSON schema types
            if (type == typeof(string))
                return "string";
            if (type == typeof(int) || type == typeof(long))
                return "integer";
            if (type == typeof(float) || type == typeof(double))
                return "number";
            if (type == typeof(bool))
                return "boolean";
            if (type.IsArray || typeof(System.Collections.IEnumerable).IsAssignableFrom(type))
                return "array";

            return "object";
        }

        private string ConvertToSnakeCase(string input)
        {
            if (string.IsNullOrEmpty(input))
                return input;

            // Convert PascalCase to snake_case
            var result = System.Text.RegularExpressions.Regex.Replace(
                input,
                "([a-z0-9])([A-Z])",
                "$1_$2"
            ).ToLower();

            return result;
        }

        public void InvalidateCache()
        {
            _cachedTools = null;
        }
    }
}
