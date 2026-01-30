using System;
using System.Linq;
using System.Text.RegularExpressions;

namespace MCPForUnity.Editor.Helpers
{
    /// <summary>
    /// Utility class for converting between naming conventions (snake_case, camelCase).
    /// Consolidates previously duplicated implementations from ToolParams, ManageVFX,
    /// BatchExecute, CommandRegistry, and ToolDiscoveryService.
    /// </summary>
    public static class StringCaseUtility
    {
        /// <summary>
        /// Checks whether a type belongs to the built-in MCP for Unity package.
        /// Returns true when the type's namespace starts with
        /// <paramref name="builtInNamespacePrefix"/> or its assembly is MCPForUnity.Editor.
        /// </summary>
        public static bool IsBuiltInMcpType(Type type, string assemblyName, string builtInNamespacePrefix)
        {
            if (type != null && !string.IsNullOrEmpty(type.Namespace)
                && type.Namespace.StartsWith(builtInNamespacePrefix, StringComparison.Ordinal))
            {
                return true;
            }

            if (!string.IsNullOrEmpty(assemblyName)
                && assemblyName.Equals("MCPForUnity.Editor", StringComparison.Ordinal))
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// Converts a camelCase string to snake_case.
        /// Example: "searchMethod" -> "search_method", "param1Value" -> "param1_value"
        /// </summary>
        /// <param name="str">The camelCase string to convert</param>
        /// <returns>The snake_case equivalent, or original string if null/empty</returns>
        public static string ToSnakeCase(string str)
        {
            if (string.IsNullOrEmpty(str))
                return str;

            return Regex.Replace(str, "([a-z0-9])([A-Z])", "$1_$2").ToLowerInvariant();
        }

        /// <summary>
        /// Converts a snake_case string to camelCase.
        /// Example: "search_method" -> "searchMethod"
        /// </summary>
        /// <param name="str">The snake_case string to convert</param>
        /// <returns>The camelCase equivalent, or original string if null/empty or no underscores</returns>
        public static string ToCamelCase(string str)
        {
            if (string.IsNullOrEmpty(str) || !str.Contains("_"))
                return str;

            var parts = str.Split('_');
            if (parts.Length == 0)
                return str;

            // First part stays lowercase, rest get capitalized
            var first = parts[0];
            var rest = string.Concat(parts.Skip(1).Select(part =>
                string.IsNullOrEmpty(part) ? "" : char.ToUpperInvariant(part[0]) + part.Substring(1)));

            return first + rest;
        }
    }
}
