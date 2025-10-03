using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using MCPForUnity.Editor.Helpers;
using Newtonsoft.Json.Linq;

namespace MCPForUnity.Editor.Tools
{
    /// <summary>
    /// Registry for all MCP command handlers via reflection.
    /// </summary>
    public static class CommandRegistry
    {
        private static readonly Dictionary<string, Func<JObject, object>> _handlers = new();
        private static bool _initialized = false;

        /// <summary>
        /// Initialize and auto-discover all tools marked with [McpForUnityTool]
        /// </summary>
        public static void Initialize()
        {
            if (_initialized) return;

            AutoDiscoverTools();
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
        /// Auto-discover all types with [McpForUnityTool] attribute
        /// </summary>
        private static void AutoDiscoverTools()
        {
            try
            {
                var toolTypes = AppDomain.CurrentDomain.GetAssemblies()
                    .Where(a => !a.IsDynamic)
                    .SelectMany(a =>
                    {
                        try { return a.GetTypes(); }
                        catch { return new Type[0]; }
                    })
                    .Where(t => t.GetCustomAttribute<McpForUnityToolAttribute>() != null);

                foreach (var type in toolTypes)
                {
                    RegisterToolType(type);
                }

                McpLog.Info($"Auto-discovered {_handlers.Count} tools");
            }
            catch (Exception ex)
            {
                McpLog.Error($"Failed to auto-discover MCP tools: {ex.Message}");
            }
        }

        private static void RegisterToolType(Type type)
        {
            var attr = type.GetCustomAttribute<McpForUnityToolAttribute>();

            // Get command name (explicit or auto-generated)
            string commandName = attr.CommandName;
            if (string.IsNullOrEmpty(commandName))
            {
                commandName = ToSnakeCase(type.Name);
            }

            // Check for duplicate command names
            if (_handlers.ContainsKey(commandName))
            {
                McpLog.Warn(
                    $"Duplicate command name '{commandName}' detected. " +
                    $"Tool {type.Name} will override previously registered handler."
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
                    $"MCP tool {type.Name} is marked with [McpForUnityTool] " +
                    $"but has no public static HandleCommand(JObject) method"
                );
                return;
            }

            try
            {
                var handler = (Func<JObject, object>)Delegate.CreateDelegate(
                    typeof(Func<JObject, object>),
                    method
                );
                _handlers[commandName] = handler;
            }
            catch (Exception ex)
            {
                McpLog.Error($"Failed to register tool {type.Name}: {ex.Message}");
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
