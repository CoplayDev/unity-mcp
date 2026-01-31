using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MCPForUnity.Editor.Constants;
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

            var toolTypes = TypeCache.GetTypesWithAttribute<McpForUnityToolAttribute>();
            foreach (var type in toolTypes)
            {
                McpForUnityToolAttribute toolAttr;
                try
                {
                    toolAttr = type.GetCustomAttribute<McpForUnityToolAttribute>();
                }
                catch (Exception ex)
                {
                    McpLog.Warn($"Failed to read [McpForUnityTool] for {type.FullName}: {ex.Message}");
                    continue;
                }

                if (toolAttr == null)
                {
                    continue;
                }

                var metadata = ExtractToolMetadata(type, toolAttr);
                if (metadata != null)
                {
                    if (_cachedTools.ContainsKey(metadata.Name))
                    {
                        McpLog.Warn($"Duplicate tool name '{metadata.Name}' from {type.FullName}; overwriting previous registration.");
                    }
                    _cachedTools[metadata.Name] = metadata;
                    EnsurePreferenceInitialized(metadata);
                }
            }

            McpLog.Info($"Discovered {_cachedTools.Count} MCP tools via reflection", false);
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

        public List<ToolMetadata> GetEnabledTools()
        {
            return DiscoverAllTools()
                .Where(tool => IsToolEnabled(tool.Name))
                .ToList();
        }

        public bool IsToolEnabled(string toolName)
        {
            if (string.IsNullOrEmpty(toolName))
            {
                return false;
            }

            string key = GetToolPreferenceKey(toolName);
            if (EditorPrefs.HasKey(key))
            {
                return EditorPrefs.GetBool(key, true);
            }

            var metadata = GetToolMetadata(toolName);
            return metadata?.AutoRegister ?? false;
        }

        public void SetToolEnabled(string toolName, bool enabled)
        {
            if (string.IsNullOrEmpty(toolName))
            {
                return;
            }

            string key = GetToolPreferenceKey(toolName);
            EditorPrefs.SetBool(key, enabled);
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

                var metadata = new ToolMetadata
                {
                    Name = toolName,
                    Description = description,
                    StructuredOutput = toolAttr.StructuredOutput,
                    Parameters = parameters,
                    ClassName = type.Name,
                    Namespace = type.Namespace ?? "",
                    AssemblyName = type.Assembly.GetName().Name,
                    AutoRegister = toolAttr.AutoRegister,
                    RequiresPolling = toolAttr.RequiresPolling,
                    PollAction = string.IsNullOrEmpty(toolAttr.PollAction) ? "status" : toolAttr.PollAction
                };

                metadata.IsBuiltIn = StringCaseUtility.IsBuiltInMcpType(
                    type, metadata.AssemblyName, "MCPForUnity.Editor.Tools");

                return metadata;

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

        private string ConvertToSnakeCase(string input) => StringCaseUtility.ToSnakeCase(input);

        public void InvalidateCache()
        {
            _cachedTools = null;
        }

        private void EnsurePreferenceInitialized(ToolMetadata metadata)
        {
            if (metadata == null || string.IsNullOrEmpty(metadata.Name))
            {
                return;
            }

            string key = GetToolPreferenceKey(metadata.Name);
            if (!EditorPrefs.HasKey(key))
            {
                bool defaultValue = metadata.AutoRegister || metadata.IsBuiltIn;
                EditorPrefs.SetBool(key, defaultValue);
                return;
            }

            if (metadata.IsBuiltIn && !metadata.AutoRegister)
            {
                bool currentValue = EditorPrefs.GetBool(key, metadata.AutoRegister);
                if (currentValue == metadata.AutoRegister)
                {
                    EditorPrefs.SetBool(key, true);
                }
            }
        }

        private static string GetToolPreferenceKey(string toolName)
        {
            return EditorPrefKeys.ToolEnabledPrefix + toolName;
        }

    }
}
