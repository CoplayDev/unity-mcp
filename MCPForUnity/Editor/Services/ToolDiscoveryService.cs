using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using MCPForUnity.Editor.Constants;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Tools;
using UnityEditor;

namespace MCPForUnity.Editor.Services
{
    public class ToolDiscoveryService : IToolDiscoveryService
    {
        private Dictionary<string, ToolMetadata> _cachedTools;
        private readonly Dictionary<Type, string> _scriptPathCache = new();
    private readonly Dictionary<string, string> _summaryCache = new();

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
                            EnsurePreferenceInitialized(metadata);
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
                    AssetPath = ResolveScriptAssetPath(type),
                    AutoRegister = toolAttr.AutoRegister,
                    RequiresPolling = toolAttr.RequiresPolling,
                    PollAction = string.IsNullOrEmpty(toolAttr.PollAction) ? "status" : toolAttr.PollAction
                };

                metadata.IsBuiltIn = DetermineIsBuiltIn(type, metadata);
                if (metadata.IsBuiltIn)
                {
                    string summaryDescription = ExtractSummaryDescription(type, metadata);
                    if (!string.IsNullOrWhiteSpace(summaryDescription))
                    {
                        metadata.Description = summaryDescription;
                    }
                }
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

        private string ResolveScriptAssetPath(Type type)
        {
            if (type == null)
            {
                return null;
            }

            if (_scriptPathCache.TryGetValue(type, out var cachedPath))
            {
                return cachedPath;
            }

            string resolvedPath = null;

            try
            {
                string filter = string.IsNullOrEmpty(type.Name) ? "t:MonoScript" : $"{type.Name} t:MonoScript";
                var guids = AssetDatabase.FindAssets(filter);

                foreach (var guid in guids)
                {
                    string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                    if (string.IsNullOrEmpty(assetPath))
                    {
                        continue;
                    }

                    var script = AssetDatabase.LoadAssetAtPath<MonoScript>(assetPath);
                    if (script == null)
                    {
                        continue;
                    }

                    var scriptClass = script.GetClass();
                    if (scriptClass == type)
                    {
                            resolvedPath = assetPath.Replace('\\', '/');
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                McpLog.Warn($"Failed to resolve asset path for {type.FullName}: {ex.Message}");
            }

            _scriptPathCache[type] = resolvedPath;
            return resolvedPath;
        }

        private bool DetermineIsBuiltIn(Type type, ToolMetadata metadata)
        {
            if (metadata == null)
            {
                return false;
            }

            if (!string.IsNullOrEmpty(metadata.AssetPath))
            {
                string normalizedPath = metadata.AssetPath.Replace("\\", "/");
                string packageRoot = AssetPathUtility.GetMcpPackageRootPath();

                if (!string.IsNullOrEmpty(packageRoot))
                {
                    string normalizedRoot = packageRoot.Replace("\\", "/");
                    if (!normalizedRoot.EndsWith("/", StringComparison.Ordinal))
                    {
                        normalizedRoot += "/";
                    }

                    string builtInRoot = normalizedRoot + "Editor/Tools/";
                    if (normalizedPath.StartsWith(builtInRoot, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }

            if (!string.IsNullOrEmpty(metadata.AssemblyName) && metadata.AssemblyName.Equals("MCPForUnity.Editor", StringComparison.Ordinal))
            {
                return true;
            }

            return false;
        }

        private string ExtractSummaryDescription(Type type, ToolMetadata metadata)
        {
            if (metadata == null || string.IsNullOrEmpty(metadata.AssetPath))
            {
                return null;
            }

            if (_summaryCache.TryGetValue(metadata.AssetPath, out var cachedSummary))
            {
                return cachedSummary;
            }

            string summary = null;

            try
            {
                var monoScript = AssetDatabase.LoadAssetAtPath<MonoScript>(metadata.AssetPath);
                string scriptText = monoScript?.text;
                if (string.IsNullOrEmpty(scriptText))
                {
                    _summaryCache[metadata.AssetPath] = null;
                    return null;
                }

                string classPattern = $@"///\s*<summary>\s*(?<content>[\s\S]*?)\s*</summary>\s*(?:\[[^\]]*\]\s*)*(?:public\s+)?(?:static\s+)?class\s+{Regex.Escape(type.Name)}";
                var match = Regex.Match(scriptText, classPattern);

                if (!match.Success)
                {
                    match = Regex.Match(scriptText, @"///\s*<summary>\s*(?<content>[\s\S]*?)\s*</summary>");
                }

                if (!match.Success)
                {
                    _summaryCache[metadata.AssetPath] = null;
                    return null;
                }

                summary = match.Groups["content"].Value;
                summary = Regex.Replace(summary, @"^\s*///\s?", string.Empty, RegexOptions.Multiline);
                summary = Regex.Replace(summary, @"<[^>]+>", string.Empty);
                summary = Regex.Replace(summary, @"\s+", " ").Trim();
            }
            catch (System.Exception ex)
            {
                McpLog.Warn($"Failed to extract summary description for {type?.FullName}: {ex.Message}");
            }

            _summaryCache[metadata.AssetPath] = summary;
            return summary;
        }
    }
}
