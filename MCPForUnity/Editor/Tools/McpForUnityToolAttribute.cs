using System;

namespace MCPForUnity.Editor.Tools
{
    /// <summary>
    /// Marks a class as an MCP tool handler for auto-discovery.
    /// The class must have a public static HandleCommand(JObject) method.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
    public class McpForUnityToolAttribute : Attribute
    {
        /// <summary>
        /// The command name used to route requests to this tool.
        /// If not specified, defaults to the PascalCase class name converted to snake_case.
        /// </summary>
        public string CommandName { get; }

        /// <summary>
        /// Create an MCP tool attribute with auto-generated command name.
        /// The command name will be derived from the class name (PascalCase → snake_case).
        /// Example: ManageAsset → manage_asset
        /// </summary>
        public McpForUnityToolAttribute()
        {
            CommandName = null; // Will be auto-generated
        }

        /// <summary>
        /// Create an MCP tool attribute with explicit command name.
        /// </summary>
        /// <param name="commandName">The command name (e.g., "manage_asset")</param>
        public McpForUnityToolAttribute(string commandName)
        {
            CommandName = commandName;
        }
    }
}
