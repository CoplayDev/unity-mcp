using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Resources;
using Newtonsoft.Json.Linq;

namespace MCPForUnity.Editor.Tools
{
    /// <summary>
    /// Registry for all MCP command handlers via reflection.
    /// Handles both MCP tools and resources.
    /// </summary>
    public static class CommandRegistry
    {
        private static readonly Dictionary<string, Func<JObject, object>> _handlers = new();
        private static bool _initialized = false;

        /// <summary>
        /// Initialize and auto-discover all tools and resources marked with
        /// [McpForUnityTool] or [McpForUnityResource]
        /// </summary>
        public static void Initialize()
        {
            if (_initialized) return;

            AutoDiscoverCommands();
            _initialized = true;
        }

        /// <summary>
        /// Convert PascalCase or camelCase to snake_case
        /// </summary>
        private static string ToSnakeCase(string name)
        {
            if (string.IsNullOrEmpty(name)) return name;

            // Insert underscore before uppercase letters (except first)
            var s1 = Regex.Replace(name, "(.)([A-Z][a-z]+)", "$1_$2");
            var s2 = Regex.Replace(s1, "([a-z0-9])([A-Z])", "$1_$2");
            return s2.ToLower();
        }

        /// <summary>
        /// Auto-discover all types with [McpForUnityTool] or [McpForUnityResource] attributes
        /// </summary>
        private static void AutoDiscoverCommands()
        {
            try
            {
                var allTypes = AppDomain.CurrentDomain.GetAssemblies()
                    .Where(a => !a.IsDynamic)
                    .SelectMany(a =>
                    {
                        try { return a.GetTypes(); }
                        catch { return new Type[0]; }
                    })
                    .ToList();

                // Discover tools
                var toolTypes = allTypes.Where(t => t.GetCustomAttribute<McpForUnityToolAttribute>() != null);
                int toolCount = 0;
                foreach (var type in toolTypes)
                {
                    if (RegisterCommandType(type, isResource: false))
                        toolCount++;
                }

                // Discover resources
                var resourceTypes = allTypes.Where(t => t.GetCustomAttribute<McpForUnityResourceAttribute>() != null);
                int resourceCount = 0;
                foreach (var type in resourceTypes)
                {
                    if (RegisterCommandType(type, isResource: true))
                        resourceCount++;
                }

                McpLog.Info($"Auto-discovered {toolCount} tools and {resourceCount} resources ({_handlers.Count} total handlers)");
            }
            catch (Exception ex)
            {
                McpLog.Error($"Failed to auto-discover MCP commands: {ex.Message}");
            }
        }

        /// <summary>
        /// Register a command type (tool or resource) with the registry.
        /// Returns true if successfully registered, false otherwise.
        /// </summary>
        private static bool RegisterCommandType(Type type, bool isResource)
        {
            string commandName;
            string typeLabel = isResource ? "resource" : "tool";

            // Get command name from appropriate attribute
            if (isResource)
            {
                var resourceAttr = type.GetCustomAttribute<McpForUnityResourceAttribute>();
                commandName = resourceAttr.ResourceName;
            }
            else
            {
                var toolAttr = type.GetCustomAttribute<McpForUnityToolAttribute>();
                commandName = toolAttr.CommandName;
            }

            // Auto-generate command name if not explicitly provided
            if (string.IsNullOrEmpty(commandName))
            {
                commandName = ToSnakeCase(type.Name);
            }

            // Check for duplicate command names
            if (_handlers.ContainsKey(commandName))
            {
                McpLog.Warn(
                    $"Duplicate command name '{commandName}' detected. " +
                    $"{typeLabel} {type.Name} will override previously registered handler."
                );
            }

            // Find HandleCommand method
            var method = type.GetMethod(
                "HandleCommand",
                BindingFlags.Public | BindingFlags.Static,
                null,
                new[] { typeof(JObject) },
                null
            );

            if (method == null)
            {
                McpLog.Warn(
                    $"MCP {typeLabel} {type.Name} is marked with [McpForUnity{(isResource ? "Resource" : "Tool")}] " +
                    $"but has no public static HandleCommand(JObject) method"
                );
                return false;
            }

            try
            {
                var handler = (Func<JObject, object>)Delegate.CreateDelegate(
                    typeof(Func<JObject, object>),
                    method
                );
                _handlers[commandName] = handler;
                return true;
            }
            catch (Exception ex)
            {
                McpLog.Error($"Failed to register {typeLabel} {type.Name}: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Get a command handler by name
        /// </summary>
        public static Func<JObject, object> GetHandler(string commandName)
        {
            if (!_handlers.TryGetValue(commandName, out var handler))
            {
                throw new InvalidOperationException(
                    $"Unknown or unsupported command type: {commandName}"
                );
            }
            return handler;
        }
    }
}
